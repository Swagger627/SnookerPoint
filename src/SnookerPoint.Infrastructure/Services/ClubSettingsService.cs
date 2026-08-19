using Microsoft.EntityFrameworkCore;
using SnookerPoint.Application.Settings;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>Reads saved club settings for display and app configuration.</summary>
public sealed class ClubSettingsService : IClubSettingsService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;

    public ClubSettingsService(IDbContextFactory<SnookerPointDbContext> factory)
    {
        _factory = factory;
    }

    public ClubSettingsView? Get()
    {
        using var db = _factory.CreateDbContext();
        var settings = db.ClubSettings.AsNoTracking().FirstOrDefault(c => c.IsSetupComplete);
        if (settings is null)
        {
            return null;
        }

        var activeTableCount = db.PoolTables.AsNoTracking().Count(t => t.IsActive);

        return new ClubSettingsView(
            settings.ClubName,
            settings.Address,
            settings.Phone,
            settings.CurrencyCode,
            settings.CurrencySymbol,
            settings.Theme,
            settings.Language,
            settings.ReceiptWidthMm,
            settings.PrinterName,
            settings.AutoPrintReceipt,
            settings.BackupFolder,
            activeTableCount);
    }
}
