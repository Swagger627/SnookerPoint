using System.Windows;
using SnookerPoint.App.ViewModels;

namespace SnookerPoint.App.Views;

/// <summary>The application shell window. Hosts the active screen view model.</summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shell)
    {
        InitializeComponent();
        DataContext = shell;
    }
}
