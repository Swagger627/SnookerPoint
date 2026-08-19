using SnookerPoint.App.Services;
using SnookerPoint.App.ViewModels.Dialogs;
using SnookerPoint.Application.Tables;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.App;

public class CorrectionDialogViewModelTests
{
    private static (Phase1Environment Env, SessionCorrectionContext Context, int SegmentId) FinishedSession()
    {
        var env = new Phase1Environment();
        var (ownerId, shiftId, tables) = env.SeedOwnerShiftAndTables(12_000);
        Assert.True(env.Sessions.StartSession(new StartSessionRequest(tables[0], ownerId, shiftId, null, null)).Succeeded);
        var id = env.Sessions.GetDashboard().First(c => c.TableId == tables[0]).Session!.SessionId;
        env.Clock.Advance(TimeSpan.FromMinutes(60));
        env.Sessions.FinishSession(id, ownerId, shiftId, null); // 12000 = Rs 120
        var context = env.Sessions.GetCorrectionContext(id)!;
        return (env, context, context.Segments.Single().SegmentId);
    }

    [Fact]
    public void RateCorrection_Workflow_ProducesRequest_AndPreviewsNewCharge()
    {
        var (env, context, segmentId) = FinishedSession();
        using var _ = env;

        var vm = new CorrectionDialogViewModel(context, env.Calculator);
        vm.IsRate = true;
        vm.SelectedSegment = vm.Segments.Single();
        vm.NewRateRupees = "240";
        vm.Reason = "wrong rate was applied";

        Assert.Equal("Rs 120", vm.OldChargeText);
        Assert.Equal("Rs 240", vm.NewChargeText); // 60 min at Rs 240/hr

        Assert.True(vm.TryConfirm());
        Assert.NotNull(vm.Result);
        Assert.Equal(CorrectionKind.SegmentRate, vm.Result!.Kind);
        Assert.Equal(segmentId, vm.Result.TargetId);
        Assert.Equal(24_000, vm.Result.NewAmount.Paisa);
    }

    [Fact]
    public void StartTimeCorrection_Workflow_ProducesShiftedTimestamp()
    {
        var (env, context, _) = FinishedSession();
        using var _ = env;

        var vm = new CorrectionDialogViewModel(context, env.Calculator);
        // Default kind is start time.
        vm.StartShiftMinutes = "-10";
        vm.Reason = "started earlier";

        Assert.True(vm.TryConfirm());
        Assert.Equal(CorrectionKind.StartTime, vm.Result!.Kind);
        Assert.Equal(context.StartUtc.AddMinutes(-10), vm.Result.NewTimestamp);
    }

    [Fact]
    public void Reason_IsRequired()
    {
        var (env, context, _) = FinishedSession();
        using var _ = env;

        var vm = new CorrectionDialogViewModel(context, env.Calculator);
        vm.IsVoid = true;
        vm.Reason = "   ";

        Assert.False(vm.TryConfirm());
        Assert.NotNull(vm.ErrorMessage);
    }

    [Fact]
    public void ChoosingRate_MakesOtherTypesFalse()
    {
        var (env, context, _) = FinishedSession();
        using var _ = env;

        var vm = new CorrectionDialogViewModel(context, env.Calculator);
        vm.IsRate = true;

        Assert.False(vm.IsStartTime);
        Assert.False(vm.IsVoid);
        Assert.True(vm.IsRate);
    }
}
