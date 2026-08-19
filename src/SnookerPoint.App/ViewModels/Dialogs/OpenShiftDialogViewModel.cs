using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the Open Shift dialog (opening cash + optional note).</summary>
public partial class OpenShiftDialogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _openingCashText = "0";

    [ObservableProperty]
    private string _note = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public OpenShiftInput? Result { get; private set; }

    public bool TryConfirm()
    {
        if (!MoneyInput.TryParseRupees(OpeningCashText, out Money cash))
        {
            ErrorMessage = "Please enter a valid opening amount (0 or more).";
            return false;
        }

        Result = new OpenShiftInput(cash, string.IsNullOrWhiteSpace(Note) ? null : Note.Trim());
        return true;
    }
}
