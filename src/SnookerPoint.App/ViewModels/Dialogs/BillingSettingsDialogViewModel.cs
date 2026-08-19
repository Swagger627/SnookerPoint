using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Settings;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>Backs the Billing Settings dialog.</summary>
public partial class BillingSettingsDialogViewModel : ObservableObject
{
    public BillingSettingsDialogViewModel(BillingSettingsView current)
    {
        _isRoundUp = current.Method == BillingMethod.RoundUp;
        _isExact = !_isRoundUp;
        _incrementText = current.RoundingIncrementMinutes.ToString();
        _minimumText = current.MinimumBillableMinutes.ToString();
        _graceText = current.GracePeriodMinutes.ToString();
    }

    [ObservableProperty] private bool _isExact;
    [ObservableProperty] private bool _isRoundUp;

    partial void OnIsExactChanged(bool value)
    {
        if (value && IsRoundUp) { IsRoundUp = false; }
    }

    partial void OnIsRoundUpChanged(bool value)
    {
        if (value && IsExact) { IsExact = false; }
    }

    [ObservableProperty] private string _incrementText;
    [ObservableProperty] private string _minimumText;
    [ObservableProperty] private string _graceText;
    [ObservableProperty] private string? _errorMessage;

    public BillingSettingsInput? Result { get; private set; }

    public bool TryConfirm()
    {
        var method = IsRoundUp ? BillingMethod.RoundUp : BillingMethod.Exact;

        if (!int.TryParse(IncrementText, out var increment) ||
            !int.TryParse(MinimumText, out var minimum) ||
            !int.TryParse(GraceText, out var grace))
        {
            ErrorMessage = "Please enter whole numbers of minutes.";
            return false;
        }

        var errors = BillingPolicy.Validate(method, increment, minimum, grace);
        if (errors.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, errors);
            return false;
        }

        Result = new BillingSettingsInput(method, increment, minimum, grace);
        return true;
    }
}
