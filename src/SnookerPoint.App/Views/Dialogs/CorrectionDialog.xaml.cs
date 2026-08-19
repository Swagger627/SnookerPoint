using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class CorrectionDialog : Window
{
    public CorrectionDialog(CorrectionDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is CorrectionDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
