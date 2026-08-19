using SnookerPoint.Application.Catalog;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class ProductCsvServiceTests
{
    private const string Header =
        "SKU,Barcode,ProductName,Category,Brand,Variant,Size,Unit,PurchaseCost,SellingPrice,TrackInventory,OpeningQuantity,ReorderLevel,Active\r\n";

    private static string Row(string sku, string barcode, string name, string category, string price,
        string opening = "0", string track = "true", string active = "true") =>
        $"{sku},{barcode},{name},{category},Brand,Variant,Size,Each,10,{price},{track},{opening},5,{active}\r\n";

    [Fact]
    public void Preview_FlagsValidAndInvalidRows()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);

        var csv = Header
            + Row("SKU-1", "B1", "Good", "Drinks", "60")
            + Row("SKU-2", "B2", "Bad", "Drinks", "-5");   // negative price

        var preview = env.ProductCsv.Preview(csv);
        Assert.Equal(1, preview.ValidCount);
        Assert.Equal(1, preview.InvalidCount);
        Assert.Contains(preview.Rows, r => !r.IsValid && r.Sku == "SKU-2");
    }

    [Fact]
    public void Import_AddsProducts_AndCreatesOpeningMovements()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);

        var csv = Header + Row("SKU-1", "B1", "Cola", "Drinks", "60", opening: "12");
        var result = env.ProductCsv.Import(csv, CsvDuplicateStrategy.Skip, ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, result.Value!.Added);

        var product = env.Products.GetList(new ProductFilter()).Single();
        Assert.Equal(12m, env.Inventory.GetCurrentStock(product.Id));
        Assert.Contains(env.Inventory.GetHistory(product.Id), m => m.Type == StockMovementType.OpeningStock);
        // Category was auto-created.
        Assert.Contains(env.Categories.GetAll(), c => c.Name == "Drinks");
    }

    [Fact]
    public void Import_RollsBack_WhenAnyRowIsInvalid()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);

        var csv = Header
            + Row("SKU-1", "B1", "Good", "Drinks", "60")
            + Row("SKU-2", "B2", "Bad", "Drinks", "-5"); // invalid → whole import blocked

        var result = env.ProductCsv.Import(csv, CsvDuplicateStrategy.Skip, ownerId, shiftId);

        Assert.True(result.Failed);
        Assert.Empty(env.Products.GetList(new ProductFilter())); // nothing imported
    }

    [Fact]
    public void Import_Skip_DoesNotChangeExistingPrice()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Drinks");
        var id = env.Products.Create(new CreateProductRequest(
            "Cola", "EXIST", "B9", cat, null, null, null, ProductUnit.Each, null, Money.FromRupees(60), true, 5m),
            ownerId, shiftId).Value;

        var csv = Header + Row("EXIST", "B9", "Cola", "Drinks", "999");
        var result = env.ProductCsv.Import(csv, CsvDuplicateStrategy.Skip, ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, result.Value!.Skipped);
        Assert.Equal(Money.FromRupees(60).Paisa, env.Products.Get(id)!.Price.Paisa); // unchanged
    }

    [Fact]
    public void Import_UpdateBySku_ChangesPrice()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Drinks");
        var id = env.Products.Create(new CreateProductRequest(
            "Cola", "EXIST", "B9", cat, null, null, null, ProductUnit.Each, null, Money.FromRupees(60), true, 5m),
            ownerId, shiftId).Value;

        var csv = Header + Row("EXIST", "B9", "Cola", "Drinks", "80");
        var result = env.ProductCsv.Import(csv, CsvDuplicateStrategy.UpdateBySku, ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(Money.FromRupees(80).Paisa, env.Products.Get(id)!.Price.Paisa);
    }

    [Fact]
    public void Export_ContainsProduct()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Drinks");
        env.Products.Create(new CreateProductRequest(
            "Cola 330", "SNK-C330", "0012345", cat, null, null, null, ProductUnit.Bottle, null, Money.FromRupees(60), true, 5m, 7m),
            ownerId, shiftId);

        var products = env.ProductCsv.ExportProducts();
        Assert.Contains("SNK-C330", products);
        Assert.Contains("Cola 330", products);

        var summary = env.ProductCsv.ExportStockSummary();
        Assert.Contains("SNK-C330", summary);
        Assert.Contains("7", summary); // current stock from opening quantity
    }

    [Fact]
    public void UnauthorisedUser_CannotImport()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        int cashier;
        using (var db = env.NewContext())
        {
            var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
            db.Users.Add(user);
            db.SaveChanges();
            cashier = user.Id;
        }

        var csv = Header + Row("SKU-1", "B1", "Cola", "Drinks", "60");
        Assert.True(env.ProductCsv.Import(csv, CsvDuplicateStrategy.Skip, cashier, shiftId).Failed);
    }
}
