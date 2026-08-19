using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class StaffEditDialog : Window
{
    public StaffEditDialog(StaffEditDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => NameBox.Focus();
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is StaffEditDialogViewModel vm && vm.TryConfirm())
        {
            DialogResult = true;
        }
    }
}
