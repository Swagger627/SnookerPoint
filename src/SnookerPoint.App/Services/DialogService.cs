using System.IO;
using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;
using SnookerPoint.App.Views.Dialogs;
using SnookerPoint.Application.Billing;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

/// <summary>Shows the shift input dialogs and simple confirm/error prompts.</summary>
public sealed class DialogService : IDialogService
{
    private readonly ISessionBillingCalculator _calculator;

    public DialogService(ISessionBillingCalculator calculator)
    {
        _calculator = calculator;
    }

    public OpenShiftInput? ShowOpenShift()
    {
        var vm = new OpenShiftDialogViewModel();
        var dialog = new OpenShiftDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public CloseShiftInput? ShowCloseShift(Money expectedCash)
    {
        var vm = new CloseShiftDialogViewModel(expectedCash);
        var dialog = new CloseShiftDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public CashMovementInput? ShowCashMovement(CashMovementType type)
    {
        var vm = new CashMovementDialogViewModel(type);
        var dialog = new CashMovementDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public bool Confirm(string title, string message) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public void ShowError(string title, string message) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string title, string message) =>
        MessageBox.Show(ActiveOwner(), message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public string? PickFolder(string? initialPath)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a backup folder",
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FolderName : null;
    }

    // --- Phase 2 table-session dialogs ---

    public StartSessionInput? ShowStartSession(string tableName, string tableType, Money hourlyRate, string policySummary)
    {
        var vm = new StartSessionDialogViewModel(
            tableName, tableType, $"{hourlyRate.Format()}/hr", policySummary, DisplayFormat.LocalTime(DateTimeOffset.UtcNow));
        var dialog = new StartSessionDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public TransferInput? ShowTransfer(string sourceTableName, IReadOnlyList<TransferDestination> destinations)
    {
        var vm = new TransferDialogViewModel(sourceTableName, destinations);
        var dialog = new TransferDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public FinishInput? ShowFinish(SnookerPoint.Application.Tables.SessionSummary summary)
    {
        var vm = new FinishDialogViewModel(summary);
        var dialog = new FinishDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public CorrectionRequest? ShowCorrection(SnookerPoint.Application.Tables.SessionCorrectionContext context)
    {
        var vm = new CorrectionDialogViewModel(context, _calculator);
        var dialog = new CorrectionDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public BillingSettingsInput? ShowBillingSettings(SnookerPoint.Application.Settings.BillingSettingsView current)
    {
        var vm = new BillingSettingsDialogViewModel(current);
        var dialog = new BillingSettingsDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    // --- Staff management dialogs ---

    public StaffEditInput? ShowStaffEditor(StaffEditContext context)
    {
        var vm = new StaffEditDialogViewModel(context);
        var dialog = new StaffEditDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public SetCredentialInput? ShowSetCredential(SetCredentialContext context)
    {
        var vm = new SetCredentialDialogViewModel(context);
        var dialog = new SetCredentialDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public void ShowRecoveryCode(string code)
    {
        var dialog = new RecoveryCodeDialog(code) { Owner = ActiveOwner() };
        dialog.ShowDialog();
    }

    public void ShowTemporaryPassword(string staffName, string temporaryPassword)
    {
        var dialog = new TemporaryPasswordDialog(staffName, temporaryPassword) { Owner = ActiveOwner() };
        dialog.ShowDialog();
    }

    public ForgotRecoveryInput? ShowForgotPassword(ForgotPasswordContext context)
    {
        var vm = new ForgotPasswordDialogViewModel(context);
        var dialog = new ForgotPasswordDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    // --- Phase 3 catalogue & inventory dialogs ---

    public ProductEditorResult? ShowProductEditor(ProductEditorContext context)
    {
        var vm = new ProductEditorDialogViewModel(context);
        var dialog = new ProductEditorDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public StockMovementResult? ShowStockMovement(StockMovementContext context)
    {
        var vm = new StockMovementDialogViewModel(context);
        var dialog = new StockMovementDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public SnookerPoint.Application.Catalog.CsvDuplicateStrategy? ShowCsvImportPreview(SnookerPoint.Application.Catalog.CsvImportPreview preview)
    {
        var vm = new CsvImportDialogViewModel(preview);
        var dialog = new CsvImportDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public void ShowStockHistory(string productName, IReadOnlyList<SnookerPoint.Application.Catalog.StockMovementLine> history)
    {
        var vm = new StockHistoryDialogViewModel(productName, history);
        var dialog = new StockHistoryDialog(vm) { Owner = ActiveOwner() };
        dialog.ShowDialog();
    }

    public string? PickOpenFile(string title, string filter, string? initialDir = null)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = filter };
        if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
        {
            dialog.InitialDirectory = initialDir;
        }

        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDir = null)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Title = title, FileName = defaultFileName, Filter = filter };
        if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
        {
            dialog.InitialDirectory = initialDir;
        }

        return dialog.ShowDialog(ActiveOwner()) == true ? dialog.FileName : null;
    }

    // --- Phase 4 sales & payment dialogs ---

    public PaymentDialogResult? ShowPayment(PaymentDialogContext context)
    {
        var vm = new PaymentDialogViewModel(context);
        var dialog = new PaymentDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public DiscountResult? ShowDiscount()
    {
        var vm = new DiscountDialogViewModel();
        var dialog = new DiscountDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public PriceOverrideResult? ShowPriceOverride(string productName, Money currentPrice)
    {
        var vm = new PriceOverrideDialogViewModel(productName, currentPrice);
        var dialog = new PriceOverrideDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public bool ShowReceiptPreview(string title, string receiptText)
    {
        var dialog = new ReceiptPreviewDialog(title, receiptText) { Owner = ActiveOwner() };
        dialog.ShowDialog();
        return dialog.Printed;
    }

    public BookingEditorResult? ShowBookingEditor(BookingEditorContext context)
    {
        var vm = new BookingEditorDialogViewModel(context);
        var dialog = new BookingEditorDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public BookingStartResult? ShowBookingStart(BookingStartContext context)
    {
        var vm = new BookingStartDialogViewModel(context);
        var dialog = new BookingStartDialog(vm) { Owner = ActiveOwner() };
        return dialog.ShowDialog() == true ? vm.Result : null;
    }

    public void OpenPath(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var target = Directory.Exists(path) || File.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                ShowInfo("Folder", "That location does not exist yet.");
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            ShowInfo("Folder", $"Could not open the location:\n{path}");
        }
    }

    private static Window ActiveOwner() =>
        System.Windows.Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
        ?? System.Windows.Application.Current.MainWindow;
}
