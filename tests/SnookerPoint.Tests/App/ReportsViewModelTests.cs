using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

public class ReportsViewModelTests
{
    private static ReportsViewModel Create(Phase1Environment env, int userId, UserRole role)
    {
        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(userId, role.ToString(), role.ToString().ToLower(), role, HasPin: false));
        return new ReportsViewModel(env.Reporting, env.Csv, env.StaffManagement, env.PaymentMethods, env.TableManagement,
            env.Products, env.Categories, session, new PermissionService(),
            new FakeDialogService(), new FakeNavigationService(), new FakeThemeService(), env.Clock);
    }

    private static int CreateUser(Phase1Environment env, UserRole role)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = role.ToString(), Username = role + "-" + Guid.NewGuid().ToString("N")[..6], Role = role, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    private static void CompleteSale(Phase1Environment env, int ownerId, int shiftId, int productId)
    {
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        var pay = new PaymentInput(env.CashMethodId, Money.FromRupees(60), Money.FromRupees(60), null, null);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { pay }, ownerId, shiftId)).Succeeded);
    }

    [Fact]
    public void Dashboard_LoadsForOwner()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var vm = Create(env, ownerId, UserRole.Owner);

        Assert.NotNull(vm.Table);
        Assert.Equal("Dashboard", vm.Table!.Title);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public void SalesSection_ShowsCompletedSale_WithinCustomRange()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        CompleteSale(env, ownerId, shiftId, productId);

        var vm = Create(env, ownerId, UserRole.Owner);
        vm.CustomFrom = new DateTime(2020, 1, 1);
        vm.CustomTo = new DateTime(2029, 12, 31);
        vm.SelectedPreset = vm.Presets.First(p => p.Value == SnookerPoint.Application.Reporting.ReportPreset.Custom);
        vm.ShowCommand.Execute("Sales");

        Assert.Equal("Sales", vm.Table!.Title);
        Assert.Single(vm.Table.Rows);

        // Export writes a file to the exports folder.
        vm.ExportCommand.Execute(null);
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.True(File.Exists(vm.LastExportPath));
    }

    [Fact]
    public void SalesSection_IsEmpty_WhenRangeExcludesTheSale()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        CompleteSale(env, ownerId, shiftId, productId);

        var vm = Create(env, ownerId, UserRole.Owner);
        vm.CustomFrom = new DateTime(2019, 1, 1);
        vm.CustomTo = new DateTime(2019, 12, 31);
        vm.SelectedPreset = vm.Presets.First(p => p.Value == SnookerPoint.Application.Reporting.ReportPreset.Custom);
        vm.ShowCommand.Execute("Sales");

        Assert.True(vm.Table!.IsEmpty);
    }

    [Fact]
    public void FloorStaff_CannotExport()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);
        var floor = CreateUser(env, UserRole.FloorStaff);
        var vm = Create(env, floor, UserRole.FloorStaff);

        Assert.False(vm.CanExport);
        vm.ExportCommand.Execute(null);
        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
    }

    // ---------- Sales report filters (Phase 6.1) ----------

    private static void UseAllTime(ReportsViewModel vm)
    {
        vm.CustomFrom = new DateTime(2020, 1, 1);
        vm.CustomTo = new DateTime(2029, 12, 31);
        vm.SelectedPreset = vm.Presets.First(p => p.Value == SnookerPoint.Application.Reporting.ReportPreset.Custom);
    }

    private static void CompleteWith(Phase1Environment env, int ownerId, int shiftId, int productId, PaymentInput pay)
    {
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        Assert.True(env.Sales.Complete(new CompleteSaleRequest(saleId, new[] { pay }, ownerId, shiftId)).Succeeded);
    }

    [Fact]
    public void SalesReport_ExposesAllFilters()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var vm = Create(env, ownerId, UserRole.Owner);
        vm.ShowCommand.Execute("Sales");

        Assert.True(vm.IsSalesSection);
        Assert.Contains(vm.Cashiers, c => c.Id is null);       // "All cashiers"
        Assert.Contains(vm.Cashiers, c => c.Id == ownerId);    // a real cashier
        Assert.Equal(3, vm.SaleTypes.Count);                   // All / Walk-in / Table
        Assert.Contains(vm.Methods, m => m.Name == "EasyPaisa");
        Assert.True(vm.SalesTables.Count >= 2);                // All + at least one table
        Assert.Equal(3, vm.SaleStatuses.Count);                // Completed-only / Completed / Cancelled
    }

    [Fact]
    public void CombinedFilters_ReturnCorrectRows()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        CompleteWith(env, ownerId, shiftId, productId, new PaymentInput(env.CashMethodId, Money.FromRupees(60), Money.FromRupees(60), null, null));
        CompleteWith(env, ownerId, shiftId, productId, new PaymentInput(env.MethodId("EasyPaisa"), Money.FromRupees(60), null, "ref", null));

        var vm = Create(env, ownerId, UserRole.Owner);
        UseAllTime(vm);
        vm.ShowCommand.Execute("Sales");
        Assert.Equal(2, vm.Table!.Rows.Count);

        // Filter by payment method (split-payment aware) — only the EasyPaisa sale.
        vm.SelectedMethod = vm.Methods.First(m => m.Name == "EasyPaisa");
        Assert.Single(vm.Table!.Rows);

        // Combine with a cashier filter (both sales are the owner's) — still one row.
        vm.SelectedCashier = vm.Cashiers.First(c => c.Id == ownerId);
        Assert.Single(vm.Table!.Rows);

        // Combine with a sale-type filter that excludes walk-ins — no rows.
        vm.SelectedSaleType = vm.SaleTypes.First(t => t.Value == SaleType.Table);
        Assert.True(vm.Table!.IsEmpty);
    }

    [Fact]
    public void ResetFilters_RestoresDefaults()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var vm = Create(env, ownerId, UserRole.Owner);
        vm.ShowCommand.Execute("Sales");

        vm.SelectedMethod = vm.Methods.Last();
        vm.SelectedSaleType = vm.SaleTypes.First(t => t.Value == SaleType.Table);
        vm.SelectedSaleStatus = vm.SaleStatuses.First(s => s.Value == SaleStatus.Cancelled);

        vm.ResetFiltersCommand.Execute(null);

        Assert.Null(vm.SelectedMethod.Id);
        Assert.Null(vm.SelectedSaleType.Value);
        Assert.Null(vm.SelectedSaleStatus.Value);
        Assert.Null(vm.SelectedCashier.Id);
        Assert.Null(vm.SelectedSalesTable.Id);
        Assert.Equal(SnookerPoint.Application.Reporting.ReportPreset.Today, vm.SelectedPreset.Value);
    }

    [Fact]
    public void Export_UsesActiveFilters()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60);
        CompleteWith(env, ownerId, shiftId, productId, new PaymentInput(env.CashMethodId, Money.FromRupees(60), Money.FromRupees(60), null, null));
        CompleteWith(env, ownerId, shiftId, productId, new PaymentInput(env.MethodId("EasyPaisa"), Money.FromRupees(60), null, "ref", null));

        var vm = Create(env, ownerId, UserRole.Owner);
        UseAllTime(vm);
        vm.ShowCommand.Execute("Sales");
        vm.SelectedMethod = vm.Methods.First(m => m.Name == "EasyPaisa"); // one row

        vm.ExportCommand.Execute(null);
        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);

        var lines = File.ReadAllLines(vm.LastExportPath!).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        Assert.Equal(1 + vm.Table!.Rows.Count, lines.Count); // header + filtered rows
        Assert.Equal(2, lines.Count);                        // header + exactly one filtered sale
    }
}
