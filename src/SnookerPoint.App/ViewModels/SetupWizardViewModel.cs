using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SnookerPoint.App.Navigation;
using SnookerPoint.App.Services;
using SnookerPoint.App.Theming;
using SnookerPoint.Application.Setup;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;
using SnookerPoint.Infrastructure.Storage;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// Drives the first-run setup wizard: seven plainly-numbered steps with Back/Next,
/// per-step validation, live theme preview, and an atomic save on Finish.
/// </summary>
public partial class SetupWizardViewModel : ObservableObject
{
    public const int TotalSteps = 7;

    private readonly ISetupService _setup;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly IDialogService _dialogs;
    private readonly SnookerPoint.App.Licensing.ILicensingService _licensing;

    public SetupWizardViewModel(
        ISetupService setup,
        INavigationService navigation,
        IThemeService theme,
        IDialogService dialogs,
        SnookerPoint.App.Licensing.ILicensingService licensing,
        AppDataPaths paths)
    {
        _setup = setup;
        _navigation = navigation;
        _theme = theme;
        _dialogs = dialogs;
        _licensing = licensing;

        _selectedTheme = theme.Current == ThemeMode.Light ? "Light" : "Dark";
        _backupFolder = paths.Backups;

        Tables = new ObservableCollection<SetupTableRowViewModel>();
        for (var i = 1; i <= 5; i++)
        {
            Tables.Add(new SetupTableRowViewModel($"Table {i}", TableType.Snooker, "0", isActive: true));
        }
    }

    // ---------- Step state ----------

    [ObservableProperty]
    private int _currentStep = 1;

