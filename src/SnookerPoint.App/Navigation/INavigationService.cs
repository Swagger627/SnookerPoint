namespace SnookerPoint.App.Navigation;

/// <summary>
/// Switches the top-level screen shown by the shell. Screens are resolved fresh
/// from the container so each navigation starts with clean view-model state.
/// </summary>
public interface INavigationService
{
    void ShowSetupWizard();

    void ShowLogin();

    void ShowHome();

    void ShowTables();

    void ShowSessionHistory();

    void ShowManageTables();

    void ShowStaff();

    void ShowAccount();

    void ShowProducts();

    void ShowCategories();

    void ShowInventory();

    void ShowNewSale();

    void ShowSalesHistory();

    void ShowBookings();

    void ShowReports();

    void ShowBackup();

    void ShowSettings();

    void ShowAdmin();

    void ShowAudit();

    void ShowActivation();
}
