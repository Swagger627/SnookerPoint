using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class CsvImportDialog : Window
{
    public CsvImportDialog(CsvImportDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (DataContext is CsvImportDialogViewModel vm && vm.Confirm())
        {
            DialogResult = true;
        }
    }
}
