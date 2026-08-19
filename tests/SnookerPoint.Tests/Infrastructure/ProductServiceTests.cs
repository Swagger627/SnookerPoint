using SnookerPoint.Application.Catalog;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class ProductServiceTests
{
    private static CreateProductRequest Cola(int categoryId, string sku = "SNK-C330", string? barcode = "0012345",
        long priceRupees = 60, long? costRupees = 35, decimal opening = 0m, bool track = true) =>
        new(
            Name: "Cola 330 ml",
            Sku: sku,
            Barcode: barcode,
            CategoryId: categoryId,
            Brand: "CoolCo",
            Variant: "Regular",
            Size: "330 ml",
            Unit: ProductUnit.Bottle,
            Cost: costRupees is { } c ? Money.FromRupees(c) : null,
            Price: Money.FromRupees(priceRupees),
            TrackInventory: track,
            ReorderLevel: 6m,
            OpeningQuantity: opening);

    private static int CreateCashier(Phase1Environment env)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public void Production_StartsWithZeroProducts()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);

        Assert.Empty(env.Products.GetList(new ProductFilter()));
    }

    [Fact]
    public void Create_AddsProduct()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);

        var result = env.Products.Create(Cola(cat), ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        var product = env.Products.Get(result.Value);
        Assert.NotNull(product);
        Assert.Equal("Cola 330 ml", product!.Name);
        Assert.Equal(Money.FromRupees(60).Paisa, product.Price.Paisa);
    }

    [Fact]
    public void Edit_UpdatesFields()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var id = env.Products.Create(Cola(cat), ownerId, shiftId).Value;

        var update = new UpdateProductRequest(id, "Cola 330 ml (new)", "SNK-C330", "0012345", cat,
            "CoolCo", "Regular", "330 ml", ProductUnit.Bottle, Money.FromRupees(40), Money.FromRupees(70), true, 8m);
        Assert.True(env.Products.Update(update, ownerId).Succeeded);

        var product = env.Products.Get(id)!;
        Assert.Equal("Cola 330 ml (new)", product.Name);
        Assert.Equal(Money.FromRupees(70).Paisa, product.Price.Paisa);
        Assert.Equal(8m, product.ReorderLevel);
    }

    [Fact]
    public void Duplicate_CreatesSeparateSku()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var sourceId = env.Products.Create(Cola(cat), ownerId, shiftId).Value;

        var dup = env.Products.Duplicate(sourceId, "SNK-C500", "0067890", ownerId);
        Assert.True(dup.Succeeded, dup.ErrorMessage);
        Assert.NotEqual(sourceId, dup.Value);

        var copy = env.Products.Get(dup.Value)!;
        Assert.Equal("SNK-C500", copy.Sku);
        Assert.Equal("0067890", copy.Barcode);
        Assert.Equal("Cola 330 ml", copy.Name); // copied name
    }

    [Fact]
    public void DifferentVariantsAndSizes_AreSeparateSkus()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);

        Assert.True(env.Products.Create(Cola(cat, "COLA-330", "A330"), ownerId, shiftId).Succeeded);
        Assert.True(env.Products.Create(Cola(cat, "COLA-500", "B500"), ownerId, shiftId).Succeeded);

        Assert.Equal(2, env.Products.GetList(new ProductFilter()).Count);
    }

    [Fact]
    public void DuplicateSku_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        env.Products.Create(Cola(cat, "SAME", "A1"), ownerId, shiftId);

        var second = env.Products.Create(Cola(cat, "SAME", "A2"), ownerId, shiftId);
        Assert.True(second.Failed);
    }

    [Fact]
    public void DuplicateBarcode_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        env.Products.Create(Cola(cat, "SKU1", "SAMEBARCODE"), ownerId, shiftId);

        var second = env.Products.Create(Cola(cat, "SKU2", "SAMEBARCODE"), ownerId, shiftId);
        Assert.True(second.Failed);
    }

    [Fact]
    public void Barcode_PreservesLeadingZeroes()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var id = env.Products.Create(Cola(cat, "SKU1", "0000123"), ownerId, shiftId).Value;

        Assert.Equal("0000123", env.Products.Get(id)!.Barcode);
        Assert.Equal(id, env.Products.FindByBarcode("0000123")!.Id);
    }

    [Fact]
    public void NegativePrice_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);

        var request = Cola(cat) with { Price = Money.FromPaisa(-1) };
        Assert.True(env.Products.Create(request, ownerId, shiftId).Failed);
    }

    [Fact]
    public void NegativeCost_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);

        var request = Cola(cat) with { Cost = Money.FromPaisa(-1) };
        Assert.True(env.Products.Create(request, ownerId, shiftId).Failed);
    }

    [Fact]
    public void OpeningQuantity_CreatesOpeningStockMovement()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var id = env.Products.Create(Cola(cat, opening: 24m), ownerId, shiftId).Value;

        var history = env.Inventory.GetHistory(id);
        var opening = Assert.Single(history);
        Assert.Equal(StockMovementType.OpeningStock, opening.Type);
        Assert.Equal(24m, opening.NewQuantity);
        Assert.Equal(24m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void ImagePathAndHash_AreStored()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);

        var request = Cola(cat) with { ImagePath = "Products/abc.png", ImageHash = "deadbeef", ImageOriginalName = "cola.png" };
        var id = env.Products.Create(request, ownerId, shiftId).Value;

        var product = env.Products.Get(id)!;
        Assert.Equal("Products/abc.png", product.ImagePath);
        Assert.Equal("deadbeef", product.ImageHash);
        Assert.Equal("cola.png", product.ImageOriginalName);
    }

    [Fact]
    public void Deactivation_PreservesStockHistory()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var id = env.Products.Create(Cola(cat, opening: 10m), ownerId, shiftId).Value;

        Assert.True(env.Products.SetActive(id, false, ownerId).Succeeded);

        // History and calculated stock survive deactivation.
        Assert.Single(env.Inventory.GetHistory(id));
        Assert.Equal(10m, env.Inventory.GetCurrentStock(id));
        Assert.False(env.Products.Get(id)!.IsActive);
    }

    [Fact]
    public void UnauthorisedUser_CannotCreateOrEdit()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var id = env.Products.Create(Cola(cat), ownerId, shiftId).Value;
        var cashier = CreateCashier(env);

        Assert.True(env.Products.Create(Cola(cat, "X", "X"), cashier, shiftId).Failed);

        var update = new UpdateProductRequest(id, "Hacked", "SNK-C330", "0012345", cat,
            null, null, null, ProductUnit.Bottle, null, Money.FromRupees(1), true, 0m);
        Assert.True(env.Products.Update(update, cashier).Failed);
    }

    [Fact]
    public void ActiveSessionsAndTables_AreUnaffectedByCatalogue()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        env.Products.Create(Cola(cat, opening: 5m), ownerId, shiftId);

        // A live table session is untouched by product/inventory activity.
        Assert.True(env.Sessions.StartSession(
            new SnookerPoint.Application.Tables.StartSessionRequest(tables[0], ownerId, shiftId, null, null)).Succeeded);
        var card = env.Sessions.GetDashboard().First(c => c.TableId == tables[0]);
        Assert.NotNull(card.Session);
    }
}