    partial void OnCurrentStepChanged(int value)
    {
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(IsStep5));
        OnPropertyChanged(nameof(IsStep6));
        OnPropertyChanged(nameof(IsStep7));
        OnPropertyChanged(nameof(StepIndicator));
        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(ShowStart));
        OnPropertyChanged(nameof(ShowNext));
        OnPropertyChanged(nameof(ShowFinish));
        OnPropertyChanged(nameof(ShowSkip));
        RefreshReview();
        BackCommand.NotifyCanExecuteChanged();
    }

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;
    public bool IsStep5 => CurrentStep == 5;
    public bool IsStep6 => CurrentStep == 6;
    public bool IsStep7 => CurrentStep == 7;

    public string StepIndicator => $"Step {CurrentStep} of {TotalSteps}";
    public double ProgressValue => (double)CurrentStep / TotalSteps * 100.0;
    public bool CanGoBack => CurrentStep > 1;

    public bool ShowStart => CurrentStep == 1;
    public bool ShowNext => CurrentStep is >= 2 and <= 6;
    public bool ShowFinish => CurrentStep == 7;
    public bool ShowSkip => CurrentStep is 4 or 6;

    [ObservableProperty]
    private string? _errorMessage;

    // ---------- Step 2: Club details ----------

    [ObservableProperty] private string _clubName = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;

    public IReadOnlyList<string> ThemeOptions { get; } = new[] { "Dark", "Light" };

    [ObservableProperty] private string _selectedTheme;

    partial void OnSelectedThemeChanged(string value) =>
        _theme.Apply(value == "Light" ? ThemeMode.Light : ThemeMode.Dark);

    public string LanguageDisplay => "English";

    // ---------- Step 3: Tables ----------

    public ObservableCollection<SetupTableRowViewModel> Tables { get; }

    [ObservableProperty] private string _bulkRateText = "0";

    // ---------- Step 4: Receipt & printer ----------

    [ObservableProperty] private bool _is58mm = true;
    [ObservableProperty] private bool _is80mm;

    partial void OnIs58mmChanged(bool value)
    {
        if (value && Is80mm)
        {
            Is80mm = false;
        }
    }

    partial void OnIs80mmChanged(bool value)
    {
        if (value && Is58mm)
        {
            Is58mm = false;
        }
    }

    public int ReceiptWidthMm => Is80mm ? 80 : 58;

    [ObservableProperty] private string _printerName = string.Empty;
    [ObservableProperty] private bool _autoPrintReceipt;

    // ---------- Step 5: Owner account ----------

    [ObservableProperty] private string _ownerDisplayName = string.Empty;
    [ObservableProperty] private string _ownerUsername = string.Empty;
    [ObservableProperty] private string _ownerPassword = string.Empty;
    [ObservableProperty] private string _ownerConfirmPassword = string.Empty;
    [ObservableProperty] private string _ownerPin = string.Empty;
    [ObservableProperty] private string _ownerConfirmPin = string.Empty;
    [ObservableProperty] private bool _showSecrets;

    public string PasswordHint =>
        $"Use at least {SetupRules.MinPasswordLength} characters. You can change it later.";

    public string PinHint =>
        $"Optional. {SetupRules.MinPinLength}–{SetupRules.MaxPinLength} digits for fast login.";

    // ---------- Step 6: Backup ----------

    [ObservableProperty] private string _backupFolder;

    // ---------- Step 7: Review ----------

    public string ReviewClubName => string.IsNullOrWhiteSpace(ClubName) ? "—" : ClubName.Trim();
    public string ReviewTableCount => Tables.Count(t => t.IsActive).ToString();
    public string ReviewReceiptWidth => $"{ReceiptWidthMm} mm";
    public string ReviewOwnerUsername => string.IsNullOrWhiteSpace(OwnerUsername) ? "—" : OwnerUsername.Trim();
    public string ReviewBackupLocation => string.IsNullOrWhiteSpace(BackupFolder) ? "Not set" : BackupFolder.Trim();

    public string ReviewRatesSummary
    {
        get
        {
            var active = Tables.Where(t => t.IsActive).ToList();
            if (active.Count == 0)
            {
                return "No active tables";
            }

            return string.Join("   ", active.Select(t =>
            {
                var rate = MoneyInput.TryParseRupees(t.RateText, out var money) ? money.Format() : "Rs ?";
                return $"{t.Name.Trim()}: {rate}/hr";
            }));
        }
    }

    private void RefreshReview()
    {
        OnPropertyChanged(nameof(ReviewClubName));
        OnPropertyChanged(nameof(ReviewTableCount));
        OnPropertyChanged(nameof(ReviewReceiptWidth));
        OnPropertyChanged(nameof(ReviewOwnerUsername));
        OnPropertyChanged(nameof(ReviewBackupLocation));
        OnPropertyChanged(nameof(ReviewRatesSummary));
    }

    // ---------- Commands ----------

    [RelayCommand]
    private void StartSetup() => CurrentStep = 2;

    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void Next()
    {
        var errors = ValidateStep(CurrentStep);
        if (errors.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, errors);
            return;
        }

        ErrorMessage = null;
        if (CurrentStep < TotalSteps)
        {
            CurrentStep++;
        }
    }

    [RelayCommand]
    private void SkipStep() => CurrentStep = Math.Min(CurrentStep + 1, TotalSteps);

    [RelayCommand]
    private void AddTable() =>
        Tables.Add(new SetupTableRowViewModel($"Table {Tables.Count + 1}", TableType.Snooker, "0", isActive: true));

    [RelayCommand]
    private void RemoveTable(SetupTableRowViewModel? row)
    {
        if (row is not null)
        {
            Tables.Remove(row);
        }
    }

    [RelayCommand]
    private void ApplyRateToAll()
    {
        if (!MoneyInput.TryParseRupees(BulkRateText, out _))
        {
            ErrorMessage = "Enter a valid rate to copy to all tables.";
            return;
        }

        foreach (var table in Tables)
        {
            table.RateText = BulkRateText;
        }

        ErrorMessage = null;
    }

    [RelayCommand]
    private void ChooseBackupFolder()
    {
        var chosen = _dialogs.PickFolder(BackupFolder);
        if (chosen is not null)
        {
            BackupFolder = chosen;
        }
    }

    [RelayCommand]
    private void Finish()
    {
        var errors = new List<string>();
        for (var step = 2; step <= 6; step++)
        {
            errors.AddRange(ValidateStep(step));
        }

        if (errors.Count > 0)
        {
            ErrorMessage = string.Join(Environment.NewLine, errors.Distinct());
            return;
        }

        var request = BuildRequest();
        var result = _setup.CompleteSetup(request);
        if (result.Failed)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        // Setup succeeded — begin the 72-hour trial exactly once (never before setup completes).
        _licensing.StartTrialIfNeeded();

        _dialogs.ShowInfo("Setup complete", "Your club is ready. Please sign in with the owner account you just created.");
        _navigation.ShowLogin();
    }

    // ---------- Validation ----------

    private List<string> ValidateStep(int step)
    {
        var errors = new List<string>();

        switch (step)
        {
            case 2:
                if (string.IsNullOrWhiteSpace(ClubName))
                {
                    errors.Add("Please enter the club name.");
                }

                break;

            case 3:
                ValidateTables(errors);
                break;

            case 5:
                ValidateOwner(errors);
                break;

            case 6:
                if (!string.IsNullOrWhiteSpace(BackupFolder) && !IsFolderWritable(BackupFolder))
                {
                    errors.Add("That backup folder can't be written to. Please choose another, or leave it empty to skip.");
                }

                break;
        }

        return errors;
    }

    private void ValidateTables(List<string> errors)
    {
        var active = Tables.Where(t => t.IsActive).ToList();
        if (active.Count == 0)
        {
            errors.Add("Please keep at least one table active.");
        }

        if (active.Any(t => string.IsNullOrWhiteSpace(t.Name)))
        {
            errors.Add("Every active table needs a name.");
        }

        var duplicates = active
            .Where(t => !string.IsNullOrWhiteSpace(t.Name))
            .GroupBy(t => t.Name.Trim().ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => g.First().Name.Trim())
            .ToList();
        if (duplicates.Count > 0)
        {
            errors.Add($"Table names must be different. Duplicate: {string.Join(", ", duplicates)}.");
        }

        foreach (var table in Tables)
        {
            if (!MoneyInput.TryParseRupees(table.RateText, out _))
            {
                errors.Add($"'{(string.IsNullOrWhiteSpace(table.Name) ? "A table" : table.Name.Trim())}' needs a valid rate (0 or more).");
            }
        }
    }

    private void ValidateOwner(List<string> errors) =>
        errors.AddRange(OwnerCredentialValidator.Validate(
            OwnerDisplayName,
            OwnerUsername,
            OwnerPassword,
            OwnerConfirmPassword,
            string.IsNullOrEmpty(OwnerPin) ? null : OwnerPin,
            OwnerConfirmPin));

    private SetupRequest BuildRequest()
    {
        var tables = Tables.Select(t =>
        {
            MoneyInput.TryParseRupees(t.RateText, out var rate);
            return new SetupTableInput(t.Name.Trim(), t.Type, rate, t.IsActive);
        }).ToList();

        var owner = new OwnerAccountInput(
            OwnerDisplayName.Trim(),
            OwnerUsername.Trim(),
            OwnerPassword,
            string.IsNullOrEmpty(OwnerPin) ? null : OwnerPin);

        return new SetupRequest(
            ClubName.Trim(),
            string.IsNullOrWhiteSpace(Address) ? null : Address.Trim(),
            string.IsNullOrWhiteSpace(Phone) ? null : Phone.Trim(),
            SelectedTheme,
            "en",
            ReceiptWidthMm,
            string.IsNullOrWhiteSpace(PrinterName) ? null : PrinterName.Trim(),
            AutoPrintReceipt,
            string.IsNullOrWhiteSpace(BackupFolder) ? null : BackupFolder.Trim(),
            tables,
            owner);
    }

    private static bool IsFolderWritable(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return false;
            }

            var probe = Path.Combine(path, $".snookerpoint-write-test-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
