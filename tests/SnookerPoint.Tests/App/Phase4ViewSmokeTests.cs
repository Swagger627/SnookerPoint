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
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

/// <summary>
/// Loads the Phase 4 New Sale / Sales History views and the payment/receipt dialogs at
/// runtime with the real resources, in both dark and light themes.
/// </summary>
[Collection("WpfSmoke")]
public class Phase4ViewSmokeTests
{
    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void Phase4ViewsAndDialogs_LoadWithoutResourceErrors(string themeName)
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
        var productId = env.SeedProduct(ownerId, shiftId, "P1", 60, opening: 10m, barcode: "0012345");

        var session = new SessionContext();
        session.SignIn(new AuthenticatedUser(ownerId, "The Owner", "owner", UserRole.Owner, false));
        var permissions = new PermissionService();
        var dialogs = new FakeDialogService();
        var nav = new FakeNavigationService();
        var theme = new ThemeService(app);

        // A completed sale so Sales History has a row.
        var saleId = env.Sales.CreateWalkinDraft(ownerId).Value;
        env.Sales.AddProduct(saleId, productId, 1, ownerId);
        env.Sales.Complete(new CompleteSaleRequest(saleId,
            new[] { new PaymentInput(env.CashMethodId, Money.FromRupees(60), Money.FromRupees(60), null, null) }, ownerId, shiftId));

        var newSaleVm = new NewSaleViewModel(env.Sales, env.Products, env.PaymentMethods, env.SalesQuery, env.Shifts,
            session, permissions, dialogs, nav, theme);
        RenderView(new NewSaleView { DataContext = newSaleVm });

        var historyVm = new SalesHistoryViewModel(env.SalesQuery, session, permissions, dialogs, nav, theme);
        RenderView(new SalesHistoryView { DataContext = historyVm });

        // Dialogs
        _ = new PaymentDialog(new PaymentDialogViewModel(
            new PaymentDialogContext(Money.FromRupees(850), env.PaymentMethods.GetActive())));
        _ = new DiscountDialog(new DiscountDialogViewModel());
        _ = new PriceOverrideDialog(new PriceOverrideDialogViewModel("Cola 330", Money.FromRupees(60)));
        _ = new ReceiptPreviewDialog("Receipt #1", "Snooker Point\r\nTOTAL   Rs 60\r\nThank you!");
    }

    private static void RenderView(FrameworkElement view)
    {
        view.Measure(new Size(1280, 900));
        view.Arrange(new Rect(0, 0, 1280, 900));
    }
}
