using System.Threading;
using System.Windows;
using SnookerPoint.App.Controls;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.App.ViewModels;
using SnookerPoint.App.ViewModels.Dialogs;
using SnookerPoint.App.Views;
using SnookerPoint.App.Views.Dialogs;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Loads every Phase 3 catalogue/inventory view and dialog at runtime with the real
/// application resources, in both dark and light themes, catching missing StaticResource
/// keys / XAML load errors a build does not detect.
/// </summary>
[Collection("WpfSmoke")]
public class Phase3ViewSmokeTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void AllPhase3ViewsAndDialogs_LoadWithoutResourceErrors(string themeName)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try
            {
                Run(themeName);
            }
            catch (Exception ex)
            {
                captured = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(captured);
    }

    private static void Run(string themeName)
    {
        var app = System.Windows.Application.Current ?? new System.Windows.Application();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SnookerPoint.App;component/Themes/Styles.xaml"),
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/SnookerPoint.App;component/Themes/{themeName}.xaml"),
        });
        app.Resources["BoolToVisible"] = new BoolToVisibilityConverter();
        app.Resources["InverseBoolToVisible"] = new InverseBoolToVisibilityConverter();
        app.Resources["NullToCollapsed"] = new NullOrEmptyToCollapsedConverter();

        using var env = new Phase1Environment();
        var (ownerId, shiftId, _) = env.SeedOwnerShiftAndTables(12_000);
        var cat = env.SeedCategory(ownerId, "Drinks");
        env.Products.Create(new CreateProductRequest(
            "Cola 330", "C330", "0012345", cat, "CoolCo", "Regular", "330 ml", ProductUnit.Bottle,
            Money.FromRupees(35), Money.FromRupees(60), true, 6m, 24m), ownerId, shiftId);

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));
        var permissions = new PermissionService();
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var theme = new ThemeService(app);

        // --- Screens ---
        var productsVm = new ProductsViewModel(env.Products, env.Categories, env.ProductCsv, env.Images, env.Paths,
            session, permissions, dialogs, nav, theme);
        RenderView(new ProductsView { DataContext = productsVm });

        var categoriesVm = new ManageCategoriesViewModel(env.Categories, session, permissions, theme, nav);
        RenderView(new ManageCategoriesView { DataContext = categoriesVm });

        var inventoryVm = new InventoryViewModel(env.Inventory, env.Categories, env.Shifts, session,
            permissions, dialogs, nav, theme, new FakeLicenseGate());
        RenderView(new InventoryView { DataContext = inventoryVm });

        // --- Dialogs ---
        var categoryOptions = new[] { new CategoryOption(cat, "Drinks") };
        _ = new ProductEditorDialog(new ProductEditorDialogViewModel(
            new ProductEditorContext("Add product", true, null, categoryOptions, "0012345", null)));

        _ = new StockMovementDialog(new StockMovementDialogViewModel(
            new StockMovementContext("Cola 330", 24m, StockMovementType.StockIn)));

        var preview = env.ProductCsv.Preview("SKU,ProductName,SellingPrice\r\nX1,Sample,60\r\n");
        _ = new CsvImportDialog(new CsvImportDialogViewModel(preview));

        var product = env.Products.GetList(new ProductFilter()).First();
        _ = new StockHistoryDialog(new StockHistoryDialogViewModel("Cola 330", env.Inventory.GetHistory(product.Id)));
    }

    private static void RenderView(FrameworkElement view)
    {
        view.Measure(new Size(1200, 900));
        view.Arrange(new Rect(0, 0, 1200, 900));
    }
}
