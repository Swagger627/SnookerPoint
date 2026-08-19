namespace SnookerPoint.Domain.Enums;

/// <summary>
/// Fine-grained capabilities checked by the permission service. Screens ask "can
/// this user do X?" rather than testing role names directly, so the role/permission
/// mapping can evolve without touching every screen.
/// </summary>
/// <remarks>
/// Several values are defined now but only exercised in later phases; Phase 1 uses
/// the shift, cash, advanced-mode and audit permissions. Keeping the fuller set
/// declared keeps the mapping stable as features land.
/// </remarks>
public enum Permission
{
    // --- Shifts & cash (Phase 1) ---
    OpenShift = 0,
    CloseShift = 1,
    RecordCashMovement = 2,
    ApproveCashMovement = 3,

    // --- Navigation / management surface (Phase 1) ---
    AccessAdvancedMode = 10,
    ViewAuditLog = 11,
    ViewSettings = 12,

    // --- Table sessions (Phase 2) ---
    ViewTables = 30,
    StartSession = 31,
    PauseResumeSession = 32,
    TransferSession = 33,
    FinishSession = 34,
    CorrectSession = 35,
    ManageBillingSettings = 36,

    // --- Table configuration (Phase 2 uses this; also management) ---
    ManageTables = 21,

    // --- Reserved for later phases (declared, not yet enforced) ---
    ManageStaff = 20,
    ManageProducts = 22,
    ManageInventory = 23,
    TakePayment = 24,
    ApproveVoidRefundDiscount = 25,
    ViewFinancialReports = 26,
    ManageBackups = 27,
    ManageLicensing = 28,

    // --- Products & inventory (Phase 3) ---
    ViewProducts = 40,
    ViewInventory = 41,
    AddStock = 42,
    AdjustInventory = 43,
    RecordWasteDamage = 44,
    ImportProducts = 45,
    ExportProducts = 46,

    // --- Sales, payments & receipts (Phase 4) ---
    CreateSale = 50,
    ViewHeldSales = 51,
    CancelDraftSale = 52,
    CompletePayment = 53,
    ApplyDiscount = 54,
    OverridePrice = 55,
    ViewSalesHistory = 56,
    ReprintReceipt = 57,
    ManagePaymentMethods = 58,

    // --- Bookings & reservations (Phase 5) ---
    ViewBookings = 60,
    ManageBookings = 61,

    // --- Reports, backups & administration (Phase 6) ---
    ViewReports = 70,
    ExportReports = 71,
    ViewProfitReports = 72,
    ViewShiftReports = 73,
    CreateBackup = 74,
    RestoreBackup = 75,
    ManageBackupSettings = 76,
    RunDatabaseHealthCheck = 77,
    ManageSettings = 78,
    ConfigureTaxService = 79,
}
