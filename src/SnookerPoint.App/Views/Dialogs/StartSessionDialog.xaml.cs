using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class StartSessionDialog : Window
{
    public StartSessionDialog(StartSessionDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => LabelBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is StartSessionDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
