using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class CorrectionServiceTests
{
    private static int StartLive(Phase1Environment env, int tableId, int ownerId, int shiftId)
    {
        Assert.True(env.Sessions.StartSession(new StartSessionRequest(tableId, ownerId, shiftId, null, null)).Succeeded);
        return env.Sessions.GetDashboard().First(c => c.TableId == tableId).Session!.SessionId;
    }

    private static int CreateCashier(Phase1Environment env)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    /// <summary>Starts a session with one closed pause [+20,+30] and finishes at +60.</summary>
    private static (int SessionId, SessionCorrectionContext Context) PausedAndFinished(
        Phase1Environment env, int ownerId, int shiftId, int tableId)
    {
        var id = StartLive(env, tableId, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(20));
        env.Sessions.PauseSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.ResumeSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        env.Sessions.FinishSession(id, ownerId, shiftId, null); // active 50 min → 10000 paisa
        return (id, env.Sessions.GetCorrectionContext(id)!);
    }

    [Fact]
    public void PauseEndCorrection_RecalculatesCharge()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var (id, ctx) = PausedAndFinished(env, ownerId, shiftId, tables[0]);
        Assert.Equal(10_000, ctx.CurrentCharge.Paisa);

        var pause = ctx.Pauses.Single();
        // Extend the pause end by 10 minutes: pause [20,40] → active 40 min → 8000.
        var result = env.Sessions.CorrectPauseEnd(pause.PauseId, pause.ResumedUtc!.Value.AddMinutes(10), "counted wrong", ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(8_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);
    }

    [Fact]
    public void PauseStartCorrection_RecalculatesCharge()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var (id, ctx) = PausedAndFinished(env, ownerId, shiftId, tables[0]);

        var pause = ctx.Pauses.Single();
        // Move the pause start 5 minutes earlier: pause [15,30] → active 45 min → 9000.
        var result = env.Sessions.CorrectPauseStart(pause.PauseId, pause.PausedUtc.AddMinutes(-5), "paused earlier", ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(9_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);
    }

    [Fact]
    public void PauseCorrection_ThatPredatesSessionStart_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var (_, ctx) = PausedAndFinished(env, ownerId, shiftId, tables[0]);

        var pause = ctx.Pauses.Single();
        var result = env.Sessions.CorrectPauseStart(pause.PauseId, ctx.StartUtc.AddMinutes(-5), "oops", ownerId, shiftId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void OverlappingPauseCorrection_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        var id = StartLive(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(20));
        env.Sessions.PauseSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.ResumeSession(id, ownerId, shiftId);   // pause1 [20,30]
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.PauseSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.ResumeSession(id, ownerId, shiftId);   // pause2 [40,50]
        env.Clock.Advance(TimeSpan.FromMinutes(20));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);

        var ctx = env.Sessions.GetCorrectionContext(id)!;
        var pause2 = ctx.Pauses.OrderBy(p => p.PausedUtc).Last();
        // Move pause2 start back onto pause1's window → overlap.
        var result = env.Sessions.CorrectPauseStart(pause2.PauseId, pause2.PausedUtc.AddMinutes(-20), "overlap", ownerId, shiftId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void SegmentRateCorrection_RecalculatesCharge_WithoutChangingTableRate()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartLive(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(id, ownerId, shiftId, null); // 12000

        var ctx = env.Sessions.GetCorrectionContext(id)!;
        var segment = ctx.Segments.Single();

        var result = env.Sessions.CorrectSegmentRate(segment.SegmentId, Money.FromPaisa(24_000), "wrong rate applied", ownerId, shiftId);
        Assert.True(result.Succeeded, result.ErrorMessage);

        Assert.Equal(24_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);

        using var db = env.NewContext();
        Assert.Equal(12_000, db.PoolTables.Single(t => t.Id == tables[0]).HourlyRate.Paisa); // table rate unchanged
    }

    [Fact]
    public void RateCorrection_PreservesOriginalValue_ForAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartLive(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);

        var segment = env.Sessions.GetCorrectionContext(id)!.Segments.Single();
        env.Sessions.CorrectSegmentRate(segment.SegmentId, Money.FromPaisa(24_000), "fix rate", ownerId, shiftId);

        using var db = env.NewContext();
        var adjustment = db.SessionAdjustments.Single(a => a.SessionId == id && a.Type == SessionAdjustmentType.RateCorrection);
        Assert.Equal(Money.FromPaisa(12_000).Format(), adjustment.OldValue);
        Assert.Equal("fix rate", adjustment.Reason);
        Assert.Equal(ownerId, adjustment.ApprovedByUserId);
    }

    [Fact]
    public void PauseCorrection_PreservesOriginalValue_ForAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var (id, ctx) = PausedAndFinished(env, ownerId, shiftId, tables[0]);
        var pause = ctx.Pauses.Single();

        env.Sessions.CorrectPauseStart(pause.PauseId, pause.PausedUtc.AddMinutes(-5), "earlier", ownerId, shiftId);

        using var db = env.NewContext();
        var adjustment = db.SessionAdjustments.Single(a => a.SessionId == id && a.Type == SessionAdjustmentType.PauseResumeCorrection);
        Assert.Contains(pause.PausedUtc.ToString("O"), adjustment.OldValue);
    }

    [Fact]
    public void RateCorrection_RequiresPermission()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartLive(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);
        var segment = env.Sessions.GetCorrectionContext(id)!.Segments.Single();
        var cashier = CreateCashier(env);

        var result = env.Sessions.CorrectSegmentRate(segment.SegmentId, Money.FromPaisa(24_000), "no perm", cashier, shiftId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void PauseCorrection_RequiresReason()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var (_, ctx) = PausedAndFinished(env, ownerId, shiftId, tables[0]);
        var pause = ctx.Pauses.Single();

        var result = env.Sessions.CorrectPauseStart(pause.PauseId, pause.PausedUtc.AddMinutes(-5), "   ", ownerId, shiftId);
        Assert.True(result.Failed);
    }
}
