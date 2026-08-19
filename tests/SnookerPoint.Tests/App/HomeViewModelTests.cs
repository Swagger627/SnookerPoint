using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Covers the reorganised Home navigation: daily modules (Products, Inventory, Tables,
/// Session History) are reachable directly — not via Advanced Mode — Products/Inventory
/// are live (not "Coming Soon"), routes aren't duplicated, and management stays
/// permission-gated.
/// </summary>
public class HomeViewModelTests
{
    private static (HomeViewModel Vm, FakeNavigationService Nav) Create(Phase1Environment env, UserRole role = UserRole.Owner)
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
        session.SignIn(new AuthenticatedUser(actorId, role.ToString(), role.ToString().ToLower(), role, HasPin: false));
        var nav = new FakeNavigationService();
        var vm = new HomeViewModel(session, env.Shifts, env.Auth, new PermissionService(), env.ClubSettings,
            env.Audit, env.OwnerRecovery, env.Bookings, new FakeLicensingService(), new FakeLicenseGate(), new FakeThemeService(), new FakeDialogService(), nav);
        return (vm, nav);
    }

    [Fact]
    public void Home_LoadsWithoutError_AndKnowsTheUser()
    {
        using var env = new Phase1Environment();
        var (vm, _) = Create(env);

        Assert.False(string.IsNullOrWhiteSpace(vm.UserDisplayName));
        Assert.False(string.IsNullOrWhiteSpace(vm.ClubName));
    }

    [Fact]
    public void Products_OpensDirectly_WithoutAdvancedMode()
    {
        using var env = new Phase1Environment();
        var (vm, nav) = Create(env);

        Assert.False(vm.IsAdvancedMode);          // not in advanced mode
        Assert.True(vm.CanViewProducts);
        vm.OpenProductsCommand.Execute(null);

        Assert.True(nav.ProductsShown);
    }

    [Fact]
    public void Inventory_OpensDirectly_WithoutAdvancedMode()
    {
        using var env = new Phase1Environment();
        var (vm, nav) = Create(env);

        Assert.False(vm.IsAdvancedMode);
        vm.OpenInventoryCommand.Execute(null);

        Assert.True(nav.InventoryShown);
    }

    [Fact]
    public void ComingSoon_OnlyIncludesUnimplementedModules()
    {
        using var env = new Phase1Environment();
        var (vm, _) = Create(env);

        // Products, Inventory, New Sale and Bookings are now live — not "Coming Soon".
        Assert.DoesNotContain(vm.ComingSoonModules, m => m.Name == "Products");
        Assert.DoesNotContain(vm.ComingSoonModules, m => m.Name == "Inventory");
        Assert.DoesNotContain(vm.ComingSoonModules, m => m.Name == "New Sale");
        Assert.DoesNotContain(vm.ComingSoonModules, m => m.Name == "Bookings");
    }

    [Fact]
    public void Bookings_IsLive_AndOpensDirectly()
    {
        using var env = new Phase1Environment();
        var (vm, nav) = Create(env);

        Assert.True(vm.CanViewBookings);
        vm.OpenBookingsCommand.Execute(null);
        Assert.True(nav.BookingsShown);
    }

    [Fact]
    public void NewSaleAndSalesHistory_OpenDirectly()
    {
        using var env = new Phase1Environment();
        var (vm, nav) = Create(env);

        Assert.True(vm.CanCreateSale);
        Assert.True(vm.CanViewSalesHistory);
        vm.OpenNewSaleCommand.Execute(null);
        vm.OpenSalesHistoryCommand.Execute(null);

        Assert.True(nav.NewSaleShown);
        Assert.True(nav.SalesHistoryShown);
    }

    [Fact]
    public void FloorStaff_SeesProducts_ButNotInventoryOrManagement()
    {
        using var env = new Phase1Environment();
        var (vm, _) = Create(env, UserRole.FloorStaff);

        Assert.True(vm.CanViewProducts);        // floor staff can view products
        Assert.False(vm.CanViewInventory);      // but not inventory
        Assert.False(vm.CanManageStaff);        // nor staff management
        Assert.False(vm.CanAccessManagement);   // management section hidden
    }

    [Fact]
    public void Cashier_CanViewProductsAndInventory_ButNotManage()
    {
        using var env = new Phase1Environment();
        var (vm, _) = Create(env, UserRole.Cashier);

        Assert.True(vm.CanViewProducts);
        Assert.True(vm.CanViewInventory);
        Assert.False(vm.CanManageProducts);
        Assert.False(vm.CanManageStaff);
    }

    [Fact]
    public void StaffManagement_IsPermissionRestricted()
    {
        using var env = new Phase1Environment();

        var (owner, _) = Create(env, UserRole.Owner);
        Assert.True(owner.CanManageStaff);
        Assert.True(owner.CanAccessManagement);

        using var env2 = new Phase1Environment();
        var (cashier, cashierNav) = Create(env2, UserRole.Cashier);
        Assert.False(cashier.CanManageStaff);
        cashier.OpenStaffCommand.Execute(null);
        Assert.False(cashierNav.StaffShown);   // gated command does nothing
    }

    [Fact]
    public void DailyModules_DoNotDependOnAdvancedMode()
    {
        using var env = new Phase1Environment();
        var (vm, nav) = Create(env);

        // Without ever toggling advanced mode, all daily modules navigate.
        vm.OpenTablesCommand.Execute(null);
        vm.OpenSessionHistoryCommand.Execute(null);
        vm.OpenProductsCommand.Execute(null);
        vm.OpenInventoryCommand.Execute(null);

        Assert.True(nav.TablesShown);
        Assert.True(nav.HistoryShown);
        Assert.True(nav.ProductsShown);
        Assert.True(nav.InventoryShown);
    }
}
