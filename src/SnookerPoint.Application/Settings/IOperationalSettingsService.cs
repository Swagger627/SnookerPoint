using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Settings;

/// <summary>The full editable operational settings, grouped for the Settings screen.</summary>
public sealed record OperationalSettingsView(
    // Club profile
    string ClubName,
    string? Address,
    string? Phone,
    int ReceiptWidthMm,
    bool AutoPrintReceipt,
    // Tax & service charge
    bool TaxEnabled,
    decimal TaxPercent,
    bool ServiceChargeEnabled,
    decimal ServiceChargePercent,
    // Backups
    bool AutoBackupEnabled,
    bool AutoBackupDaily,
    bool AutoBackupOnClose,
    int AutoBackupRetention,
    string? BackupFolder,
    DateTimeOffset? LastAutoBackupUtc,
    // Appearance / language (read-only summary here; changed via their own controls)
    string Theme,
    string Language);

public sealed record ClubProfileInput(string ClubName, string? Address, string? Phone, int ReceiptWidthMm, bool AutoPrintReceipt);

public sealed record TaxServiceInput(bool TaxEnabled, decimal TaxPercent, bool ServiceChargeEnabled, decimal ServiceChargePercent);

public sealed record BackupSettingsInput(bool AutoBackupEnabled, bool AutoBackupDaily, bool AutoBackupOnClose, int AutoBackupRetention, string? BackupFolder);

/// <summary>
/// Reads and updates operational club settings. Each update is permission-gated, validated,
/// and audited; tax and service charge default to 0% and disabled and affect new sales only
/// (existing completed sales are immutable).
/// </summary>
public interface IOperationalSettingsService
{
    OperationalSettingsView? Get();

    OperationResult UpdateClubProfile(ClubProfileInput input, int actorUserId);

    OperationResult UpdateTaxService(TaxServiceInput input, int actorUserId);

    OperationResult UpdateBackupSettings(BackupSettingsInput input, int actorUserId);
}
