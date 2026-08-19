using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Infrastructure.Persistence;

/// <summary>
/// The application's EF Core database context. Phase 0 added the foundational
/// <see cref="AppInfo"/> table and the migration pipeline; Phase 1 adds the setup,
/// staff, shift and audit tables. Money is stored as INTEGER minor units, enums as
/// text, and financial/audit foreign keys use RESTRICT.
/// </summary>
public sealed class SnookerPointDbContext : DbContext
{
    /// <summary>Converts the <see cref="Money"/> value object to/from INTEGER paisa.</summary>
    private static readonly ValueConverter<Money, long> MoneyConverter =
        new(m => m.Paisa, p => Money.FromPaisa(p));

    public SnookerPointDbContext(DbContextOptions<SnookerPointDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppInfo> AppInfo => Set<AppInfo>();
    public DbSet<ClubSettings> ClubSettings => Set<ClubSettings>();
    public DbSet<PoolTable> PoolTables => Set<PoolTable>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // Phase 2 — table sessions & billing
    public DbSet<BillingSettings> BillingSettings => Set<BillingSettings>();
    public DbSet<TableSession> TableSessions => Set<TableSession>();
    public DbSet<SessionSegment> SessionSegments => Set<SessionSegment>();
    public DbSet<SessionPause> SessionPauses => Set<SessionPause>();
    public DbSet<SessionAdjustment> SessionAdjustments => Set<SessionAdjustment>();

    // Phase 3 — catalogue & inventory
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    // Phase 4 — sales, payments & receipts
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleLine> SaleLines => Set<SaleLine>();
    public DbSet<SalePayment> SalePayments => Set<SalePayment>();
    public DbSet<PaymentMethod> PaymentMethods => Set<PaymentMethod>();

    // Phase 5 — bookings & reservations
    public DbSet<Booking> Bookings => Set<Booking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppInfo>(entity =>
        {
            entity.ToTable("AppInfo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SchemaVersion).IsRequired();
            entity.Property(e => e.AppVersion).IsRequired();
            entity.Property(e => e.InstalledUtc).IsRequired();
        });

        modelBuilder.Entity<ClubSettings>(entity =>
        {
            entity.ToTable("ClubSettings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.ClubName).IsRequired();
            entity.Property(e => e.CurrencyCode).IsRequired();
            entity.Property(e => e.CurrencySymbol).IsRequired();
            entity.Property(e => e.Theme).IsRequired();
            entity.Property(e => e.Language).IsRequired();
        });

        modelBuilder.Entity<PoolTable>(entity =>
        {
            entity.ToTable("PoolTables");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Type).HasConversion<string>().IsRequired();
            entity.Property(e => e.HourlyRate).HasConversion(MoneyConverter).IsRequired();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DisplayName).IsRequired();
            entity.Property(e => e.Username).IsRequired();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.Property(e => e.Role).HasConversion<string>().IsRequired();
            entity.Property(e => e.PasswordHash).IsRequired();
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.ToTable("Shifts");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasConversion<string>().IsRequired();
            entity.Property(e => e.OpeningCash).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.ExpectedCash).HasConversion(MoneyConverter!);
            entity.Property(e => e.CountedCash).HasConversion(MoneyConverter!);
            entity.Property(e => e.Variance).HasConversion(MoneyConverter!);

            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // A user may have at most one open shift (enforced in the service; the
            // filtered index provides a database-level backstop on SQLite).
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
        });

        modelBuilder.Entity<CashMovement>(entity =>
        {
            entity.ToTable("CashMovements");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().IsRequired();
            entity.Property(e => e.Amount).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.Reason).IsRequired();

            entity.HasOne(e => e.Shift)
                .WithMany()
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Action).IsRequired();

