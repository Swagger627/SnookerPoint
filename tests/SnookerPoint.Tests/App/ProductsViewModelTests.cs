using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Covers the Products screen view model: adding a product through the editor, the
/// barcode-scan lookup flow, permission gating, and clear feedback — all headless.
/// </summary>
public class ProductsViewModelTests
{
    private static (ProductsViewModel Vm, FakeDialogService Dialogs, FakeNavigationService Nav, Phase1Environment Env, int OwnerId)
        Create(Phase1Environment env, UserRole role = UserRole.Owner)
    {
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var actorId = ownerId;
        if (role != UserRole.Owner)
        {
            using var db = env.NewContext();
            var user = new SnookerPoint.Domain.Entities.User
            {
                DisplayName = role.ToString(), Username = role.ToString().ToLower(), Role = role, PasswordHash = "x", IsActive = true,
            };
            db.Users.Add(user);
            db.SaveChanges();
            actorId = user.Id;
        }

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(actorId, "Actor", "actor", role, HasPin: false));
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var vm = new ProductsViewModel(env.Products, env.Categories, env.ProductCsv, env.Images, env.Paths,
            session, new PermissionService(), dialogs, nav, new FakeThemeService());
        return (vm, dialogs, nav, env, ownerId);
    }

    private static ProductEditorResult Editor(string name, string sku, string? barcode, int categoryId, decimal opening = 0m) =>
        new(name, sku, barcode, categoryId, null, null, null, ProductUnit.Each, null, Money.FromRupees(60),
            true, false, 5m, opening, null, ProductImageAction.Keep, null);

    [Fact]
    public void EmptyCatalogue_ShowsEmptyState()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void AddProduct_ThroughEditor_CreatesAndShowsSuccess()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _, ownerId) = Create(env);
        var cat = env.SeedCategory(ownerId, "Drinks");
        dialogs.ProductEditorResult = Editor("Cola 330", "C330", "0012345", cat, opening: 10m);

        vm.AddProductCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.False(vm.IsEmpty);
        Assert.Contains(vm.Rows, r => r.Sku == "C330");
    }

    [Fact]
    public void AddProduct_WithNoCategory_WarnsInsteadOfOpeningEditor()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env);

        vm.AddProductCommand.Execute(null);

        Assert.Equal(FeedbackKind.Warning, vm.Feedback.Kind);
        Assert.True(vm.IsEmpty);
    }

    [Fact]
    public void Scan_KnownBarcode_ReportsFound()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _, ownerId) = Create(env);
        var cat = env.SeedCategory(ownerId, "Drinks");
        dialogs.ProductEditorResult = Editor("Cola", "C1", "555000", cat);
        vm.AddProductCommand.Execute(null);

        vm.SearchText = "555000";
        vm.ScanCommand.Execute(null);

        Assert.Equal(FeedbackKind.Success, vm.Feedback.Kind);
        Assert.Contains("Found", vm.Feedback.Message);
    }

    [Fact]
    public void Scan_UnknownBarcode_AsManager_OpensEditorPrefilled()
    {
        using var env = new Phase1Environment();
        var (vm, dialogs, _, _, ownerId) = Create(env);
        env.SeedCategory(ownerId, "Drinks");
        // Editor returns null (cancelled) but we can assert the prefilled barcode was offered.
        dialogs.ProductEditorResult = null;

        vm.SearchText = "0009999";
        vm.ScanCommand.Execute(null);

        Assert.Equal(FeedbackKind.Warning, vm.Feedback.Kind);
        Assert.Contains("0009999", vm.Feedback.Message);
    }

    [Fact]
    public void CashierWithoutManage_CannotAdd()
    {
        using var env = new Phase1Environment();
        var (vm, _, _, _, _) = Create(env, UserRole.Cashier);

        Assert.False(vm.CanManage);
    }

    [Fact]
    public void NavigationCommands_RouteToScreens()
    {
        using var env = new Phase1Environment();
        var (vm, _, nav, _, _) = Create(env);

        vm.OpenCategoriesCommand.Execute(null);
        vm.OpenInventoryCommand.Execute(null);

        Assert.True(nav.CategoriesShown);
        Assert.True(nav.InventoryShown);
    }
}
