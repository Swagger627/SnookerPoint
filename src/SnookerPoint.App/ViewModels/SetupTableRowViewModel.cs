using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.ViewModels;

/// <summary>One editable table row in the setup wizard's Table Setup step.</summary>
public partial class SetupTableRowViewModel : ObservableObject
{
    public SetupTableRowViewModel(string name, TableType type, string rateText, bool isActive)
    {
        _name = name;
        _type = type;
        _rateText = rateText;
        _isActive = isActive;
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private TableType _type;

    /// <summary>Hourly rate in rupees, as typed.</summary>
    [ObservableProperty]
    private string _rateText;

    [ObservableProperty]
    private bool _isActive;

    public IReadOnlyList<TableType> TableTypes { get; } = Enum.GetValues<TableType>();
}
