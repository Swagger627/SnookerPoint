using SnookerPoint.Application.Audit;
using SnookerPoint.Application.Settings;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class AuditQueryServiceTests
{
    [Fact]
    public void Query_FiltersByAction()
    {
        using var env = new Phase1Environment();
        env.SeedOwnerShiftAndTables(12_000);

        var shiftOpened = env.Audit.Query(new AuditFilter(Action: "ShiftOpened"), 0, 100);
        Assert.NotEmpty(shiftOpened);
        Assert.All(shiftOpened, e => Assert.Equal("ShiftOpened", e.Action));
        Assert.Equal(shiftOpened.Count, env.Audit.Count(new AuditFilter(Action: "ShiftOpened")));
    }

    [Fact]
    public void Query_FiltersByModule()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(2), 60);

        var bookings = env.Audit.Query(new AuditFilter(Module: "Bookings"), 0, 100);
        Assert.NotEmpty(bookings);
        Assert.All(bookings, e => Assert.Equal("Bookings", e.Module));
    }

    [Fact]
    public void Query_Paginates()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tableIds) = env.SeedOwnerShiftAndTables(12_000);
        for (var i = 1; i <= 6; i++)
        {
            env.SeedBooking(ownerId, tableIds[0], env.Clock.UtcNow.AddHours(i * 2), 60);
        }

        var filter = new AuditFilter(Module: "Bookings");
        Assert.Equal(6, env.Audit.Count(filter));
        Assert.Equal(4, env.Audit.Query(filter, 0, 4).Count);
        Assert.Equal(2, env.Audit.Query(filter, 4, 4).Count);
    }

    [Fact]
    public void AuditDetails_ContainNoSecrets()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);   // setup uses the password "secret123"
        env.OperationalSettings.UpdateTaxService(new TaxServiceInput(true, 5m, false, 0m), ownerId);

        var all = env.Audit.Query(new AuditFilter(), 0, 10_000);
        Assert.NotEmpty(all);
        foreach (var e in all)
        {
            Assert.DoesNotContain("secret123", e.Details ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret123", e.Reference ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }
}