            entity.HasOne(e => e.Actor)
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Utc);
        });

        ConfigurePhase2(modelBuilder);
        ConfigurePhase3(modelBuilder);
        ConfigurePhase4(modelBuilder);
        ConfigurePhase5(modelBuilder);
    }

    private static void ConfigurePhase5(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Booking>(entity =>
        {
            entity.ToTable("Bookings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CustomerName).IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().IsRequired();

            entity.HasOne<PoolTable>()
                .WithMany()
                .HasForeignKey(e => e.TableId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TableSession>()
                .WithMany()
                .HasForeignKey(e => e.LinkedSessionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.TableId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartUtc);
        });
    }

    private static void ConfigurePhase4(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentMethod>(entity =>
        {
            entity.ToTable("PaymentMethods");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Kind).HasConversion<string>().IsRequired();
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<Sale>(entity =>
        {
            entity.ToTable("Sales");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().IsRequired();
            entity.Property(e => e.Status).HasConversion<string>().IsRequired();
            entity.Property(e => e.TableBillingType).HasConversion<string>();
            entity.Property(e => e.DiscountKind).HasConversion<string>().IsRequired();
            entity.Property(e => e.TableCharge).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.DiscountAmount).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.TaxAmount).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.ServiceAmount).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.Subtotal).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.Total).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.CashReceived).HasConversion(MoneyConverter!);
            entity.Property(e => e.ChangeGiven).HasConversion(MoneyConverter!);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<TableSession>()
                .WithMany()
                .HasForeignKey(e => e.TableSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Shift>()
                .WithMany()
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            // A completed sale's number is unique; drafts have no number.
            entity.HasIndex(e => e.SaleNumber).IsUnique().HasFilter("\"SaleNumber\" IS NOT NULL");
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ShiftId);
            entity.HasIndex(e => e.CreatedByUserId);
            entity.HasIndex(e => e.CompletedUtc);
            // At most one open (Draft/Held) sale may be attached to a table session.
            entity.HasIndex(e => e.TableSessionId)
                .IsUnique()
                .HasFilter("\"TableSessionId\" IS NOT NULL AND \"Status\" IN ('Draft', 'Held')");

            entity.HasMany(e => e.Lines)
                .WithOne(l => l.Sale!)
                .HasForeignKey(l => l.SaleId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(e => e.Payments)
                .WithOne(p => p.Sale!)
                .HasForeignKey(p => p.SaleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SaleLine>(entity =>
        {
            entity.ToTable("SaleLines");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.NameSnapshot).IsRequired();
            entity.Property(e => e.UnitPrice).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.CostSnapshot).HasConversion(MoneyConverter!);
            entity.Property(e => e.OriginalUnitPrice).HasConversion(MoneyConverter!);
            entity.Property(e => e.LineTotal).HasConversion(MoneyConverter).IsRequired();

            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.SaleId);
        });

        modelBuilder.Entity<SalePayment>(entity =>
        {
            entity.ToTable("SalePayments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Kind).HasConversion<string>().IsRequired();
            entity.Property(e => e.MethodNameSnapshot).IsRequired();
            entity.Property(e => e.Amount).HasConversion(MoneyConverter).IsRequired();
            entity.Property(e => e.ReceivedAmount).HasConversion(MoneyConverter!);
            entity.Property(e => e.ChangeAmount).HasConversion(MoneyConverter!);

            entity.HasOne<PaymentMethod>()
                .WithMany()
                .HasForeignKey(e => e.MethodId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.SaleId);
        });
    }

    private static void ConfigurePhase3(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Categories");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.HasIndex(e => e.Name);
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Sku).IsRequired();
            entity.Property(e => e.Unit).HasConversion<string>().IsRequired();
            entity.Property(e => e.Cost).HasConversion(MoneyConverter!);
            entity.Property(e => e.Price).HasConversion(MoneyConverter).IsRequired();

            entity.HasOne<Category>()
                .WithMany()
                .HasForeignKey(e => e.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // SKU is globally unique; barcode is unique only when supplied.
            entity.HasIndex(e => e.Sku).IsUnique();
            entity.HasIndex(e => e.Barcode).IsUnique().HasFilter("\"Barcode\" IS NOT NULL");
            entity.HasIndex(e => e.CategoryId);
            entity.HasIndex(e => e.IsActive);
        });

        modelBuilder.Entity<StockMovement>(entity =>
        {
            entity.ToTable("StockMovements");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().IsRequired();

            entity.HasOne<Product>()
                .WithMany()
                .HasForeignKey(e => e.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.ActorUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Shift>()
                .WithMany()
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.ProductId);
            entity.HasIndex(e => e.Utc);
        });
    }

    private static void ConfigurePhase2(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingSettings>(entity =>
        {
            entity.ToTable("BillingSettings");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Method).HasConversion<string>().IsRequired();
        });

        modelBuilder.Entity<TableSession>(entity =>
        {
            entity.ToTable("TableSessions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Status).HasConversion<string>().IsRequired();
            entity.Property(e => e.CheckoutStatus).HasConversion<string>().IsRequired();
            entity.Property(e => e.BillingType).HasConversion<string>().IsRequired();
            entity.Property(e => e.FixedAmount).HasConversion(MoneyConverter!);
            entity.Property(e => e.BillingMethod).HasConversion<string>().IsRequired();
            entity.Property(e => e.FinalCharge).HasConversion(MoneyConverter!);

            entity.HasOne<PoolTable>()
                .WithMany()
                .HasForeignKey(e => e.CurrentTableId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.OpenedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Shift>()
                .WithMany()
                .HasForeignKey(e => e.OpenedShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.SessionNumber).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.StartUtc);
            entity.HasIndex(e => e.OpenedShiftId);
            entity.HasIndex(e => e.OpenedByUserId);

            // At most one live (Active/Paused) session per table — a database backstop
            // to the application check.
            entity.HasIndex(e => e.CurrentTableId)
                .IsUnique()
                .HasFilter("\"Status\" IN ('Active', 'Paused')");

            entity.HasMany(e => e.Segments)
                .WithOne(s => s.Session!)
                .HasForeignKey(s => s.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Pauses)
                .WithOne(p => p.Session!)
                .HasForeignKey(p => p.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(e => e.Adjustments)
                .WithOne(a => a.Session!)
                .HasForeignKey(a => a.SessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SessionSegment>(entity =>
        {
            entity.ToTable("SessionSegments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HourlyRate).HasConversion(MoneyConverter).IsRequired();

            entity.HasOne<PoolTable>()
                .WithMany()
                .HasForeignKey(e => e.TableId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.TableId);
        });

        modelBuilder.Entity<SessionPause>(entity =>
        {
            entity.ToTable("SessionPauses");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SessionId);
        });

        modelBuilder.Entity<SessionAdjustment>(entity =>
        {
            entity.ToTable("SessionAdjustments");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasConversion<string>().IsRequired();
            entity.Property(e => e.Reason).IsRequired();
            entity.Property(e => e.Amount).HasConversion(MoneyConverter!);

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.ApprovedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Shift>()
                .WithMany()
                .HasForeignKey(e => e.ShiftId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.SessionId);
        });
    }
}
