using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class ShiftServiceTests
{
    private static (Phase1Environment Env, int OwnerId) SetUp()
    {
        var env = new Phase1Environment();
        Assert.True(env.Setup.CompleteSetup(SetupRequests.Valid()).Succeeded);
        using var db = env.NewContext();
        var ownerId = db.Users.Single().Id;
        return (env, ownerId);
    }

    [Fact]
    public void OpenShift_Succeeds_AndBecomesCurrent()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var result = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), "Morning float");

        Assert.True(result.Succeeded, result.ErrorMessage);
        var current = env.Shifts.GetCurrentShift(ownerId);
        Assert.NotNull(current);
        Assert.Equal(Money.FromRupees(1000L), current!.OpeningCash);
    }

    [Fact]
    public void OpenShift_Twice_IsRejected()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        Assert.True(env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null).Succeeded);
        var second = env.Shifts.OpenShift(ownerId, Money.FromRupees(500L), null);

        Assert.True(second.Failed);
    }

    [Fact]
    public void ExpectedCash_UsesPhase1Formula()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var open = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null);
        var shiftId = open.Value!.ShiftId;

        env.Shifts.RecordCashMovement(shiftId, CashMovementType.CashIn, Money.FromRupees(500L), "Top up", ownerId);
        env.Shifts.RecordCashMovement(shiftId, CashMovementType.CashOut, Money.FromRupees(200L), "Change", ownerId);
        env.Shifts.RecordCashMovement(shiftId, CashMovementType.Expense, Money.FromRupees(100L), "Tea", ownerId);
        env.Shifts.RecordCashMovement(shiftId, CashMovementType.Drop, Money.FromRupees(300L), "Safe", ownerId);

        var current = env.Shifts.GetCurrentShift(ownerId);

        // 1000 + 500 - 200 - 100 - 300 = 900
        Assert.Equal(Money.FromRupees(900L), current!.ExpectedCash);
        Assert.Equal(Money.FromRupees(500L), current.CashInTotal);
        Assert.Equal(Money.FromRupees(300L), current.DropTotal);
    }

    [Fact]
    public void CloseShift_ComputesVariance_AndFreezesValues()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var open = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null);
        var shiftId = open.Value!.ShiftId;
        env.Shifts.RecordCashMovement(shiftId, CashMovementType.CashIn, Money.FromRupees(500L), "Top up", ownerId);

        // Expected = 1500. Counted = 1450 → variance -50.
        var close = env.Shifts.CloseShift(shiftId, Money.FromRupees(1450L), "End of day");

        Assert.True(close.Succeeded, close.ErrorMessage);
        Assert.Equal(Money.FromRupees(1500L), close.Value!.ExpectedCash);
        Assert.Equal(Money.FromRupees(-50L), close.Value.Variance);

        using var db = env.NewContext();
        var shift = db.Shifts.Single(s => s.Id == shiftId);
        Assert.Equal(ShiftStatus.Closed, shift.Status);
        Assert.Equal(Money.FromRupees(1500L), shift.ExpectedCash);
        Assert.Equal(Money.FromRupees(-50L), shift.Variance);
    }

    [Fact]
    public void ClosedShift_CannotBeClosedAgain_OrTakeMovements()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var open = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null);
        var shiftId = open.Value!.ShiftId;
        env.Shifts.CloseShift(shiftId, Money.FromRupees(1000L), null);

        Assert.True(env.Shifts.CloseShift(shiftId, Money.FromRupees(1000L), null).Failed);
        Assert.True(env.Shifts.RecordCashMovement(shiftId, CashMovementType.CashIn, Money.FromRupees(10L), "x", ownerId).Failed);
        Assert.Null(env.Shifts.GetCurrentShift(ownerId)); // no open shift after close
    }

    [Fact]
    public void CashMovements_AreAppendOnly_AndListed()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var open = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null);
        var shiftId = open.Value!.ShiftId;

        env.Shifts.RecordCashMovement(shiftId, CashMovementType.CashIn, Money.FromRupees(100L), "One", ownerId);
        env.Shifts.RecordCashMovement(shiftId, CashMovementType.Expense, Money.FromRupees(40L), "Two", ownerId);

        var lines = env.Shifts.GetCashMovements(shiftId);
        Assert.Equal(2, lines.Count);

        using var db = env.NewContext();
        Assert.Equal(2, db.CashMovements.Count(m => m.ShiftId == shiftId));
    }

    [Fact]
    public void RecordCashMovement_RejectsZeroAmount()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var open = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null);
        var shiftId = open.Value!.ShiftId;

        var result = env.Shifts.RecordCashMovement(shiftId, CashMovementType.CashIn, Money.Zero, "Nothing", ownerId);

        Assert.True(result.Failed);
    }

    [Fact]
    public void OpenShift_WritesAuditEvents()
    {
        var (env, ownerId) = SetUp();
        using var _ = env;

        var open = env.Shifts.OpenShift(ownerId, Money.FromRupees(1000L), null);
        env.Shifts.CloseShift(open.Value!.ShiftId, Money.FromRupees(1000L), null);

        var actions = env.Audit.GetRecent(50).Select(a => a.Action).ToList();
        Assert.Contains("ShiftOpened", actions);
        Assert.Contains("ShiftClosed", actions);
    }
}
