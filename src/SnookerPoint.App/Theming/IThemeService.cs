namespace SnookerPoint.App.Theming;

/// <summary>
/// Swaps the active colour-token resource dictionary at runtime. Screens reference
/// only the semantic token keys (e.g. <c>Brush.Surface</c>), so a theme change
/// requires no screen changes — matching the branding approach in §23.
/// </summary>
public interface IThemeService
{
    ThemeMode Current { get; }

    /// <summary>Applies the given theme's token dictionary to the application.</summary>
    void Apply(ThemeMode mode);

    /// <summary>Toggles between Dark and Light.</summary>
    void Toggle();
}
