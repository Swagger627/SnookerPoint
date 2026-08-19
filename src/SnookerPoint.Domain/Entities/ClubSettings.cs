namespace SnookerPoint.Domain.Entities;

/// <summary>
/// Single-row club configuration captured by the first-run setup wizard. The
/// presence of a row with <see cref="IsSetupComplete"/> = true is what tells the
/// app that first-run setup is done and the wizard should not reappear.
/// </summary>
public sealed class ClubSettings
{
    /// <summary>Fixed primary key — there is only ever one settings row (Id = 1).</summary>
    public int Id { get; set; } = 1;

    public string ClubName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? Phone { get; set; }

    /// <summary>ISO 4217 currency code. Fixed to PKR in the first release.</summary>
    public string CurrencyCode { get; set; } = "PKR";

    /// <summary>Display symbol. Fixed to "Rs" in the first release.</summary>
    public string CurrencySymbol { get; set; } = "Rs";

    /// <summary>"Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>UI language code. "en" in the first release; architecture stays localisable.</summary>
    public string Language { get; set; } = "en";

    /// <summary>Receipt paper width in millimetres (58 or 80).</summary>
    public int ReceiptWidthMm { get; set; } = 58;

    /// <summary>Optional selected printer name (printing itself is a later phase).</summary>
    public string? PrinterName { get; set; }

    /// <summary>Whether receipts should print automatically after checkout (later phase).</summary>
    public bool AutoPrintReceipt { get; set; }

    /// <summary>Optional chosen backup folder (backup/restore is a later phase).</summary>
    public string? BackupFolder { get; set; }

    // --- Tax & service charge (Phase 6; disabled and 0% by default) ---

    /// <summary>Whether a sales tax percentage is applied at checkout. Off by default.</summary>
    public bool TaxEnabled { get; set; }

    /// <summary>Sales tax percentage (0–100). Applied only when <see cref="TaxEnabled"/>.</summary>
    public decimal TaxPercent { get; set; }

    /// <summary>Whether a service charge percentage is applied at checkout. Off by default.</summary>
    public bool ServiceChargeEnabled { get; set; }

    /// <summary>Service charge percentage (0–100). Applied only when <see cref="ServiceChargeEnabled"/>.</summary>
    public decimal ServiceChargePercent { get; set; }

    // --- Automatic backups (Phase 6) ---

    /// <summary>Whether automatic backups run. Off by default.</summary>
    public bool AutoBackupEnabled { get; set; }

    /// <summary>Run an automatic backup once per day when the app is used. Off by default.</summary>
    public bool AutoBackupDaily { get; set; }

    /// <summary>Run an automatic backup when the application closes. Off by default.</summary>
    public bool AutoBackupOnClose { get; set; }

    /// <summary>How many automatic backups to keep (oldest managed backups pruned beyond this).</summary>
    public int AutoBackupRetention { get; set; } = 7;

    /// <summary>The UTC date of the last successful automatic backup, to avoid duplicate daily runs.</summary>
    public DateTimeOffset? LastAutoBackupUtc { get; set; }

    public bool IsSetupComplete { get; set; }
    public DateTimeOffset? SetupCompletedUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
}
