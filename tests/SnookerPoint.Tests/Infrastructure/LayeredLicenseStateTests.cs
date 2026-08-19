using SnookerPoint.App.Licensing;
using SnookerPoint.Licensing;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class LayeredLicenseStateTests
{
    private static LicenseState TrialAt(DateTimeOffset start) =>
        new() { TrialStartUtc = start, LastSeenUtc = start, LastRunUtc = start };

    [Fact]
    public void Save_FansOutToBothCopies()
    {
        var user = new InMemoryLicenseStateStore();
        var machine = new InMemoryLicenseStateStore();
        var layered = new LayeredLicenseStateStore(user, machine);

        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(layered.Save(TrialAt(start)));

        Assert.Equal(start, user.State!.TrialStartUtc);
        Assert.Equal(start, machine.State!.TrialStartUtc);
    }

    [Fact]
    public void DeletingPerUserCopy_DoesNotRestartTrial_MachineCopyRemains()
    {
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var machine = new InMemoryLicenseStateStore { State = TrialAt(start) };
        var user = new InMemoryLicenseStateStore(); // per-user copy "deleted"/empty
        var layered = new LayeredLicenseStateStore(user, machine);

        var loaded = layered.Load(out var corrupt);
        Assert.False(corrupt);
        Assert.Equal(start, loaded!.TrialStartUtc); // machine copy preserves the trial
    }

    [Fact]
    public void SwitchingWindowsUser_SeesSharedMachineTrial_NotAFreshOne()
    {
        // The machine (shared) copy holds the original start; a new user's per-user copy is empty.
        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var machine = new InMemoryLicenseStateStore { State = TrialAt(start) };
        var newUser = new InMemoryLicenseStateStore();
        var layered = new LayeredLicenseStateStore(newUser, machine);

        Assert.Equal(start, layered.Load(out _)!.TrialStartUtc);
    }

    [Fact]
    public void EarliestStart_Wins_AcrossCopies()
    {
        var early = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var late = early.AddDays(5);
        var user = new InMemoryLicenseStateStore { State = TrialAt(late) };
        var machine = new InMemoryLicenseStateStore { State = TrialAt(early) };
        var layered = new LayeredLicenseStateStore(user, machine);

        Assert.Equal(early, layered.Load(out _)!.TrialStartUtc); // never benefits from the later start
    }

    [Fact]
    public void BothMissing_ReturnsNull_AllowingAFreshTrial()
    {
        var layered = new LayeredLicenseStateStore(new InMemoryLicenseStateStore(), new InMemoryLicenseStateStore());
        Assert.Null(layered.Load(out var corrupt));
        Assert.False(corrupt);
    }

    [Fact]
    public void CorruptCopy_WithNoUsableStart_ReportsCorrupt_NotFresh()
    {
        var user = new InMemoryLicenseStateStore { Corrupt = true };
        var machine = new InMemoryLicenseStateStore(); // empty
        var layered = new LayeredLicenseStateStore(user, machine);

        layered.Load(out var corrupt);
        Assert.True(corrupt); // a warning state, not a silent fresh trial
    }

    [Fact]
    public void Licence_ReadFromEitherCopy_RemainsAuthoritative()
    {
        var user = new InMemoryLicenseStateStore();          // no per-user licence
        var machine = new InMemoryLicenseStateStore { License = "MACHINE-LICENCE" };
        var layered = new LayeredLicenseStateStore(user, machine);

        Assert.Equal("MACHINE-LICENCE", layered.LoadLicense());
    }

    [Fact]
    public void SaveLicence_MirrorsToBothCopies()
    {
        var user = new InMemoryLicenseStateStore();
        var machine = new InMemoryLicenseStateStore();
        var layered = new LayeredLicenseStateStore(user, machine);

        Assert.True(layered.SaveLicense("LICENCE-TEXT"));
        Assert.Equal("LICENCE-TEXT", user.License);
        Assert.Equal("LICENCE-TEXT", machine.License);
    }

    [Fact]
    public void MachineCopyUnavailable_DegradesToPerUserOnly()
    {
        var user = new InMemoryLicenseStateStore();
        var layered = new LayeredLicenseStateStore(user, new NoOpLicenseStateStore());

        var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        Assert.True(layered.Save(TrialAt(start)));      // per-user save still succeeds
        Assert.Equal(start, layered.Load(out _)!.TrialStartUtc);
    }
}
