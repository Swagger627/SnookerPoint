using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;
using SnookerPoint.App.ViewModels;

namespace SnookerPoint.App.Views;

/// <summary>
/// Renders a generic <see cref="ReportTable"/> in a DataGrid, rebuilding columns whenever the
/// view-model's Table changes (columns bind to the string-array indexer).
/// </summary>
public partial class ReportsView : UserControl
{
    private ReportsViewModel? _vm;

    public ReportsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as ReportsViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            Rebuild();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.Table))
        {
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Grid.Columns.Clear();
        var table = _vm?.Table;
        if (table is null)
        {
            Grid.ItemsSource = null;
            return;
        }

        for (var i = 0; i < table.Columns.Count; i++)
        {
            Grid.Columns.Add(new DataGridTextColumn
            {
                Header = table.Columns[i],
                Binding = new Binding($"[{i}]"),
                Width = i == 0 ? new DataGridLength(1, DataGridLengthUnitType.Auto) : new DataGridLength(1, DataGridLengthUnitType.SizeToCells),
            });
        }

        Grid.ItemsSource = table.Rows;
    }
}
