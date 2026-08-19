using System.Globalization;
using System.Threading;
using System.Windows;

namespace SnookerPoint.App.Localization;

/// <summary>Default <see cref="ILocalizationService"/> backed by thread cultures.</summary>
public sealed class LocalizationService : ILocalizationService
{
    private const string English = "en";
    private const string Urdu = "ur";

    public CultureInfo Current { get; private set; } = CultureInfo.GetCultureInfo(English);

    public FlowDirection FlowDirection =>
        Current.TextInfo.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public event EventHandler? CultureChanged;

    public void SetCulture(string cultureName)
    {
        var culture = CultureInfo.GetCultureInfo(cultureName);
        Current = culture;

        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Toggle() =>
        SetCulture(Current.TwoLetterISOLanguageName == English ? Urdu : English);
}
