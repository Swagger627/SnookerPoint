using SnookerPoint.App.Theming;

namespace SnookerPoint.Tests.TestSupport;

/// <summary>A no-op theme service for headless view-model tests.</summary>
public sealed class FakeThemeService : IThemeService
{
    public ThemeMode Current { get; private set; } = ThemeMode.Dark;

    public void Apply(ThemeMode mode) => Current = mode;

    public void Toggle() => Current = Current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark;
}
