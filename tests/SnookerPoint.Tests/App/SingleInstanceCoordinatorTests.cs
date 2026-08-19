using SnookerPoint.App.Services;

namespace SnookerPoint.Tests.App;

public class SingleInstanceCoordinatorTests
{
    // A named mutex is re-entrant for the OWNING thread, so a genuine "second instance" is modelled
    // by acquiring from a separate thread.
    private static bool AcquireOnOtherThread(SingleInstanceCoordinator coordinator)
    {
        var result = false;
        var t = new Thread(() => result = coordinator.TryAcquire());
        t.Start();
        t.Join();
        return result;
    }

    [Fact]
    public void SecondInstance_CannotAcquire_WhileFirstHoldsIt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceCoordinator(suffix);
        using var second = new SingleInstanceCoordinator(suffix);

        Assert.True(first.TryAcquire());
        Assert.False(AcquireOnOtherThread(second)); // blocked while first owns the lock
    }

    [Fact]
    public void AfterRelease_SecondInstanceCanAcquire_SupportingRestoreRestart()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceCoordinator(suffix);
        using var second = new SingleInstanceCoordinator(suffix);

        Assert.True(first.TryAcquire());
        first.Release(); // the restore-restart releases before relaunching
        Assert.True(AcquireOnOtherThread(second));
    }

    [Fact]
    public void SignalExistingInstance_RaisesActivationOnPrimary()
    {
        var suffix = Guid.NewGuid().ToString("N");
        using var primary = new SingleInstanceCoordinator(suffix);
        Assert.True(primary.TryAcquire());

        using var activated = new ManualResetEventSlim(false);
        primary.ActivationRequested += () => activated.Set();

        using var second = new SingleInstanceCoordinator(suffix);
        second.SignalExistingInstance();

        Assert.True(activated.Wait(TimeSpan.FromSeconds(3))); // primary was asked to come forward
    }
}
