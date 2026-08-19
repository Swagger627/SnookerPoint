using System.Globalization;
using System.Resources;

namespace SnookerPoint.App.Localization;

/// <summary>
/// Strongly-typed access to the localised UI strings in
/// <c>Resources/Strings.resx</c> (English) and its satellites (e.g.
/// <c>Strings.ur.resx</c> for Urdu). Values are resolved against the current UI
/// culture, so switching culture switches the returned text.
/// </summary>
/// <remarks>
/// Hand-written (rather than VS designer-generated) so it builds identically from
/// the CLI. New keys are added here alongside the .resx entries.
/// </remarks>
public static class Strings
{
    private static readonly ResourceManager Manager =
        new("SnookerPoint.App.Resources.Strings", typeof(Strings).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    public static string AppName => Get(nameof(AppName));
    public static string Welcome_Heading => Get(nameof(Welcome_Heading));
    public static string Welcome_Body => Get(nameof(Welcome_Body));
    public static string Foundation_Status => Get(nameof(Foundation_Status));
    public static string Status_DatabaseReady => Get(nameof(Status_DatabaseReady));
    public static string Action_ToggleTheme => Get(nameof(Action_ToggleTheme));
    public static string Action_ToggleLanguage => Get(nameof(Action_ToggleLanguage));
}
