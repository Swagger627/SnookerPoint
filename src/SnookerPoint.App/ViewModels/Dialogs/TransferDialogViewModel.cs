using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the Transfer Session dialog.</summary>
public partial class TransferDialogViewModel : ObservableObject
{
    public TransferDialogViewModel(string sourceTableName, IReadOnlyList<TransferDestination> destinations)
    {
        SourceTableName = sourceTableName;
        Destinations = destinations;
        _selectedDestination = destinations.Count > 0 ? destinations[0] : null;
    }

    public string SourceTableName { get; }
    public IReadOnlyList<TransferDestination> Destinations { get; }

    [ObservableProperty] private TransferDestination? _selectedDestination;
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    public string DestinationRateText =>
        SelectedDestination is null ? "—" : $"{SelectedDestination.HourlyRate.Format()}/hr";

    partial void OnSelectedDestinationChanged(TransferDestination? value) =>
        OnPropertyChanged(nameof(DestinationRateText));

    public bool HasDestinations => Destinations.Count > 0;

    public TransferInput? Result { get; private set; }

    public bool TryConfirm()
    {
        if (SelectedDestination is null)
        {
            ErrorMessage = "Please choose a destination table.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Please enter a reason for the transfer.";
            return false;
        }

        Result = new TransferInput(SelectedDestination.TableId, Reason.Trim());
        return true;
    }
}
