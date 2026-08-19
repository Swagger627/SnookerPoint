namespace SnookerPoint.App.Licensing;

/// <summary>
/// Compile-time licensing profile. The development public-key override is only honoured when the
/// <c>DEV_LICENSING</c> symbol is defined (Debug/Development builds). Pilot and Customer Release
/// builds compile <see cref="AllowDevOverride"/> to <c>false</c>, so they ignore and reject
/// development override files, environment keys, scratchpad keys and unsigned licences.
/// </summary>
public static class LicenseProfile
{
#if DEV_LICENSING
    public const bool AllowDevOverride = true;
    public const string Name = "Development";
#else
    public const bool AllowDevOverride = false;
    public const string Name = "Release";
#endif
}
