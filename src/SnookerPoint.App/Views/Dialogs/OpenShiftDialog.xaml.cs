using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class OpenShiftDialog : Window
{
    public OpenShiftDialog(OpenShiftDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => AmountBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is OpenShiftDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
