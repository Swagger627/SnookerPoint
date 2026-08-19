using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SnookerPoint.Application.Billing;
using SnookerPoint.Application.Security;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Security;
using SnookerPoint.Infrastructure.Services;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>
/// A disposable, fully-migrated test environment over a temporary SQLite file, with
/// the Phase 1 services wired up (fast hashing for speed).
/// </summary>
public sealed class Phase1Environment : IDisposable
{
    private readonly string _dbPath;
    private readonly string _appDataRoot;

    public Phase1Environment()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"snookerpoint-test-{Guid.NewGuid():N}.db");
        _appDataRoot = Path.Combine(Path.GetTempPath(), $"snookerpoint-appdata-{Guid.NewGuid():N}");
        Factory = new TestDbContextFactory(_dbPath);
        Clock = new TestClock();
        Hasher = new Pbkdf2SecretHasher(iterations: 10_000);
        Paths = new AppDataPaths(_appDataRoot, machineRootOverride: Path.Combine(_appDataRoot, "MachineLicense"));
        Paths.EnsureLiveDirectories();

        using (var db = Factory.CreateDbContext())
        {
            db.Database.Migrate();
        }

        var permissions = new PermissionService();
        Setup = new SetupService(Factory, Hasher, Clock, NullLogger<SetupService>.Instance);
        Auth = new AuthenticationService(Factory, Hasher, Clock, NullLogger<AuthenticationService>.Instance);
        Shifts = new ShiftService(Factory, Clock, NullLogger<ShiftService>.Instance);
        ClubSettings = new ClubSettingsService(Factory);
        Audit = new AuditQueryService(Factory);

        Calculator = new SessionBillingCalculator();
        Billing = new BillingSettingsService(Factory, permissions, Clock, NullLogger<BillingSettingsService>.Instance);
        Sessions = new TableSessionService(Factory, Calculator, permissions, Clock, NullLogger<TableSessionService>.Instance);

        TableManagement = new TableManagementService(Factory, permissions, Clock, NullLogger<TableManagementService>.Instance);
        StaffManagement = new StaffManagementService(Factory, Hasher, permissions, Clock, NullLogger<StaffManagementService>.Instance);
        AccountSecurity = new AccountSecurityService(Factory, Hasher, Clock, NullLogger<AccountSecurityService>.Instance);
        OwnerRecovery = new OwnerRecoveryService(Factory, Hasher, Clock, NullLogger<OwnerRecoveryService>.Instance);

        // Phase 3 — catalogue & inventory
        Images = new ProductImageStore(Paths, NullLogger<ProductImageStore>.Instance);
        Categories = new CategoryService(Factory, permissions, Clock, NullLogger<CategoryService>.Instance);
        Products = new ProductService(Factory, permissions, Images, Clock, NullLogger<ProductService>.Instance);
        Inventory = new InventoryService(Factory, permissions, Clock, NullLogger<InventoryService>.Instance);
        ProductCsv = new ProductCsvService(Factory, permissions, Clock, NullLogger<ProductCsvService>.Instance);

        // Phase 4 — sales, payments & receipts
        PaymentMethods = new PaymentMethodService(Factory, permissions, Clock, NullLogger<PaymentMethodService>.Instance);
        Sales = new SaleService(Factory, permissions, Clock, NullLogger<SaleService>.Instance);
        SalesQuery = new SalesQueryService(Factory, permissions, Clock, NullLogger<SalesQueryService>.Instance);

        // Phase 5 — bookings & reservations
        Bookings = new BookingService(Factory, permissions, Sessions, Clock, NullLogger<BookingService>.Instance);

        // Phase 6 — reports, backups & administration
        Reporting = new ReportingService(Factory);
        Csv = new CsvExportService(Factory, Paths, Clock, NullLogger<CsvExportService>.Instance);
        Backups = new BackupService(Factory, Paths, Clock, NullLogger<BackupService>.Instance);
        Health = new DatabaseHealthService(Factory, Paths, Backups, Clock, NullLogger<DatabaseHealthService>.Instance);
        OperationalSettings = new OperationalSettingsService(Factory, permissions, Clock, NullLogger<OperationalSettingsService>.Instance);
    }

    public TestDbContextFactory Factory { get; }
    public TestClock Clock { get; }
    public Pbkdf2SecretHasher Hasher { get; }
    public AppDataPaths Paths { get; }

    public SetupService Setup { get; }
    public AuthenticationService Auth { get; }
    public ShiftService Shifts { get; }
    public ClubSettingsService ClubSettings { get; }
    public AuditQueryService Audit { get; }

    public ISessionBillingCalculator Calculator { get; }
    public BillingSettingsService Billing { get; }
    public TableSessionService Sessions { get; }
    public TableManagementService TableManagement { get; }
    public StaffManagementService StaffManagement { get; }
    public AccountSecurityService AccountSecurity { get; }
    public OwnerRecoveryService OwnerRecovery { get; }

    // Phase 3 — catalogue & inventory
    public ProductImageStore Images { get; }
    public CategoryService Categories { get; }
    public ProductService Products { get; }
    public InventoryService Inventory { get; }
    public ProductCsvService ProductCsv { get; }

    // Phase 4 — sales, payments & receipts
    public PaymentMethodService PaymentMethods { get; }
    public SaleService Sales { get; }
    public SalesQueryService SalesQuery { get; }

    // Phase 5 — bookings & reservations
    public BookingService Bookings { get; }

    // Phase 6 — reports, backups & administration
    public ReportingService Reporting { get; }
    public CsvExportService Csv { get; }
    public BackupService Backups { get; }
    public DatabaseHealthService Health { get; }
    public OperationalSettingsService OperationalSettings { get; }

    /// <summary>Creates a Scheduled booking and returns its id (actor needs ManageBookings).</summary>
    public int SeedBooking(int actorUserId, int tableId, DateTimeOffset startUtc, int durationMinutes = 60,
        string customerName = "Walk-in", string? phone = null, int? players = null, string? notes = null)
    {
        var result = Bookings.Create(new SnookerPoint.Application.Bookings.CreateBookingRequest(
            customerName, phone, tableId, startUtc, durationMinutes, players, notes), actorUserId);
        if (result.Failed)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.Value;
    }

    /// <summary>Creates a tracked product in a seeded category and returns its id.</summary>
    public int SeedProduct(int ownerId, int shiftId, string sku, long priceRupees, decimal opening = 100m,
        bool track = true, int? categoryId = null, string? barcode = null, long? costRupees = null)
    {
        var cat = categoryId ?? SeedCategory(ownerId, $"Cat-{Guid.NewGuid():N}".Substring(0, 12));
        var result = Products.Create(new SnookerPoint.Application.Catalog.CreateProductRequest(
            $"Product {sku}", sku, barcode, cat, null, null, null,
            SnookerPoint.Domain.Enums.ProductUnit.Each,
            costRupees is { } c ? SnookerPoint.Domain.ValueObjects.Money.FromRupees(c) : null,
            SnookerPoint.Domain.ValueObjects.Money.FromRupees(priceRupees),
            track, 5m, opening), ownerId, shiftId);
        if (result.Failed)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.Value;
    }

    /// <summary>The id of the seeded Cash payment method.</summary>
    public int CashMethodId => PaymentMethods.GetActive().First(m => m.Kind == SnookerPoint.Domain.Enums.PaymentMethodKind.Cash).Id;

    /// <summary>The id of a seeded electronic method by name (EasyPaisa/JazzCash/Bank Transfer).</summary>
    public int MethodId(string name) => PaymentMethods.GetActive().First(m => m.Name == name).Id;

    /// <summary>Creates an active category and returns its id (owner has ManageProducts).</summary>
    public int SeedCategory(int ownerId, string name = "Drinks")
    {
        var result = Categories.Create(name, ownerId);
        if (result.Failed)
        {
            throw new InvalidOperationException(result.ErrorMessage);
        }

        return result.Value;
    }

    public SnookerPointDbContext NewContext() => Factory.CreateDbContext();

    /// <summary>
    /// Completes setup (owner + tables) and opens an owner shift. Pass table rates in
    /// paisa to control them; otherwise five default tables are created.
    /// </summary>
    public (int OwnerId, int ShiftId, System.Collections.Generic.List<int> TableIds) SeedOwnerShiftAndTables(
        params long[] tableRatesPaisa)
    {
        var tables = tableRatesPaisa.Length > 0
            ? tableRatesPaisa
                .Select((r, i) => new SnookerPoint.Application.Setup.SetupTableInput(
                    $"Table {i + 1}",
                    SnookerPoint.Domain.Enums.TableType.Snooker,
                    SnookerPoint.Domain.ValueObjects.Money.FromPaisa(r),
                    true))
                .ToList()
            : SetupRequests.DefaultTables();

        var setup = Setup.CompleteSetup(SetupRequests.Valid(tables: tables));
        if (setup.Failed)
        {
            throw new InvalidOperationException(setup.ErrorMessage);
        }

        int ownerId;
        System.Collections.Generic.List<int> tableIds;
        using (var db = NewContext())
        {
            ownerId = db.Users.Single().Id;
            tableIds = db.PoolTables.OrderBy(t => t.SortOrder).Select(t => t.Id).ToList();
        }

        var shift = Shifts.OpenShift(ownerId, SnookerPoint.Domain.ValueObjects.Money.Zero, null);
        return (ownerId, shift.Value!.ShiftId, tableIds);
    }

    public void Dispose()
    {
        SafeDelete(_dbPath);
        SafeDelete(_dbPath + "-wal");
        SafeDelete(_dbPath + "-shm");

        try
        {
            if (Directory.Exists(_appDataRoot))
            {
                Directory.Delete(_appDataRoot, recursive: true);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort temp cleanup
        }
    }
}
