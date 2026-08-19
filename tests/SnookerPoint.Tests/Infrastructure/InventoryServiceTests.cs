using SnookerPoint.Application.Catalog;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class InventoryServiceTests
{
    private static (int OwnerId, int ShiftId, int ProductId) Setup(Phase1Environment env, decimal opening = 100m, bool track = true, bool allowNegative = false)
    {
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId);
        var id = env.Products.Create(new CreateProductRequest(
            "Water", "SNK-WATER", "W1", cat, null, null, "500 ml", ProductUnit.Bottle,
            Money.FromRupees(20), Money.FromRupees(40), track, 10m, opening, AllowNegativeStock: allowNegative),
            ownerId, shiftId).Value;
        return (ownerId, shiftId, id);
    }

    private static int CreateCashier(Phase1Environment env)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private static void Record(Phase1Environment env, int productId, StockMovementType type, decimal qty, int actor, int shift, string? reason = "test")
    {
        var result = env.Inventory.RecordMovement(new StockMovementRequest(productId, type, qty, reason, shift), actor);
        Assert.True(result.Succeeded, result.ErrorMessage);
    }

    [Fact]
    public void StockIn_IncreasesInventory()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 10m);

        Record(env, id, StockMovementType.StockIn, 15m, owner, shift, null);
        Assert.Equal(25m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void NegativeAdjustment_DecreasesInventory()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 30m);

        Record(env, id, StockMovementType.NegativeAdjustment, 5m, owner, shift);
        Assert.Equal(25m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void Waste_DecreasesInventory()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 20m);

        Record(env, id, StockMovementType.Waste, 3m, owner, shift, "spillage");
        Assert.Equal(17m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void Damage_DecreasesInventory()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 20m);

        Record(env, id, StockMovementType.Damage, 4m, owner, shift, "broken");
        Assert.Equal(16m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void SupplierReturn_DecreasesInventory()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 20m);

        Record(env, id, StockMovementType.SupplierReturn, 6m, owner, shift, "returned to supplier");
        Assert.Equal(14m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void MovementThatWouldGoNegative_IsRejected()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 2m);

        var result = env.Inventory.RecordMovement(
            new StockMovementRequest(id, StockMovementType.Waste, 5m, "too much", shift), owner);
        Assert.True(result.Failed);
        Assert.Equal(2m, env.Inventory.GetCurrentStock(id)); // unchanged
    }

    [Fact]
    public void NegativeStock_IsAllowed_WhenProductOptsIn()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 2m, allowNegative: true);

        var result = env.Inventory.RecordMovement(
            new StockMovementRequest(id, StockMovementType.NegativeAdjustment, 5m, "correction", shift), owner);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(-3m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void Adjustment_RequiresReason()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 10m);

        var result = env.Inventory.RecordMovement(
            new StockMovementRequest(id, StockMovementType.NegativeAdjustment, 1m, "  ", shift), owner);
        Assert.True(result.Failed);
    }

    [Fact]
    public void Movements_AreAppendOnly_AndRecordBeforeAndAfter()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 10m);
        Record(env, id, StockMovementType.StockIn, 5m, owner, shift, null);

        var history = env.Inventory.GetHistory(id);
        Assert.Equal(2, history.Count); // opening + stock-in, nothing edited in place
        var stockIn = history.First(m => m.Type == StockMovementType.StockIn);
        Assert.Equal(10m, stockIn.PreviousQuantity);
        Assert.Equal(15m, stockIn.NewQuantity);
    }

    [Fact]
    public void Reversal_PreservesOriginal_AndCompensatesStock()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 10m);
        Record(env, id, StockMovementType.StockIn, 5m, owner, shift, null);
        Assert.Equal(15m, env.Inventory.GetCurrentStock(id));

        var stockIn = env.Inventory.GetHistory(id).First(m => m.Type == StockMovementType.StockIn);
        Assert.True(env.Inventory.ReverseMovement(stockIn.Id, "entered twice", owner, shift).Succeeded);

        // Original still present; a compensating movement brings stock back to 10.
        var history = env.Inventory.GetHistory(id);
        Assert.Contains(history, m => m.Id == stockIn.Id);                 // original untouched
        Assert.Contains(history, m => m.ReversalOfMovementId == stockIn.Id); // reversal appended
        Assert.Equal(10m, env.Inventory.GetCurrentStock(id));
    }

    [Fact]
    public void LowStock_And_OutOfStock_Statuses()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 8m); // reorder level 10 → low
        var low = env.Inventory.GetInventory(new InventoryFilter()).Single(r => r.ProductId == id);
        Assert.Equal(StockStatus.LowStock, low.Status);

        Record(env, id, StockMovementType.Waste, 8m, owner, shift, "emptied");
        var empty = env.Inventory.GetInventory(new InventoryFilter()).Single(r => r.ProductId == id);
        Assert.Equal(StockStatus.OutOfStock, empty.Status);
    }

    [Fact]
    public void NotTracked_Status()
    {
        using var env = new Phase1Environment();
        var (owner, shift, id) = Setup(env, opening: 0m, track: false);
        var row = env.Inventory.GetInventory(new InventoryFilter()).Single(r => r.ProductId == id);
        Assert.Equal(StockStatus.NotTracked, row.Status);
    }

    [Fact]
    public void UnauthorisedUser_CannotAdjustStock()
    {
        using var env = new Phase1Environment();
        var (_, shift, id) = Setup(env, opening: 10m);
        var cashier = CreateCashier(env);

        var result = env.Inventory.RecordMovement(
            new StockMovementRequest(id, StockMovementType.NegativeAdjustment, 1m, "no perm", shift), cashier);
        Assert.True(result.Failed);
    }
}
