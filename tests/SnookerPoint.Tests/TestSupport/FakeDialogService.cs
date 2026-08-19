using SnookerPoint.App.Services;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Settings;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>
/// A dialog service that shows nothing but can be scripted to return values and records
/// what it was asked to display, so view-model feedback flows can be tested headlessly.
/// </summary>
public sealed class FakeDialogService : IDialogService
{
    // Scripted return values for the flows exercised in tests.
    public bool ConfirmResult { get; set; }
    public SetCredentialInput? SetCredentialResult { get; set; }
    public StaffEditInput? StaffEditorResult { get; set; }

    // Records of what was shown.
    public string? LastError { get; private set; }
    public string? LastInfo { get; private set; }
    public string? ShownRecoveryCode { get; private set; }
    public string? ShownTemporaryPassword { get; private set; }
    public string? ShownTemporaryPasswordStaff { get; private set; }

    public OpenShiftInput? ShowOpenShift() => null;
    public CloseShiftInput? ShowCloseShift(Money expectedCash) => null;
    public CashMovementInput? ShowCashMovement(CashMovementType type) => null;
    public bool Confirm(string title, string message) => ConfirmResult;
    public void ShowError(string title, string message) => LastError = message;
    public void ShowInfo(string title, string message) => LastInfo = message;
    public string? PickFolder(string? initialPath) => null;
    public StartSessionInput? ShowStartSession(string tableName, string tableType, Money hourlyRate, string policySummary) => null;
    public TransferInput? ShowTransfer(string sourceTableName, IReadOnlyList<TransferDestination> destinations) => null;
    public FinishInput? ShowFinish(SessionSummary summary) => null;
    public CorrectionRequest? ShowCorrection(SessionCorrectionContext context) => null;
    public BillingSettingsInput? ShowBillingSettings(BillingSettingsView current) => null;
    public StaffEditInput? ShowStaffEditor(StaffEditContext context) => StaffEditorResult;
    public SetCredentialInput? ShowSetCredential(SetCredentialContext context) => SetCredentialResult;
    public void ShowRecoveryCode(string code) => ShownRecoveryCode = code;

    public void ShowTemporaryPassword(string staffName, string temporaryPassword)
    {
        ShownTemporaryPasswordStaff = staffName;
        ShownTemporaryPassword = temporaryPassword;
    }

    public ForgotRecoveryInput? ShowForgotPassword(ForgotPasswordContext context) => null;

    // --- Phase 3 catalogue & inventory dialogs ---

    public ProductEditorResult? ProductEditorResult { get; set; }
    public StockMovementResult? StockMovementResult { get; set; }
    public CsvDuplicateStrategy? CsvStrategyResult { get; set; }
    public string? OpenFileResult { get; set; }
    public string? SaveFileResult { get; set; }
    public bool StockHistoryShown { get; private set; }

    public ProductEditorResult? ShowProductEditor(ProductEditorContext context) => ProductEditorResult;
    public StockMovementResult? ShowStockMovement(StockMovementContext context) => StockMovementResult;
    public CsvDuplicateStrategy? ShowCsvImportPreview(CsvImportPreview preview) => CsvStrategyResult;
    public void ShowStockHistory(string productName, IReadOnlyList<StockMovementLine> history) => StockHistoryShown = true;
    public string? PickOpenFile(string title, string filter, string? initialDir = null) => OpenFileResult;
    public string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDir = null) => SaveFileResult;

    // --- Phase 4 sales & payment dialogs ---

    public PaymentDialogResult? PaymentResult { get; set; }
    public DiscountResult? DiscountResult { get; set; }
    public PriceOverrideResult? PriceOverrideResult { get; set; }
    public bool ReceiptPreviewPrints { get; set; }
    public bool ReceiptPreviewShown { get; private set; }

    public PaymentDialogResult? ShowPayment(PaymentDialogContext context) => PaymentResult;
    public DiscountResult? ShowDiscount() => DiscountResult;
    public PriceOverrideResult? ShowPriceOverride(string productName, Money currentPrice) => PriceOverrideResult;

    public bool ShowReceiptPreview(string title, string receiptText)
    {
        ReceiptPreviewShown = true;
        return ReceiptPreviewPrints;
    }

    // --- Phase 5 booking dialogs ---

    public BookingEditorResult? BookingEditorResult { get; set; }
    public BookingStartResult? BookingStartResult { get; set; }
    public BookingEditorContext? LastBookingEditorContext { get; private set; }
    public BookingStartContext? LastBookingStartContext { get; private set; }

    public BookingEditorResult? ShowBookingEditor(BookingEditorContext context)
    {
        LastBookingEditorContext = context;
        return BookingEditorResult;
    }

    public BookingStartResult? ShowBookingStart(BookingStartContext context)
    {
        LastBookingStartContext = context;
        return BookingStartResult;
    }

    public string? OpenedPath { get; private set; }

    public void OpenPath(string path) => OpenedPath = path;
}
