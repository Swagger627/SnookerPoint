using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class FinishDialog : Window
{
    public FinishDialog(FinishDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is FinishDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
