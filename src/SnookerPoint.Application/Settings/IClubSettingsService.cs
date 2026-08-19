namespace SnookerPoint.Application.Settings;

/// <summary>A read-only view of the saved club settings for display and app config.</summary>
public sealed record ClubSettingsView(
    string ClubName,
    string? Address,
    string? Phone,
    string CurrencyCode,
    string CurrencySymbol,
    string Theme,
    string Language,
    int ReceiptWidthMm,
    string? PrinterName,
    bool AutoPrintReceipt,
    string? BackupFolder,
    int ActiveTableCount);

/// <summary>Reads saved club settings (available only after setup completes).</summary>
public interface IClubSettingsService
{
    ClubSettingsView? Get();
}
