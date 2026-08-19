using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

public sealed record OpenShiftInput(Money OpeningCash, string? Note);

public sealed record CloseShiftInput(Money CountedCash, string? Note);

public sealed record CashMovementInput(Money Amount, string Reason);

/// <summary>
/// Shows the small modal input dialogs used by the shift workflow, plus simple
/// confirmation/error prompts. Returns null when the user cancels.
/// </summary>
public interface IDialogService
{
    OpenShiftInput? ShowOpenShift();

    CloseShiftInput? ShowCloseShift(Money expectedCash);

    CashMovementInput? ShowCashMovement(CashMovementType type);

    bool Confirm(string title, string message);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);

    /// <summary>Opens a folder picker, returning the chosen path or null if cancelled.</summary>
    string? PickFolder(string? initialPath);

    // --- Phase 2 table-session dialogs ---

    StartSessionInput? ShowStartSession(string tableName, string tableType, Money hourlyRate, string policySummary);

    TransferInput? ShowTransfer(string sourceTableName, IReadOnlyList<TransferDestination> destinations);

    FinishInput? ShowFinish(SnookerPoint.Application.Tables.SessionSummary summary);

    CorrectionRequest? ShowCorrection(SnookerPoint.Application.Tables.SessionCorrectionContext context);

    BillingSettingsInput? ShowBillingSettings(SnookerPoint.Application.Settings.BillingSettingsView current);

    // --- Staff management dialogs ---

    StaffEditInput? ShowStaffEditor(StaffEditContext context);

    SetCredentialInput? ShowSetCredential(SetCredentialContext context);

    // --- Account security & recovery dialogs ---

    /// <summary>Displays a one-time recovery code with copy/print options.</summary>
    void ShowRecoveryCode(string code);

    /// <summary>Displays a one-time temporary password for a staff member with a copy option.</summary>
    void ShowTemporaryPassword(string staffName, string temporaryPassword);

    /// <summary>Shows forgot-password guidance and (when available) the Owner recovery form.</summary>
    ForgotRecoveryInput? ShowForgotPassword(ForgotPasswordContext context);

    // --- Phase 3 catalogue & inventory dialogs ---

    ProductEditorResult? ShowProductEditor(ProductEditorContext context);

    StockMovementResult? ShowStockMovement(StockMovementContext context);

    /// <summary>Shows a CSV import preview; returns the chosen duplicate strategy or null if cancelled.</summary>
    SnookerPoint.Application.Catalog.CsvDuplicateStrategy? ShowCsvImportPreview(SnookerPoint.Application.Catalog.CsvImportPreview preview);

    void ShowStockHistory(string productName, IReadOnlyList<SnookerPoint.Application.Catalog.StockMovementLine> history);

    /// <summary>Opens a file picker; returns the chosen path or null if cancelled.</summary>
    string? PickOpenFile(string title, string filter, string? initialDir = null);

    /// <summary>Opens a save-file picker; returns the chosen path or null if cancelled.</summary>
    string? PickSaveFile(string title, string defaultFileName, string filter, string? initialDir = null);

    // --- Phase 4 sales & payment dialogs ---

    PaymentDialogResult? ShowPayment(PaymentDialogContext context);

    DiscountResult? ShowDiscount();

    PriceOverrideResult? ShowPriceOverride(string productName, Money currentPrice);

    /// <summary>Shows a receipt preview with a print option; returns true if the user printed it.</summary>
    bool ShowReceiptPreview(string title, string receiptText);

    // --- Phase 5 booking dialogs ---

    /// <summary>Shows the create/edit booking dialog; returns the entered values or null if cancelled.</summary>
    BookingEditorResult? ShowBookingEditor(BookingEditorContext context);

    /// <summary>Shows the start-booking dialog (table + billing choice); returns the choice or null.</summary>
    BookingStartResult? ShowBookingStart(BookingStartContext context);

    // --- Phase 6 administration ---

    /// <summary>Opens a folder or file in the system file explorer. Failures are swallowed with a message.</summary>
    void OpenPath(string path);
}
