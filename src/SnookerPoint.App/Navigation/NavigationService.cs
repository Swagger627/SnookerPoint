using Microsoft.Extensions.DependencyInjection;
using SnookerPoint.App.ViewModels;

namespace SnookerPoint.App.Navigation;

/// <summary>
/// Resolves screen view models from the container and places them into the shell.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly ShellViewModel _shell;

    public NavigationService(IServiceProvider services, ShellViewModel shell)
    {
        _services = services;
        _shell = shell;
    }

    private void SetCurrent(object viewModel)
    {
        // Dispose the outgoing screen (e.g. to stop the dashboard's live timer).
        if (_shell.Current is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _shell.Current = viewModel;
    }

    public void ShowSetupWizard() => SetCurrent(_services.GetRequiredService<SetupWizardViewModel>());

    public void ShowLogin() => SetCurrent(_services.GetRequiredService<LoginViewModel>());

    public void ShowHome() => SetCurrent(_services.GetRequiredService<HomeViewModel>());

    public void ShowTables() => SetCurrent(_services.GetRequiredService<TablesViewModel>());

    public void ShowSessionHistory() => SetCurrent(_services.GetRequiredService<SessionHistoryViewModel>());

    public void ShowManageTables() => SetCurrent(_services.GetRequiredService<ManageTablesViewModel>());

    public void ShowStaff() => SetCurrent(_services.GetRequiredService<StaffViewModel>());

    public void ShowAccount() => SetCurrent(_services.GetRequiredService<AccountViewModel>());

    public void ShowProducts() => SetCurrent(_services.GetRequiredService<ProductsViewModel>());

    public void ShowCategories() => SetCurrent(_services.GetRequiredService<ManageCategoriesViewModel>());

    public void ShowInventory() => SetCurrent(_services.GetRequiredService<InventoryViewModel>());

    public void ShowNewSale() => SetCurrent(_services.GetRequiredService<NewSaleViewModel>());

    public void ShowSalesHistory() => SetCurrent(_services.GetRequiredService<SalesHistoryViewModel>());

    public void ShowBookings() => SetCurrent(_services.GetRequiredService<BookingsViewModel>());

    public void ShowReports() => SetCurrent(_services.GetRequiredService<ReportsViewModel>());

    public void ShowBackup() => SetCurrent(_services.GetRequiredService<BackupViewModel>());

    public void ShowSettings() => SetCurrent(_services.GetRequiredService<SettingsViewModel>());

    public void ShowAdmin() => SetCurrent(_services.GetRequiredService<AdminViewModel>());

    public void ShowAudit() => SetCurrent(_services.GetRequiredService<AuditViewModel>());

    public void ShowActivation() => SetCurrent(_services.GetRequiredService<ActivationViewModel>());
}
