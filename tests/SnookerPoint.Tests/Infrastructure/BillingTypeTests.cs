using Microsoft.Extensions.Logging.Abstractions;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Services;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

/// <summary>
/// Section A/B/C billing tests: Hourly stays as before; a table's hourly rate can be
/// changed without touching other tables or running sessions; a Fixed-amount session's
/// charge is frozen and never moves with elapsed time, pauses, restart or transfer; and
/// authorised users can correct the fixed amount / billing type with a full audit trail.
/// </summary>
public class BillingTypeTests
{
    private static int StartHourly(Phase1Environment env, int tableId, int ownerId, int shiftId)
    {
        var result = env.Sessions.StartSession(new StartSessionRequest(tableId, ownerId, shiftId, null, null));
        Assert.True(result.Succeeded, result.ErrorMessage);
        return env.Sessions.GetDashboard().First(c => c.TableId == tableId).Session!.SessionId;
    }

    private static int StartFixed(Phase1Environment env, int tableId, int ownerId, int shiftId, long fixedPaisa)
    {
        var result = env.Sessions.StartSession(new StartSessionRequest(
            tableId, ownerId, shiftId, null, null, BillingType.Fixed, Money.FromPaisa(fixedPaisa)));
        Assert.True(result.Succeeded, result.ErrorMessage);
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

    // ---------- Hourly regression ----------

    [Fact]
    public void HourlySession_ChargesFromSnapshotRate_Unchanged()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);
        Assert.True(finish.Succeeded, finish.ErrorMessage);
        Assert.Equal(BillingType.Hourly, finish.Value!.BillingType);
        Assert.Equal(12_000, finish.Value.Charge.Paisa);
    }

    [Fact]
    public void NewSession_UsesUpdatedTableRate_AfterAChange()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        var drafts = env.TableManagement.GetAll()
            .Select(i => new TableDraft(i.Id, i.Name, i.Type, i.HourlyRate, i.IsActive)).ToList();
        drafts[0] = drafts[0] with { HourlyRate = Money.FromPaisa(30_000) };
        Assert.True(env.TableManagement.SaveLayout(drafts, ownerId).Succeeded);

        var id = StartHourly(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);

        Assert.Equal(30_000, finish.Value!.Charge.Paisa); // the new session picks up the new rate
    }

    [Fact]
    public void ChangingOneTablesRate_DoesNotAffectOthers()
    {
        using var env = new Phase1Environment();
        var (ownerId, _, tables) = env.SeedOwnerShiftAndTables(12_000, 24_000);

        var drafts = env.TableManagement.GetAll()
            .Select(i => new TableDraft(i.Id, i.Name, i.Type, i.HourlyRate, i.IsActive)).ToList();
        var firstIndex = drafts.FindIndex(d => d.Id == tables[0]);
        drafts[firstIndex] = drafts[firstIndex] with { HourlyRate = Money.FromPaisa(50_000) };
        Assert.True(env.TableManagement.SaveLayout(drafts, ownerId).Succeeded);

        using var db = env.NewContext();
        Assert.Equal(50_000, db.PoolTables.Single(t => t.Id == tables[0]).HourlyRate.Paisa);
        Assert.Equal(24_000, db.PoolTables.Single(t => t.Id == tables[1]).HourlyRate.Paisa); // untouched
    }

    // ---------- Fixed amount frozen ----------

    [Fact]
    public void FixedAmount_DoesNotChangeAsTimePasses()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 50_000);

        env.Clock.Advance(TimeSpan.FromMinutes(200)); // a long time on a live fixed session
        var card = env.Sessions.GetDashboard().First(c => c.TableId == tables[0]);
        Assert.Equal(BillingType.Fixed, card.Session!.BillingType);
        Assert.Equal(50_000, card.Session.FixedAmount!.Value.Paisa);

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);
        Assert.Equal(50_000, finish.Value!.Charge.Paisa); // elapsed time never altered the charge
    }

    [Fact]
    public void FixedAmount_SurvivesPauseAndResume()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 40_000);

        env.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(env.Sessions.PauseSession(id, ownerId, shiftId).Succeeded);
        env.Clock.Advance(TimeSpan.FromMinutes(45));
        Assert.True(env.Sessions.ResumeSession(id, ownerId, shiftId).Succeeded);
        env.Clock.Advance(TimeSpan.FromMinutes(30));

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);
        Assert.Equal(40_000, finish.Value!.Charge.Paisa);
    }

    [Fact]
    public void FixedAmount_SurvivesApplicationRestart()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 35_000);
        env.Clock.Advance(TimeSpan.FromMinutes(20));

        // A fresh service reads purely from SQLite, as after a crash/restart.
        var restarted = new TableSessionService(env.Factory, env.Calculator, new PermissionService(),
            env.Clock, NullLogger<TableSessionService>.Instance);

        var card = restarted.GetDashboard().First(c => c.TableId == tables[0]);
        Assert.Equal(BillingType.Fixed, card.Session!.BillingType);
        Assert.Equal(35_000, card.Session.FixedAmount!.Value.Paisa);
    }

    [Fact]
    public void FixedAmount_SurvivesTableTransfer()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000, 24_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 45_000);

        env.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.True(env.Sessions.TransferSession(id, tables[1], ownerId, shiftId, "customer moved").Succeeded);
        env.Clock.Advance(TimeSpan.FromMinutes(30));

        var card = env.Sessions.GetDashboard().First(c => c.TableId == tables[1]);
        Assert.Equal(BillingType.Fixed, card.Session!.BillingType);
        Assert.Equal(45_000, card.Session.FixedAmount!.Value.Paisa);

        var finish = env.Sessions.FinishSession(id, ownerId, shiftId, null);
        Assert.Equal(45_000, finish.Value!.Charge.Paisa); // the segment rates on transfer never apply
    }

    [Fact]
    public void FixedSession_RemainsAwaitingCheckout_AfterFinishing()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 30_000);
        env.Clock.Advance(TimeSpan.FromMinutes(15));

        Assert.True(env.Sessions.FinishSession(id, ownerId, shiftId, null).Succeeded);

        using var db = env.NewContext();
        var session = db.TableSessions.Single(s => s.Id == id);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.Equal(CheckoutStatus.AwaitingCheckout, session.CheckoutStatus);
    }

    [Fact]
    public void NegativeFixedAmount_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.Sessions.StartSession(new StartSessionRequest(
            tables[0], ownerId, shiftId, null, null, BillingType.Fixed, Money.FromPaisa(-1)));

        Assert.True(result.Failed);
    }

    [Fact]
    public void FixedBilling_RequiresAnAmount()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);

        var result = env.Sessions.StartSession(new StartSessionRequest(
            tables[0], ownerId, shiftId, null, null, BillingType.Fixed, null));

        Assert.True(result.Failed);
    }

    // ---------- Controlled billing corrections ----------

    [Fact]
    public void AuthorisedUser_CanCorrectHourlyRate()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);

        var segment = env.Sessions.GetCorrectionContext(id)!.Segments.Single();
        var result = env.Sessions.CorrectSegmentRate(segment.SegmentId, Money.FromPaisa(24_000), "wrong rate", ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(24_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);
    }

    [Fact]
    public void AuthorisedUser_CanCorrectFixedAmount()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 30_000);
        env.Clock.Advance(TimeSpan.FromMinutes(20));
        env.Sessions.FinishSession(id, ownerId, shiftId, null);

        var result = env.Sessions.CorrectFixedAmount(id, Money.FromPaisa(45_000), "agreed higher price", ownerId, shiftId);

        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(45_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);
    }

    [Fact]
    public void CorrectFixedAmount_OnHourlySession_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);

        var result = env.Sessions.CorrectFixedAmount(id, Money.FromPaisa(10_000), "nope", ownerId, shiftId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void SwitchHourlyToFixed_ThenBack_RecalculatesCharge()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(id, ownerId, shiftId, null); // hourly charge 12000

        // Switch to a fixed charge of Rs 200.
        Assert.True(env.Sessions.CorrectBillingType(id, BillingType.Fixed, Money.FromPaisa(20_000), "agreed flat rate", ownerId, shiftId).Succeeded);
        Assert.Equal(20_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);

        // Switch back to hourly: the snapshotted segment rate charge returns.
        Assert.True(env.Sessions.CorrectBillingType(id, BillingType.Hourly, null, "back to hourly", ownerId, shiftId).Succeeded);
        Assert.Equal(12_000, env.Sessions.GetSessionSummary(id)!.Charge.Paisa);
    }

    [Fact]
    public void SwitchToFixed_WithoutAmount_IsRejected()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);

        var result = env.Sessions.CorrectBillingType(id, BillingType.Fixed, null, "missing amount", ownerId, shiftId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void BillingTypeCorrection_RequiresReason()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);

        var result = env.Sessions.CorrectBillingType(id, BillingType.Fixed, Money.FromPaisa(10_000), "   ", ownerId, shiftId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void UnauthorisedUser_CannotCorrectFixedAmount()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 30_000);
        var cashier = CreateCashier(env);

        var result = env.Sessions.CorrectFixedAmount(id, Money.FromPaisa(99_000), "no permission", cashier, shiftId);
        Assert.True(result.Failed);
        Assert.Equal(30_000, env.Sessions.GetDashboard().First(c => c.TableId == tables[0]).Session!.FixedAmount!.Value.Paisa);
    }

    [Fact]
    public void UnauthorisedUser_CannotChangeBillingType()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);
        var cashier = CreateCashier(env);

        var result = env.Sessions.CorrectBillingType(id, BillingType.Fixed, Money.FromPaisa(20_000), "no permission", cashier, shiftId);
        Assert.True(result.Failed);
    }

    [Fact]
    public void FixedAmountCorrection_PreservesOriginalValue_ForAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartFixed(env, tables[0], ownerId, shiftId, 30_000);
        env.Sessions.CorrectFixedAmount(id, Money.FromPaisa(45_000), "agreed higher price", ownerId, shiftId);

        using var db = env.NewContext();
        var adjustment = db.SessionAdjustments.Single(a => a.SessionId == id && a.Type == SessionAdjustmentType.FixedAmountCorrection);
        Assert.Equal(Money.FromPaisa(30_000).Format(), adjustment.OldValue);   // original preserved
        Assert.Equal(Money.FromPaisa(45_000).Format(), adjustment.NewValue);
        Assert.Equal("agreed higher price", adjustment.Reason);
        Assert.Equal(ownerId, adjustment.ApprovedByUserId);
    }

    [Fact]
    public void BillingTypeCorrection_PreservesOriginalBillingType_ForAudit()
    {
        using var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        var id = StartHourly(env, tables[0], ownerId, shiftId);
        env.Sessions.CorrectBillingType(id, BillingType.Fixed, Money.FromPaisa(20_000), "agreed flat rate", ownerId, shiftId);

        using var db = env.NewContext();
        var adjustment = db.SessionAdjustments.Single(a => a.SessionId == id && a.Type == SessionAdjustmentType.BillingTypeCorrection);
        Assert.Equal("Hourly", adjustment.OldValue);                            // original preserved
        Assert.Contains("Fixed", adjustment.NewValue);
        Assert.Equal("agreed flat rate", adjustment.Reason);
    }
}
