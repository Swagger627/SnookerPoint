using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Settings;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Reads and updates operational club settings. Updates are permission-gated, validated and
/// audited. Tax and service charge default to 0% and disabled; changing them affects new
/// sales only because completed sales freeze their own totals.
/// </summary>
public sealed class OperationalSettingsService : IOperationalSettingsService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<OperationalSettingsService> _logger;

    public OperationalSettingsService(
        IDbContextFactory<SnookerPointDbContext> factory,
        IPermissionService permissions,
        IClock clock,
        ILogger<OperationalSettingsService> logger)
    {
        _factory = factory;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public OperationalSettingsView? Get()
    {
        using var db = _factory.CreateDbContext();
        var s = db.ClubSettings.AsNoTracking().FirstOrDefault();
        if (s is null)
        {
            return null;
        }

        return new OperationalSettingsView(
            s.ClubName, s.Address, s.Phone, s.ReceiptWidthMm, s.AutoPrintReceipt,
            s.TaxEnabled, s.TaxPercent, s.ServiceChargeEnabled, s.ServiceChargePercent,
            s.AutoBackupEnabled, s.AutoBackupDaily, s.AutoBackupOnClose, s.AutoBackupRetention, s.BackupFolder, s.LastAutoBackupUtc,
            s.Theme, s.Language);
    }

    public OperationResult UpdateClubProfile(ClubProfileInput input, int actorUserId)
    {
        if (string.IsNullOrWhiteSpace(input.ClubName))
        {
            return OperationResult.Failure("Please enter the club name.");
        }

        if (input.ReceiptWidthMm is not (58 or 80))
        {
            return OperationResult.Failure("Receipt width must be 58 mm or 80 mm.");
        }

        return Mutate(actorUserId, Permission.ManageSettings, AuditActions.SettingsUpdated, "Club profile updated.", s =>
        {
            s.ClubName = input.ClubName.Trim();
            s.Address = Clean(input.Address);
            s.Phone = Clean(input.Phone);
            s.ReceiptWidthMm = input.ReceiptWidthMm;
            s.AutoPrintReceipt = input.AutoPrintReceipt;
        });
    }

    public OperationResult UpdateTaxService(TaxServiceInput input, int actorUserId)
    {
        if (input.TaxPercent is < 0 or > 100 || input.ServiceChargePercent is < 0 or > 100)
        {
            return OperationResult.Failure("Tax and service charge percentages must be between 0 and 100.");
        }

        return Mutate(actorUserId, Permission.ConfigureTaxService, AuditActions.TaxServiceUpdated,
            $"Tax {(input.TaxEnabled ? input.TaxPercent + "%" : "off")}, service {(input.ServiceChargeEnabled ? input.ServiceChargePercent + "%" : "off")}.", s =>
        {
            s.TaxEnabled = input.TaxEnabled;
            s.TaxPercent = input.TaxEnabled ? input.TaxPercent : 0m;
            s.ServiceChargeEnabled = input.ServiceChargeEnabled;
            s.ServiceChargePercent = input.ServiceChargeEnabled ? input.ServiceChargePercent : 0m;
        });
    }

    public OperationResult UpdateBackupSettings(BackupSettingsInput input, int actorUserId)
    {
        if (input.AutoBackupRetention < 1)
        {
            return OperationResult.Failure("Keep at least one backup (retention must be 1 or more).");
        }

        return Mutate(actorUserId, Permission.ManageBackupSettings, AuditActions.BackupSettingsUpdated,
            $"Automatic backups {(input.AutoBackupEnabled ? "on" : "off")}, keep {input.AutoBackupRetention}.", s =>
        {
            s.AutoBackupEnabled = input.AutoBackupEnabled;
            s.AutoBackupDaily = input.AutoBackupDaily;
            s.AutoBackupOnClose = input.AutoBackupOnClose;
            s.AutoBackupRetention = input.AutoBackupRetention;
            s.BackupFolder = Clean(input.BackupFolder);
        });
    }

    private OperationResult Mutate(int actorUserId, Permission permission, string action, string details, Action<ClubSettings> apply)
    {
        using var db = _factory.CreateDbContext();
        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, permission))
        {
            return OperationResult.Failure("You do not have permission to change these settings.");
        }

        var settings = db.ClubSettings.FirstOrDefault();
        if (settings is null)
        {
            return OperationResult.Failure("Settings are not available until setup is complete.");
        }

        apply(settings);
        settings.UpdatedUtc = _clock.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = "Settings",
            Details = details,
        });

        db.SaveChanges();
        return OperationResult.Success();
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
