namespace SnookerPoint.Tests.App;

/// <summary>
/// Groups the WPF smoke tests into one non-parallel collection. WPF's <c>Application</c>
/// and related statics are process-global and cannot be touched by concurrent STA threads,
/// so these classes must not run in parallel with each other.
/// </summary>
[CollectionDefinition("WpfSmoke", DisableParallelization = true)]
public sealed class WpfSmokeCollection
{
}
