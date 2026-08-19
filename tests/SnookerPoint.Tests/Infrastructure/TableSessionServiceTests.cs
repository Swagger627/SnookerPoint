using Microsoft.Extensions.Logging.Abstractions;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Services;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class TableSessionServiceTests
{
    private static int Start(Phase1Environment env, int tableId, int ownerId, int shiftId)
    {
        var result = env.Sessions.StartSession(new StartSessionRequest(tableId, ownerId, shiftId, null, null));
        Assert.True(result.Succeeded, result.ErrorMessage);
        return env.Sessions.GetDashboard().First(c => c.TableId == tableId).Session!.SessionId;
    }

    private static DashboardStatus StatusOf(Phase1Environment env, int tableId) =>
        env.Sessions.GetDashboard().First(c => c.TableId == tableId).Status;

    private static int CreateCashier(Phase1Environment env)
    {
        using var db = env.NewContext();
        var user = new User { DisplayName = "Cash", Username = "cash", Role = UserRole.Cashier, PasswordHash = "x", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();
        return user.Id;
    }

    // ---------- Start / shift / duplicates ----------

    [Fact]
    public void StartSession_RequiresOpenShift()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        env.Shifts.CloseShift(shiftId, Money.Zero, null); // no open shift now

        var result = env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null));

        Assert.True(result.Failed);
    }

    [Fact]
    public void StartSession_Succeeds_AndMarksTableInUse()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        Start(env, tables[0], ownerId, shiftId);

        Assert.Equal(DashboardStatus.InUse, StatusOf(env, tables[0]));
    }

    [Fact]
    public void OnlyOneActiveSessionPerTable()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        Start(env, tables[0], ownerId, shiftId);

        var second = env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null));

        Assert.True(second.Failed);
    }

    [Fact]
    public void DuplicateStart_DoesNotCreateTwoSessions()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        var first = env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null));
        var second = env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null));

        Assert.True(first.Succeeded);
        Assert.True(second.Failed);
        using var db = env.NewContext();
        Assert.Equal(1, db.TableSessions.Count(s => s.CurrentTableId == tables[0] &&
            (s.Status == SessionStatus.Active || s.Status == SessionStatus.Paused)));
    }

    // ---------- Pause / resume ----------

    [Fact]
    public void Pause_StopsBillableTime_Resume_Continues()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        env.Clock.Advance(TimeSpan.FromMinutes(60));       // 60 min active
        Assert.True(env.Sessions.PauseSession(id, ownerId, shiftId).Succeeded);
        env.Clock.Advance(TimeSpan.FromMinutes(30));       // 30 min paused (not billable)
        Assert.True(env.Sessions.ResumeSession(id, ownerId, shiftId).Succeeded);
        env.Clock.Advance(TimeSpan.FromMinutes(15));       // 15 min active

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);

        Assert.True(finish.Succeeded, finish.ErrorMessage);
        Assert.Equal(75 * 60, finish.Value!.BillableSeconds);     // 60 + 15
        Assert.Equal(15_000, finish.Value.Charge.Paisa);          // 75/60 * 12000
    }

    [Fact]
    public void MultiplePausePeriods_CalculateCorrectly()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        env.Clock.Advance(TimeSpan.FromMinutes(20));
        env.Sessions.PauseSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.ResumeSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(20));
        env.Sessions.PauseSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(5));
        env.Sessions.ResumeSession(id, ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(20));

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);
        Assert.Equal(60 * 60, finish.Value!.BillableSeconds); // 20+20+20 active
    }

    [Fact]
    public void InvalidTransitions_AreRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        Assert.True(env.Sessions.ResumeSession(id, ownerId, shiftId).Failed);  // active cannot resume
        env.Sessions.PauseSession(id, ownerId, shiftId);
        Assert.True(env.Sessions.PauseSession(id, ownerId, shiftId).Failed);   // paused cannot pause again
    }

    // ---------- Recovery ----------

    [Fact]
    public void ActiveSession_RecoversAfterRestart()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(40));

        // A fresh service instance reads state purely from SQLite.
        var restarted = new TableSessionService(env.Factory, env.Calculator, new PermissionService(),
            env.Clock, NullLogger<TableSessionService>.Instance);

        var card = restarted.GetDashboard().First(c => c.TableId == tables[0]);
        Assert.Equal(DashboardStatus.InUse, card.Status);
        Assert.Equal(id, card.Session!.SessionId);
        var charge = env.Calculator.Calculate(card.Session.Policy, card.Session.Segments, card.Session.Pauses, env.Clock.UtcNow);
        Assert.Equal(2400, charge.ActiveSeconds); // 40 min preserved
    }

    [Fact]
    public void PausedSession_RecoversAfterRestart()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(10));
        env.Sessions.PauseSession(id, ownerId, shiftId);

        var restarted = new TableSessionService(env.Factory, env.Calculator, new PermissionService(),
            env.Clock, NullLogger<TableSessionService>.Instance);

        Assert.Equal(DashboardStatus.Paused, restarted.GetDashboard().First(c => c.TableId == tables[0]).Status);
    }

    // ---------- Finish ----------

    [Fact]
    public void Finish_FreezesCharge_AndTableBecomesAvailable_AwaitingCheckout()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, "done");
        Assert.True(finish.Succeeded);
        Assert.Equal(12_000, finish.Value!.Charge.Paisa);

        Assert.Equal(DashboardStatus.Available, StatusOf(env, tables[0]));

        // Advancing the clock does not change the frozen charge.
        env.Clock.Advance(TimeSpan.FromMinutes(120));
        var summary = env.Sessions.GetSessionSummary(id)!;
        Assert.Equal(12_000, summary.Charge.Paisa);

        using var db = env.NewContext();
        var session = db.TableSessions.Single(s => s.Id == id);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(CheckoutStatus.AwaitingCheckout, session.CheckoutStatus);
    }

    [Fact]
    public void CompletedSession_CannotBeReopened()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);

        Assert.True(env.Sessions.PauseSession(id, ownerId, shiftId).Failed);
        Assert.True(env.Sessions.ResumeSession(id, ownerId, shiftId).Failed);
        Assert.True(env.Sessions.FinishSession(id, ownerId, shiftId, null).Failed);
    }

    // ---------- Transfer ----------

    [Fact]
    public void Transfer_ToAvailableTable_PreservesIdentity()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000, 24_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        var transfer = env.Sessions.TransferSession(id, tables[1], ownerId, shiftId, "customer moved");
        Assert.True(transfer.Succeeded, transfer.ErrorMessage);

        Assert.Equal(DashboardStatus.Available, StatusOf(env, tables[0]));
        Assert.Equal(DashboardStatus.InUse, StatusOf(env, tables[1]));

        var card = env.Sessions.GetDashboard().First(c => c.TableId == tables[1]);
        Assert.Equal(id, card.Session!.SessionId); // same session identity
        using var db = env.NewContext();
        Assert.Equal(2, db.SessionSegments.Count(s => s.SessionId == id));
    }

    [Fact]
    public void Transfer_ToOccupiedTable_IsRejected_AndLeavesStateIntact()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000, 24_000);
        var a = Start(env, tables[0], ownerId, shiftId);
        Start(env, tables[1], ownerId, shiftId);

        var transfer = env.Sessions.TransferSession(a, tables[1], ownerId, shiftId, "nope");

        Assert.True(transfer.Failed);
        Assert.Equal(DashboardStatus.InUse, StatusOf(env, tables[0])); // source unchanged
        Assert.Equal(DashboardStatus.InUse, StatusOf(env, tables[1])); // dest unchanged
    }

    [Fact]
    public void Transfer_BetweenDifferentRates_ChargesCorrectly()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000, 24_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        env.Clock.Advance(TimeSpan.FromMinutes(30));                 // 30 min @ 12000
        env.Sessions.TransferSession(id, tables[1], ownerId, shiftId, "moved");
        env.Clock.Advance(TimeSpan.FromMinutes(30));                 // 30 min @ 24000

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);
        Assert.Equal(18_000, finish.Value!.Charge.Paisa);           // 6000 + 12000
    }

    // ---------- Corrections ----------

    [Fact]
    public void Correction_RequiresPermission()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        var cashierId = CreateCashier(env);

        var result = env.Sessions.CorrectStartTime(id, env.Clock.UtcNow, "fixing", cashierId, shiftId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Correction_RequiresReason()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        var result = env.Sessions.CorrectStartTime(id, env.Clock.UtcNow, "   ", ownerId, shiftId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void Correction_PreservesOriginalValue_ForAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        var originalStart = env.Sessions.GetDashboard().First(c => c.TableId == tables[0]).Session!.StartUtc;

        var newStart = originalStart.AddMinutes(-10);
        Assert.True(env.Sessions.CorrectStartTime(id, newStart, "started earlier", ownerId, shiftId).Succeeded);

        using var db = env.NewContext();
        var adjustment = db.SessionAdjustments.Single(a => a.SessionId == id && a.Type == SessionAdjustmentType.StartTimeCorrection);
        Assert.Equal(originalStart.ToString("O"), adjustment.OldValue);
        Assert.Equal("started earlier", adjustment.Reason);
        Assert.Equal(ownerId, adjustment.ApprovedByUserId);
    }

    [Fact]
    public void Void_MarksSessionVoided_AndFreesTable()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);

        Assert.True(env.Sessions.VoidSession(id, "created by mistake", ownerId, shiftId).Succeeded);

        Assert.Equal(DashboardStatus.Available, StatusOf(env, tables[0]));
        using var db = env.NewContext();
        Assert.Equal(SessionStatus.Voided, db.TableSessions.Single(s => s.Id == id).Status);
    }

    [Fact]
    public void ChargeAdjustment_OnCompletedSession_UpdatesFrozenCharge()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(id, ownerId, shiftId, null); // charge 12000

        Assert.True(env.Sessions.AddChargeAdjustment(id, Money.FromPaisa(-2_000), "goodwill discount", ownerId, shiftId).Succeeded);

        Assert.Equal(10_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);
    }

    [Fact]
    public void History_ReturnsFinishedSessions_AwaitingCheckout()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = Start(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(30));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);

        var history = env.Sessions.GetHistory(new SessionHistoryFilter());
        var item = Assert.Single(history);
        Assert.Equal(CheckoutStatus.AwaitingCheckout, item.CheckoutStatus);
        Assert.Equal(SessionStatus.Completed, item.Status);
    }
}
