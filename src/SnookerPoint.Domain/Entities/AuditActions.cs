namespace SnookerPoint.Domain.Entities;

/// <summary>Stable action names used in <see cref="AuditEvent.Action"/>.</summary>
public static class AuditActions
{
    public const string SetupCompleted = "SetupCompleted";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string AccountLockedOut = "AccountLockedOut";
    public const string Logout = "Logout";
    public const string ShiftOpened = "ShiftOpened";
    public const string ShiftClosed = "ShiftClosed";
    public const string CashMovementRecorded = "CashMovementRecorded";

    // Phase 2 — table sessions
    public const string SessionStarted = "SessionStarted";
    public const string SessionPaused = "SessionPaused";
    public const string SessionResumed = "SessionResumed";
    public const string SessionTransferred = "SessionTransferred";
    public const string SessionFinished = "SessionFinished";
    public const string SessionCorrected = "SessionCorrected";
    public const string SessionVoided = "SessionVoided";
    public const string BillingSettingsUpdated = "BillingSettingsUpdated";

    // Table management (Owner/Administrator/Manager)
    public const string TableAdded = "TableAdded";
    public const string TableUpdated = "TableUpdated";
    public const string TableActivated = "TableActivated";
    public const string TableDeactivated = "TableDeactivated";

    // Staff management (Owner/Administrator)
    public const string StaffCreated = "StaffCreated";
    public const string StaffUpdated = "StaffUpdated";
    public const string StaffRoleChanged = "StaffRoleChanged";
    public const string StaffEnabled = "StaffEnabled";
    public const string StaffDisabled = "StaffDisabled";
    public const string StaffPasswordReset = "StaffPasswordReset";
    public const string StaffPinChanged = "StaffPinChanged";
    public const string StaffPinRemoved = "StaffPinRemoved";
    public const string StaffLockoutCleared = "StaffLockoutCleared";
    public const string StaffTemporaryPasswordIssued = "StaffTemporaryPasswordIssued";

    // Self-service account security (My Account)
    public const string AccountPasswordChanged = "AccountPasswordChanged";
    public const string AccountPinChanged = "AccountPinChanged";
    public const string AccountPinRemoved = "AccountPinRemoved";

    // Owner offline recovery
    public const string OwnerRecoveryCodeGenerated = "OwnerRecoveryCodeGenerated";
    public const string OwnerRecoveryUsed = "OwnerRecoveryUsed";

    // Phase 3 — categories
    public const string CategoryCreated = "CategoryCreated";
    public const string CategoryUpdated = "CategoryUpdated";
    public const string CategoryActivated = "CategoryActivated";
    public const string CategoryDeactivated = "CategoryDeactivated";

    // Phase 3 — products
    public const string ProductCreated = "ProductCreated";
    public const string ProductUpdated = "ProductUpdated";
    public const string ProductPriceChanged = "ProductPriceChanged";
    public const string ProductCostChanged = "ProductCostChanged";
    public const string ProductBarcodeChanged = "ProductBarcodeChanged";
    public const string ProductActivated = "ProductActivated";
    public const string ProductDeactivated = "ProductDeactivated";
    public const string ProductsImported = "ProductsImported";
    public const string ProductsExported = "ProductsExported";

    // Phase 3 — inventory
    public const string StockMovementRecorded = "StockMovementRecorded";
    public const string StockMovementReversed = "StockMovementReversed";

    // Phase 4 — sales, payments & receipts
    public const string SaleCreated = "SaleCreated";
    public const string SaleHeld = "SaleHeld";
    public const string SaleReopened = "SaleReopened";
    public const string SaleCancelled = "SaleCancelled";
    public const string SaleTableAttached = "SaleTableAttached";
    public const string SaleDiscountApplied = "SaleDiscountApplied";
    public const string SalePriceOverridden = "SalePriceOverridden";
    public const string SaleCompleted = "SaleCompleted";
    public const string ReceiptPrinted = "ReceiptPrinted";
    public const string ReceiptReprinted = "ReceiptReprinted";
    public const string PaymentMethodConfigured = "PaymentMethodConfigured";

    // Phase 5 — bookings & reservations
    public const string BookingCreated = "BookingCreated";
    public const string BookingUpdated = "BookingUpdated";
    public const string BookingCancelled = "BookingCancelled";
    public const string BookingCheckedIn = "BookingCheckedIn";
    public const string BookingStarted = "BookingStarted";
    public const string BookingCompleted = "BookingCompleted";
    public const string BookingNoShow = "BookingNoShow";

    // Phase 6 — reports, backups & administration
    public const string ReportExported = "ReportExported";
    public const string BackupCreated = "BackupCreated";
    public const string BackupFailed = "BackupFailed";
    public const string BackupRestored = "BackupRestored";
    public const string BackupSettingsUpdated = "BackupSettingsUpdated";
    public const string DatabaseHealthChecked = "DatabaseHealthChecked";
    public const string DiagnosticSummaryCreated = "DiagnosticSummaryCreated";
    public const string SettingsUpdated = "SettingsUpdated";
    public const string TaxServiceUpdated = "TaxServiceUpdated";

    // Phase 7 — trial & offline licensing (safe diagnostic summaries only; never secrets)
    public const string TrialStarted = "TrialStarted";
    public const string TrialExpiringSoon = "TrialExpiringSoon";
    public const string TrialExpired = "TrialExpired";
    public const string LicenseActivationAttempted = "LicenseActivationAttempted";
    public const string LicenseActivated = "LicenseActivated";
    public const string LicenseActivationFailed = "LicenseActivationFailed";
    public const string LicenseInvalidSignature = "LicenseInvalidSignature";
    public const string LicenseMachineMismatch = "LicenseMachineMismatch";
    public const string LicenseReplaced = "LicenseReplaced";
    public const string ClockRollbackDetected = "ClockRollbackDetected";
    public const string LicenseStateCorruption = "LicenseStateCorruption";
}
