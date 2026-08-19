using System.Windows;
using SnookerPoint.App.ViewModels.Dialogs;

namespace SnookerPoint.App.Views.Dialogs;

public partial class StockHistoryDialog : Window
{
    public StockHistoryDialog(StockHistoryDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
