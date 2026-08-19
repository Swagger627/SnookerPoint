using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Security;

/// <summary>
/// The default mapping from a <see cref="UserRole"/> to the set of
/// <see cref="Permission"/>s it grants. This is pure domain policy with no
/// dependencies; the application layer consults it via a permission service.
/// </summary>
public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<UserRole, HashSet<Permission>> Map =
        new Dictionary<UserRole, HashSet<Permission>>
        {
            [UserRole.Owner] = AllPermissions(),
            [UserRole.Administrator] = AllPermissions(),
            [UserRole.Manager] = new()
            {
                Permission.OpenShift,
                Permission.CloseShift,
                Permission.RecordCashMovement,
                Permission.ApproveCashMovement,
                Permission.AccessAdvancedMode,
                Permission.ViewAuditLog,
                Permission.ViewSettings,
                Permission.ManageTables,
                Permission.ManageProducts,
                Permission.ManageInventory,
                Permission.TakePayment,
                Permission.ApproveVoidRefundDiscount,
                Permission.ViewFinancialReports,
                Permission.ManageBackups,
                // Table sessions (Phase 2)
                Permission.ViewTables,
                Permission.StartSession,
                Permission.PauseResumeSession,
                Permission.TransferSession,
                Permission.FinishSession,
                Permission.CorrectSession,
                Permission.ManageBillingSettings,
                // Products & inventory (Phase 3) — full management
                Permission.ViewProducts,
                Permission.ViewInventory,
                Permission.AddStock,
                Permission.AdjustInventory,
                Permission.RecordWasteDamage,
                Permission.ImportProducts,
                Permission.ExportProducts,
                // Sales, payments & receipts (Phase 4) — full operational access.
                Permission.CreateSale,
                Permission.ViewHeldSales,
                Permission.CancelDraftSale,
                Permission.CompletePayment,
                Permission.ApplyDiscount,
                Permission.OverridePrice,
                Permission.ViewSalesHistory,
                Permission.ReprintReceipt,
                // Bookings (Phase 5) — full management.
                Permission.ViewBookings,
                Permission.ManageBookings,
                // Reports & backups (Phase 6) — operational reporting, exports, shift
                // reports and profit; may create backups but not restore, and does not
                // administer settings or database health by default.
                Permission.ViewReports,
                Permission.ExportReports,
                Permission.ViewProfitReports,
                Permission.ViewShiftReports,
                Permission.CreateBackup,
                Permission.ManageBackupSettings,
            },
            [UserRole.Cashier] = new()
            {
                Permission.OpenShift,
                Permission.CloseShift,
                Permission.RecordCashMovement,
                Permission.TakePayment,
                // Cashiers run tables day-to-day, but cannot correct sessions,
                // manage table config, or change billing settings.
                Permission.ViewTables,
                Permission.StartSession,
                Permission.PauseResumeSession,
                Permission.TransferSession,
                Permission.FinishSession,
                // Products & inventory (Phase 3) — view/search only, no stock changes.
                Permission.ViewProducts,
                Permission.ViewInventory,
                // Sales, payments & receipts (Phase 4) — take sales/payments, view history,
                // but no unrestricted price override or payment-method management.
                Permission.CreateSale,
                Permission.ViewHeldSales,
                Permission.CancelDraftSale,
                Permission.CompletePayment,
                Permission.ViewSalesHistory,
                Permission.ReprintReceipt,
                // Bookings (Phase 5) — cashiers take reservations at the desk.
                Permission.ViewBookings,
                Permission.ManageBookings,
            },
            [UserRole.FloorStaff] = new()
            {
                // Floor staff assist at tables: view and start/pause/resume only.
                Permission.TakePayment,
                Permission.ViewTables,
                Permission.StartSession,
                Permission.PauseResumeSession,
                // Products & inventory (Phase 3) — can view products where needed.
                Permission.ViewProducts,
                // Sales (Phase 4) — may build/update drafts, but not complete payment.
                Permission.CreateSale,
                Permission.ViewHeldSales,
                // Bookings (Phase 5) — floor staff can view reservations.
                Permission.ViewBookings,
            },
        };

    /// <summary>True when the role is granted the given permission by default.</summary>
    public static bool Has(UserRole role, Permission permission) =>
        Map.TryGetValue(role, out var set) && set.Contains(permission);

    /// <summary>The full set of permissions granted to a role.</summary>
    public static IReadOnlyCollection<Permission> For(UserRole role) =>
        Map.TryGetValue(role, out var set) ? set : Array.Empty<Permission>();

    private static HashSet<Permission> AllPermissions() =>
        new(Enum.GetValues<Permission>());
}
