using SnookerPoint.Application.Sales;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class SalePaymentTests
{
    private static (int SaleId, int OwnerId, int ShiftId) NewWalkin(Phase1Environment env, long priceRupees, decimal qty)
    {
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", priceRupees);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, qty, ownerId);
        return (saleId, ownerId, shiftId);
    }

    private static PaymentInput Pay(int methodId, long rupees, long? received = null, string? reference = null) =>
        new(methodId, Money.FromRupees(rupees), received is { } r ? Money.FromRupees(r) : null, reference, null);

    [Fact]
    public void CashPayment_ComputesChange()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 850, 1);

        var result = env.Sales.Complete(new CompleteSaleRequest(
            saleId, new[] { Pay(env.CashMethodId, 850, received: 1000) }, ownerId, shiftId));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(Money.FromRupees(150).Paisa, result.Value!.Change.Paisa);
    }

    [Fact]
    public void EasyPaisaPayment_Works_WithReference()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 500, 1);

        var result = env.Sales.Complete(new CompleteSaleRequest(
            saleId, new[] { Pay(env.MethodId("EasyPaisa"), 500, reference: "TXN123") }, ownerId, shiftId));

        Assert.True(result.Succeeded, result.ErrorMessage);
        var detail = env.SalesQuery.GetDetail(saleId)!;
        Assert.Contains(detail.Payments, p => p.MethodName == "EasyPaisa" && p.Reference == "TXN123");
    }

    [Fact]
    public void JazzCashAndBankTransfer_Work()
    {
        using var env = new Phase1Environment();
        var (jazz, ownerId1, shift1) = NewWalkin(env, 300, 1);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(jazz, new[] { Pay(env.MethodId("JazzCash"), 300) }, ownerId1, shift1)).Succeeded);

        var bankSale = env.Sales.CreateWalkinDraft(ownerId1).Value;
        var p2 = env.SeedProduct(ownerId1, shift1, "P2", 400);
        env.Sales.AddProduct(bankSale, p2, 1, ownerId1);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(bankSale, new[] { Pay(env.MethodId("Bank Transfer"), 400) }, ownerId1, shift1)).Succeeded);
    }

    [Fact]
    public void SplitPayment_CashPlusElectronic_Completes()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 1000, 1);

        var result = env.Sales.Complete(new CompleteSaleRequest(
            saleId, new[] { Pay(env.MethodId("EasyPaisa"), 500), Pay(env.CashMethodId, 500) }, ownerId, shiftId));

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(2, env.SalesQuery.GetDetail(saleId)!.Payments.Count);
    }

    [Fact]
    public void Underpayment_IsRejected()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 1000, 1);

        var result = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Pay(env.CashMethodId, 500) }, ownerId, shiftId));
        Assert.True(result.Failed);
    }

    [Fact]
    public void NonCashOverpayment_IsRejected()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 500, 1);

        var result = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Pay(env.MethodId("JazzCash"), 600) }, ownerId, shiftId));
        Assert.True(result.Failed);
    }

    [Fact]
    public void Payment_RequiresOpenShift()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 200, 1);
        env.Shifts.CloseShift(shiftId, Money.Zero, null); // no open shift

        var result = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Pay(env.CashMethodId, 200) }, ownerId, shiftId));
        Assert.True(result.Failed);
        Assert.Contains("shift", result.ErrorMessage);
    }

    // ---------- Shift integration ----------

    [Fact]
    public void CashSale_AffectsExpectedShiftCash_ElectronicDoesNot()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var product = env.SeedProduct(ownerId, shiftId, "P1", 500, opening: 100m);

        // Cash sale of 500.
        var cashSale = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(cashSale, product, 1, ownerId);
        env.Sales.Complete(new CompleteSaleRequest(cashSale, new[] { Pay(env.CashMethodId, 500, received: 500) }, ownerId, shiftId));

        // Electronic sale of 500.
        var epSale = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(epSale, product, 1, ownerId);
        env.Sales.Complete(new CompleteSaleRequest(epSale, new[] { Pay(env.MethodId("EasyPaisa"), 500) }, ownerId, shiftId));

        var summary = env.Shifts.GetCurrentShift(ownerId)!;
        // Opening 0 + cash sale 500 = expected 500 (electronic excluded).
        Assert.Equal(Money.FromRupees(500).Paisa, summary.ExpectedCash.Paisa);
        Assert.Equal(Money.FromRupees(500).Paisa, summary.CashSales.Paisa);
        Assert.Equal(Money.FromRupees(500).Paisa, summary.ElectronicSales.Paisa);
        Assert.Equal(Money.FromRupees(1000).Paisa, summary.GrossSales.Paisa);
        Assert.Equal(2, summary.SaleCount);
        Assert.Contains(summary.PaymentTotals, t => t.MethodName == "EasyPaisa" && t.Total.Paisa == Money.FromRupees(500).Paisa);
    }

    [Fact]
    public void CashChange_IsNotCountedAsExtraCash()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var product = env.SeedProduct(ownerId, shiftId, "P1", 850, opening: 100m);
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, product, 1, ownerId);

        // Pay 850 with 1000 received → 150 change. Expected cash increases by 850 only.
        env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Pay(env.CashMethodId, 850, received: 1000) }, ownerId, shiftId));

        Assert.Equal(Money.FromRupees(850).Paisa, env.Shifts.GetCurrentShift(ownerId)!.ExpectedCash.Paisa);
    }

    // ---------- Receipts & history ----------

    [Fact]
    public void CompletedSale_StoresReceiptSnapshot_AndReprintMarksReprint()
    {
        using var env = new Phase1Environment();
        var (saleId, ownerId, shiftId) = NewWalkin(env, 200, 1);
        env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Pay(env.CashMethodId, 200) }, ownerId, shiftId));

        var snapshot = env.SalesQuery.GetReceiptSnapshot(saleId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot));
        Assert.DoesNotContain("REPRINT", snapshot);

        Assert.True(env.SalesQuery.MarkReceiptPrinted(saleId, ownerId, isReprint: true).Succeeded);
        Assert.Equal(1, env.SalesQuery.GetDetail(saleId)!.PrintCount);
    }

    [Fact]
    public void SalesHistory_FiltersByType()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var product = env.SeedProduct(ownerId, shiftId, "P1", 60);

        var walkin = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(walkin, product, 1, ownerId);
        env.Sales.Complete(new CompleteSaleRequest(walkin, new[] { Pay(env.CashMethodId, 60) }, ownerId, shiftId));

        Assert.True(env.Sessions.StartSession(new SnookerPoint.Application.Tables.StartSessionRequest(tables[0], ownerId, shiftId, null, null)).Succeeded);
        var sid = env.Sessions.GetDashboard().First(c => c.TableId == tables[0]).Session!.SessionId;
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(sid, ownerId, shiftId, null);
        var tableSale = env.Sales.CreateTableCheckoutDraft(sid, ownerId).Value;
        env.Sales.Complete(new CompleteSaleRequest(tableSale, new[] { Pay(env.CashMethodId, 120) }, ownerId, shiftId));

        Assert.Equal(2, env.SalesQuery.GetHistory(new SalesHistoryFilter()).Count);
        Assert.Single(env.SalesQuery.GetHistory(new SalesHistoryFilter(Type: SaleType.Walkin)));
        Assert.Single(env.SalesQuery.GetHistory(new SalesHistoryFilter(Type: SaleType.Table)));
    }

    // ---------- Payment methods ----------

    [Fact]
    public void CashMethod_CannotBeDeactivated()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        Assert.True(env.PaymentMethods.SetActive(env.CashMethodId, false, ownerId).Failed);
    }
}
