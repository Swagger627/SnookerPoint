using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class TableManagementServiceTests
{
    private static List<TableDraft> Drafts(IEnumerable<TableListItem> items) =>
        items.Select(i => new TableDraft(i.Id, i.Name, i.Type, i.HourlyRate, i.IsActive)).ToList();

    private static int CreateCashier(Phase1Environment env)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    [Fact]
    public void SaveLayout_RequiresManageTablesPermission()
    {
        using var env = new Phase1Environment();
        var (_, _, _) = env.SeedOwnerShiftAndTables(12_000);
        var cashier = CreateCashier(env);

        var drafts = Drafts(env.TableManagement.GetAll());
        var result = env.TableManagement.SaveLayout(drafts, cashier);

        Assert.True(result.Failed);
    }

    [Fact]
    public void SaveLayout_AddsNewTable()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var drafts = Drafts(env.TableManagement.GetAll());
        drafts.Add(new TableDraft(null, "Table X", TableType.Pool, Money.FromPaisa(20_000), true));

        var result = env.TableManagement.SaveLayout(drafts, ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);

        var all = env.TableManagement.GetAll();
        Assert.Contains(all, t => t.Name == "Table X" && t.Type == TableType.Pool && t.HourlyRate.Paisa == 20_000);
    }

    [Fact]
    public void SaveLayout_UpdatesRate()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tables) = env.SeedOwnerShiftAndTables(12_000);

        var drafts = Drafts(env.TableManagement.GetAll());
        var first = drafts[0];
        drafts[0] = first with { HourlyRate = Money.FromPaisa(30_000) };

        var result = env.TableManagement.SaveLayout(drafts, ownerId);
        Assert.True(result.Succeeded, result.ErrorMessage);

        var reloaded = env.TableManagement.GetAll().Single(t => t.Id == tables[0]);
        Assert.Equal(30_000, reloaded.HourlyRate.Paisa);
    }

    [Fact]
    public void RateChange_DoesNotAffectRunningSessionSnapshot()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null));

        var drafts = Drafts(env.TableManagement.GetAll());
        drafts[0] = drafts[0] with { HourlyRate = Money.FromPaisa(30_000) };
        Assert.True(env.TableManagement.SaveLayout(drafts, ownerId).Succeeded);

        using var db = env.NewContext();
        // The table's configured rate changed...
        Assert.Equal(30_000, db.PoolTables.Single(t => t.Id == tables[0]).HourlyRate.Paisa);
        // ...but the live session's snapshotted segment rate is untouched.
        var segment = db.SessionSegments.Single(s => s.TableId == tables[0]);
        Assert.Equal(12_000, segment.HourlyRate.Paisa);
    }

    [Fact]
    public void NegativeRate_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000);

        var drafts = Drafts(env.TableManagement.GetAll());
        drafts[0] = drafts[0] with { HourlyRate = Money.FromPaisa(-100) };

        Assert.True(env.TableManagement.SaveLayout(drafts, ownerId).Failed);
    }

    [Fact]
    public void DuplicateActiveName_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, _) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        var drafts = Drafts(env.TableManagement.GetAll());
        drafts[1] = drafts[1] with { Name = drafts[0].Name.ToUpperInvariant() }; // case-insensitive clash

        Assert.True(env.TableManagement.SaveLayout(drafts, ownerId).Failed);
    }

    [Fact]
    public void CannotDeactivateTableInUse()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null));

        var drafts = Drafts(env.TableManagement.GetAll());
        var target = drafts.FindIndex(d => d.Id == tables[0]);
        drafts[target] = drafts[target] with { IsActive = false };

        var result = env.TableManagement.SaveLayout(drafts, ownerId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void DeactivateIdleTable_KeepsItButInactive()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tables) = env.SeedOwnerShiftAndTables(12_000, 12_000);

        var drafts = Drafts(env.TableManagement.GetAll());
        var target = drafts.FindIndex(d => d.Id == tables[1]);
        drafts[target] = drafts[target] with { IsActive = false };

        Assert.True(env.TableManagement.SaveLayout(drafts, ownerId).Succeeded, "deactivate idle table");

        var reloaded = env.TableManagement.GetAll().Single(t => t.Id == tables[1]);
        Assert.False(reloaded.IsActive); // still present, just inactive (not deleted)
    }
}
