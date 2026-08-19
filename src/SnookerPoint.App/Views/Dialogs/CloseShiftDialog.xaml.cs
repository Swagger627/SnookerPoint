using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class CloseShiftDialog : Window
{
    public CloseShiftDialog(CloseShiftDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => CountedBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is CloseShiftDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
