using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Settings;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class OperationalSettingsTests
{
    private static PaymentInput Cash(Phase1Environment env, long rupees) =>
        new(env.CashMethodId, Money.FromRupees(rupees), Money.FromRupees(rupees), null, null);

    private static int CompleteWalkin(Phase1Environment env, int ownerId, int shiftId, int productId, decimal qty, long cashRupees)
    {
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        Assert.True(env.Sales.AddProduct(saleId, productId, qty, ownerId).Succeeded);
        var complete = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, cashRupees) }, ownerId, shiftId));
        Assert.True(complete.Succeeded, complete.ErrorMessage);
        return complete.Value!.SaleId;
    }

    private static int CreateUser(Phase1Environment env, UserRole role)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = role.ToString(), Username = role + "-" + Guid.NewGuid().ToString("N")[..6], Role = role, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public void TaxAndService_DefaultToZeroAndDisabled()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);

        var s = env.OperationalSettings.Get()!;
        Assert.False(s.TaxEnabled);
        Assert.False(s.ServiceChargeEnabled);
        Assert.Equal(0m, s.TaxPercent);
        Assert.Equal(0m, s.ServiceChargePercent);
    }

    [Fact]
    public void EnablingTax_AppliesToNewSalesOnly_NotExistingSales()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 100);

        // Sale BEFORE tax: Rs 100.
        var beforeId = CompleteWalkin(env, ownerId, shiftId, productId, 1, 100);

        // Enable 10% tax.
        Assert.True(env.OperationalSettings.UpdateTaxService(new TaxServiceInput(true, 10m, false, 0m), ownerId).Succeeded);

        // Sale AFTER tax: Rs 110.
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        var afterComplete = env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { Cash(env, 110) }, ownerId, shiftId));
        Assert.True(afterComplete.Succeeded, afterComplete.ErrorMessage);

        using var db = env.NewContext();
        var before = db.Sales.First(s => s.Id == beforeId);
        var after = db.Sales.First(s => s.Id == afterComplete.Value!.SaleId);

        Assert.Equal(Money.FromRupees(100).Paisa, before.Total.Paisa);   // unchanged
        Assert.True(before.TaxAmount.IsZero);
        Assert.Equal(Money.FromRupees(110).Paisa, after.Total.Paisa);    // 100 + 10% tax
        Assert.Equal(Money.FromRupees(10).Paisa, after.TaxAmount.Paisa);
    }

    [Fact]
    public void UpdateTaxService_RequiresPermission()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);
        var cashier = CreateUser(env, UserRole.Cashier);

        var result = env.OperationalSettings.UpdateTaxService(new TaxServiceInput(true, 5m, false, 0m), cashier);
        Assert.True(result.Failed);
        Assert.Contains("permission", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateTaxService_RejectsOutOfRangePercent()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        Assert.True(env.OperationalSettings.UpdateTaxService(new TaxServiceInput(true, 150m, false, 0m), ownerId).Failed);
    }

    [Fact]
    public void UpdateBackupSettings_RejectsRetentionBelowOne()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        Assert.True(env.OperationalSettings.UpdateBackupSettings(new BackupSettingsInput(true, true, false, 0, null), ownerId).Failed);
    }
}
