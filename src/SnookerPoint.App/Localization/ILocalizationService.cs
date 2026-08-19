using System.Globalization;
using System.Windows;

namespace SnookerPoint.App.Localization;

/// <summary>
/// Controls the active UI culture and the resulting layout direction. English is
/// the default; Urdu (RTL) is supported so the UI flips to right-to-left without a
/// redesign (§23). Screens bind their <c>FlowDirection</c> to <see cref="FlowDirection"/>.
/// </summary>
public interface ILocalizationService
{
    CultureInfo Current { get; }

    /// <summary>Layout direction implied by the current culture (LTR / RTL).</summary>
    FlowDirection FlowDirection { get; }

    /// <summary>Raised after the culture changes so views can refresh their text.</summary>
    event EventHandler? CultureChanged;

    /// <summary>Sets the active culture by name (e.g. "en", "ur").</summary>
    void SetCulture(string cultureName);

    /// <summary>Toggles between English and Urdu.</summary>
    void Toggle();
}
