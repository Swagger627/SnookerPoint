using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class SaleServiceTests
{
    private static int CreateUser(Phase1Environment env, UserRole role)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = role.ToString(), Username = role + "-" + Guid.NewGuid().ToString("N")[..6], Role = role, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private static PaymentInput Cash(Phase1Environment env, long rupees, long? received = null) =>
        new(env.CashMethodId, Money.FromRupees(rupees), received is { } r ? Money.FromRupees(r) : null, null, null);

    private static int FinishSession(Phase1Environment env, int tableId, int ownerId, int shiftId, int minutes)
    {
        Assert.True(env.Sessions.StartSession(new StartSessionRequest(tableId, ownerId, shiftId, "VIP", null)).Succeeded);
        var id = env.Sessions.GetDashboard().First(c => c.TableId == tableId).Session!.SessionId;
        env.Clock.Advance(TimeSpan.FromMinutes(minutes));
        Assert.True(env.Sessions.FinishSession(id, ownerId, shiftId, null).Succeeded);
        return id;
    }

    // ---------- Baseline ----------

    [Fact]
    public void Production_StartsWithNoSales()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);
        Assert.Empty(env.SalesQuery.GetHistory(new SalesHistoryFilter()));
    }

    // ---------- Walk-in ----------

    [Fact]
    public void WalkinSale_CompletesAndDeductsInventoryOnce()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees: 60, opening: 10m);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        Assert.True(env.Sales.AddProduct(saleId, productId, 2, ownerId).Succeeded);

        var complete = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 120) }, ownerId, shiftId));
        Assert.True(complete.Succeeded, complete.ErrorMessage);
        Assert.Equal(Money.FromRupees(120).Paisa, complete.Value!.Total.Paisa);

        Assert.Equal(8m, env.Inventory.GetCurrentStock(productId)); // 10 − 2
        Assert.Single(env.SalesQuery.GetHistory(new SalesHistoryFilter()));

        // Exactly one Sale stock movement.
        var saleMovements = env.Inventory.GetHistory(productId).Count(m => m.Type == StockMovementType.Sale);
        Assert.Equal(1, saleMovements);
    }

    [Fact]
    public void RepeatedProduct_MergesIntoOneLine_IncreasingQuantity()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        env.Sales.AddProduct(saleId, productId, 1, ownerId);

        var draft = env.Sales.GetDraft(saleId)!;
        var line = Assert.Single(draft.Lines);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public void InactiveProduct_CannotBeAdded()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        env.Products.SetActive(productId, false, ownerId);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        Assert.True(env.Sales.AddProduct(saleId, productId, 1, ownerId).Failed);
    }

    [Fact]
    public void ZeroQuantity_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;

        Assert.True(env.Sales.AddProduct(saleId, productId, 0, ownerId).Failed);
    }

    [Fact]
    public void UntrackedProduct_SellsWithoutStockMovement()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 0m, track: false);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 3, ownerId);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 180) }, ownerId, shiftId)).Succeeded);

        Assert.Empty(env.Inventory.GetHistory(productId));
    }

    // ---------- Table checkout ----------

    [Fact]
    public void TableOnlyCheckout_ImportsFrozenChargeExactly_AndMarksCheckedOut()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var sessionId = FinishSession(env, tables[0], ownerId, shiftId, 60); // 12000 paisa charge

        var saleId = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Value;
        var draft = env.Sales.GetDraft(saleId)!;
        Assert.Equal(12_000, draft.TableCharge.Paisa);            // imported exactly
        Assert.Empty(draft.Lines);                               // table-only allowed

        var complete = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 120) }, ownerId, shiftId));
        Assert.True(complete.Succeeded, complete.ErrorMessage);

        using var db = env.NewContext();
        Assert.Equal(CheckoutStatus.CheckedOut, db.TableSessions.Single(s => s.Id == sessionId).CheckoutStatus);
    }

    [Fact]
    public void CombinedTableAndProducts_SumCorrectly()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 10m);
        var sessionId = FinishSession(env, tables[0], ownerId, shiftId, 60); // 12000

        var saleId = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 2, ownerId); // 12000

        var draft = env.Sales.GetDraft(saleId)!;
        Assert.Equal(24_000, draft.Totals.Total.Paisa); // 12000 table + 12000 products
    }

    [Fact]
    public void SameSession_CannotBeCheckedOutTwice()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var sessionId = FinishSession(env, tables[0], ownerId, shiftId, 60);

        var saleId = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Value;
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 120) }, ownerId, shiftId)).Succeeded);

        // A second checkout attempt on the same (now checked-out) session fails.
        Assert.True(env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Failed);
    }

    [Fact]
    public void SameSession_CannotBeAttachedToTwoActiveDrafts()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var sessionId = FinishSession(env, tables[0], ownerId, shiftId, 60);

        var first = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Value;
        var second = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Value;
        Assert.Equal(first, second); // resumes the same draft, never a second one
    }

    // ---------- Drafts ----------

    [Fact]
    public void Draft_PersistsAcrossRestart_WithoutDeductingInventory()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 10m);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 2, ownerId);

        // A fresh service instance reads purely from SQLite.
        var restarted = new SnookerPoint.Infrastructure.Services.SaleService(
            env.Factory, new SnookerPoint.Application.Security.PermissionService(), env.Clock,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SnookerPoint.Infrastructure.Services.SaleService>.Instance);

        var draft = restarted.GetDraft(saleId)!;
        Assert.Single(draft.Lines);
        Assert.Equal(10m, env.Inventory.GetCurrentStock(productId)); // no deduction for a draft
    }

    [Fact]
    public void CancelledDraft_DeductsNothing()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 10m);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 2, ownerId);
        Assert.True(env.Sales.Cancel(saleId, ownerId).Succeeded);

        Assert.Equal(10m, env.Inventory.GetCurrentStock(productId));
        Assert.Empty(env.SalesQuery.GetHistory(new SalesHistoryFilter()));
    }

    [Fact]
    public void HeldSale_ReopensAndCompletes()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);

        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        Assert.True(env.Sales.Hold(saleId, "Table 5 tab", ownerId).Succeeded);
        Assert.Contains(env.Sales.GetHeldSales(), h => h.SaleId == saleId);

        Assert.True(env.Sales.Reopen(saleId, ownerId).Succeeded);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 60) }, ownerId, shiftId)).Succeeded);
    }

    // ---------- Discounts & override ----------

    [Fact]
    public void FixedDiscount_Applies()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 100);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 2, ownerId); // 200

        Assert.True(env.Sales.ApplyDiscount(saleId, DiscountKind.FixedAmount, Money.FromRupees(50).Paisa, "loyal customer", ownerId).Succeeded);
        Assert.Equal(Money.FromRupees(150).Paisa, env.Sales.GetDraft(saleId)!.Totals.Total.Paisa);
    }

    [Fact]
    public void PriceOverride_RequiresPermission_AndPreservesOriginal()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 100);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        var lineId = env.Sales.GetDraft(saleId)!.Lines.Single().LineId;

        // A floor-staff user cannot override price.
        var floor = CreateUser(env, UserRole.FloorStaff);
        Assert.True(env.Sales.OverrideLinePrice(saleId, lineId, Money.FromRupees(80), "damaged", floor).Failed);

        // An owner can, and the original is preserved.
        Assert.True(env.Sales.OverrideLinePrice(saleId, lineId, Money.FromRupees(80), "damaged", ownerId).Succeeded);
        var line = env.Sales.GetDraft(saleId)!.Lines.Single();
        Assert.Equal(Money.FromRupees(80).Paisa, line.UnitPrice.Paisa);
        Assert.Equal(Money.FromRupees(100).Paisa, line.OriginalUnitPrice!.Value.Paisa);
    }

    // ---------- Idempotency & completed immutability ----------

    [Fact]
    public void CompletedSale_CannotBeCompletedAgain_NoDuplicateStock()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 10m);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 2, ownerId);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 120) }, ownerId, shiftId)).Succeeded);

        // A retry is rejected, and stock is untouched.
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 120) }, ownerId, shiftId)).Failed);
        Assert.Equal(8m, env.Inventory.GetCurrentStock(productId));
        Assert.Equal(1, env.Inventory.GetHistory(productId).Count(m => m.Type == StockMovementType.Sale));
    }

    [Fact]
    public void CompletedSale_IsNotEditable()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 60) }, ownerId, shiftId));

        Assert.True(env.Sales.AddProduct(saleId, productId, 1, ownerId).Failed);
        Assert.True(env.Sales.Cancel(saleId, ownerId).Failed);
    }

    [Fact]
    public void FailedCheckout_KeepsSessionAwaitingCheckout()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        // Product tracked with only 1 in stock; try to sell 5 → stock validation fails → rollback.
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 1m);
        var sessionId = FinishSession(env, tables[0], ownerId, shiftId, 60);
        var saleId = env.Sales.CreateTableCheckoutDraft(sessionId, ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 5, ownerId);

        var complete = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 420) }, ownerId, shiftId));
        Assert.True(complete.Failed);

        using var db = env.NewContext();
        Assert.Equal(CheckoutStatus.AwaitingCheckout, db.TableSessions.Single(s => s.Id == sessionId).CheckoutStatus);
        Assert.Empty(env.SalesQuery.GetHistory(new SalesHistoryFilter())); // nothing completed
    }
}
