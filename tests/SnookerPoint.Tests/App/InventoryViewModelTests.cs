using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>Covers the Inventory screen view model: recording movements and feedback, headless.</summary>
public class InventoryViewModelTests
{
    private static (InventoryViewModel Vm, FakeDialogService Dialogs, Phase1Environment Env, int ProductId)
        Create(Phase1Environment env, decimal opening = 20m)
    {
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Drinks");
        var productId = env.Products.Create(new CreateProductRequest(
            "Water", "W1", "W-BC", cat, null, null, "500 ml", ProductUnit.Bottle,
            Money.FromRupees(20), Money.FromRupees(40), true, 10m, opening), ownerId, shiftId).Value;

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, HasPin: false));
        var dialogs = new FakeDialogService();
        var vm = new InventoryViewModel(env.Inventory, env.Categories, env.Shifts, session,
            new PermissionService(), dialogs, new FakeNavigationService(), new FakeThemeService(), new FakeLicenseGate());
        return (vm, dialogs, env, productId);
    }

    private static InventoryRowViewModel Row(InventoryViewModel vm) => vm.Rows.First();

    [Fact]
    public void StockIn_UpdatesRowAndShowsSuccess()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, envRef, productId) = Create(env, opening: 20m);
        dialogs.StockMovementResult = new StockMovementResult(StockMovementType.StockIn, 5m, null);

        vm.StockInCommand.Execute(Row(vm));

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Equal(25m, envRef.Inventory.GetCurrentStock(productId));
    }

    [Fact]
    public void Waste_BeyondStock_ShowsError()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _) = Create(env, opening: 2m);
        dialogs.StockMovementResult = new StockMovementResult(StockMovementType.Waste, 5m, "too much");

        vm.WasteCommand.Execute(Row(vm));

        Assert.Equal(FeedbackKind.Error, vm.Feedback.Kind);
    }

    [Fact]
    public void History_OpensDialog()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _) = Create(env);

        vm.HistoryCommand.Execute(Row(vm));

        Assert.True(dialogs.StockHistoryShown);
    }

    [Fact]
    public void LowStockFilter_ShowsOnlyLowRows()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _) = Create(env, opening: 100m); // above reorder of 10 → in stock
        Assert.Single(vm.Rows);

        vm.LowStockOnly = true;
        Assert.Empty(vm.Rows);
    }
}
