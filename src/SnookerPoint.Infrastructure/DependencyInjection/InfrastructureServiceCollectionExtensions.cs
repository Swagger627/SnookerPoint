using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Audit;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Backups;
using SnookerPoint.Application.Billing;
using SnookerPoint.Application.Bookings;
using SnookerPoint.Application.Catalog;
using SnookerPoint.Application.Diagnostics;
using SnookerPoint.Application.Reporting;
using SnookerPoint.Application.Sales;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Settings;
using SnookerPoint.Application.Setup;
using SnookerPoint.Application.Shifts;
using SnookerPoint.Application.Staff;
using SnookerPoint.Application.Tables;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Security;
using SnookerPoint.Infrastructure.Services;
using SnookerPoint.Infrastructure.Storage;
using SnookerPoint.Infrastructure.Time;

namespace SnookerPoint.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services: app-data paths, the system clock, the
/// SQLite-backed context factory, the startup initialiser, security, and the
/// Phase 1 application services.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddSnookerPointInfrastructure(this IServiceCollection services)
    {
        var paths = new AppDataPaths();
        paths.EnsureLiveDirectories();

        services.AddSingleton(paths);
        services.AddSingleton<IClock, SystemClock>();

        // A context factory (rather than a scoped context) suits a desktop app:
        // each operation creates and disposes its own short-lived context.
        services.AddDbContextFactory<SnookerPointDbContext>(options =>
            options.UseSqlite($"Data Source={paths.LiveDatabaseFile}"));

        services.AddSingleton<DatabaseInitializer>();

        // Security
        services.AddSingleton<ISecretHasher>(_ => new Pbkdf2SecretHasher());
        services.AddSingleton<IPermissionService, PermissionService>();

        // Phase 1 application services (stateless; each creates its own context)
        services.AddSingleton<ISetupService, SetupService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IShiftService, ShiftService>();
        services.AddSingleton<IClubSettingsService, ClubSettingsService>();
        services.AddSingleton<IAuditQueryService, AuditQueryService>();

        // Phase 2 — table sessions & billing
        services.AddSingleton<ISessionBillingCalculator, SessionBillingCalculator>();
        services.AddSingleton<IBillingSettingsService, BillingSettingsService>();
        services.AddSingleton<ITableSessionService, TableSessionService>();

        // Owner management — tables & staff
        services.AddSingleton<ITableManagementService, TableManagementService>();
        services.AddSingleton<IStaffManagementService, StaffManagementService>();

        // Account security & recovery
        services.AddSingleton<IAccountSecurityService, AccountSecurityService>();
        services.AddSingleton<IOwnerRecoveryService, OwnerRecoveryService>();

        // Phase 3 — catalogue & inventory
        services.AddSingleton<IProductImageStore, ProductImageStore>();
        services.AddSingleton<ICategoryService, CategoryService>();
        services.AddSingleton<IProductService, ProductService>();
        services.AddSingleton<IInventoryService, InventoryService>();
        services.AddSingleton<IProductCsvService, ProductCsvService>();
        services.AddSingleton<IProductLookupProvider, OpenFoodFactsLookupProvider>();

        // Phase 4 — sales, payments & receipts
        services.AddSingleton<IPaymentMethodService, PaymentMethodService>();
        services.AddSingleton<ISaleService, SaleService>();
        services.AddSingleton<ISalesQueryService, SalesQueryService>();

        // Phase 5 — bookings & reservations
        services.AddSingleton<IBookingService, BookingService>();

        // Phase 6 — reports, backups & administration
        services.AddSingleton<IReportingService, ReportingService>();
        services.AddSingleton<ICsvExportService, CsvExportService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IDatabaseHealthService, DatabaseHealthService>();
        services.AddSingleton<IOperationalSettingsService, OperationalSettingsService>();

        return services;
    }
}
