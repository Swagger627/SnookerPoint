using CommunityToolkit.Mvvm.ComponentModel;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// The root view model for the shell window. Hosts whichever screen view model is
/// currently active; the window renders it through data templates.
/// </summary>
public partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _current;
}
