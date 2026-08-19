using System.Windows;

namespace SnookerPoint.App.Theming;

/// <summary>
/// Default <see cref="IThemeService"/> that merges/unmerges a theme token
/// dictionary on the live application resources.
/// </summary>
public sealed class ThemeService : IThemeService
{
    // Fully qualified: the SnookerPoint.Application namespace otherwise shadows
    // the bare name "Application".
    private readonly System.Windows.Application _app;
    private ResourceDictionary? _currentDictionary;

    public ThemeService(System.Windows.Application app)
    {
        _app = app;
    }

    public ThemeMode Current { get; private set; } = ThemeMode.Dark;

    public void Apply(ThemeMode mode)
    {
        var uri = new Uri($"pack://application:,,,/Themes/{mode}.xaml", UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = uri };

        if (_currentDictionary is not null)
        {
            _app.Resources.MergedDictionaries.Remove(_currentDictionary);
        }

        _app.Resources.MergedDictionaries.Add(dictionary);
        _currentDictionary = dictionary;
        Current = mode;
    }

    public void Toggle() => Apply(Current == ThemeMode.Dark ? ThemeMode.Light : ThemeMode.Dark);
}
