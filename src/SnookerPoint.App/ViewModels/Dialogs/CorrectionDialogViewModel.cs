using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Billing;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels.Dialogs;

/// <summary>A pause choice for the correction dialog.</summary>
public sealed record PauseOption(int PauseId, string Text, DateTimeOffset Start, DateTimeOffset? End);

/// <summary>A segment (table/rate) choice for the correction dialog.</summary>
public sealed record SegmentOption(int SegmentId, string Text, Money HourlyRate);

/// <summary>
/// Backs the controlled correction dialog. Supports session start, pause start/end,
/// segment hourly rate, fixed-amount snapshot, billing-type switch (Hourly ↔ Fixed),
/// charge adjustment and void. Shows the original value, the proposed value, and the
/// old-vs-recalculated charge before confirmation. All require a reason.
/// </summary>
public partial class CorrectionDialogViewModel : ObservableObject
{
    private readonly SessionCorrectionContext _context;
    private readonly ISessionBillingCalculator _calculator;
    private readonly DateTimeOffset _asOf;

    public CorrectionDialogViewModel(SessionCorrectionContext context, ISessionBillingCalculator calculator)
    {
        _context = context;
        _calculator = calculator;
        _asOf = context.FinishUtc ?? DateTimeOffset.UtcNow;

        SessionNumber = context.SessionNumber;
        StatusText = context.Status.ToString();
        CurrentStartText = DisplayFormat.LocalDateTime(context.StartUtc);
        OldChargeText = context.CurrentCharge.Format();
        IsBillingFixed = context.BillingType == BillingType.Fixed;
        CurrentBillingText = IsBillingFixed
            ? $"Fixed · {(context.FixedAmount ?? Money.Zero).Format()}"
            : "Hourly";

        Pauses = context.Pauses
            .Select(p => new PauseOption(p.PauseId,
                p.ResumedUtc is { } e ? $"Paused {DisplayFormat.LocalTime(p.PausedUtc)} – {DisplayFormat.LocalTime(e)}" : $"Paused {DisplayFormat.LocalTime(p.PausedUtc)} (open)",
                p.PausedUtc, p.ResumedUtc))
            .ToList();
        ClosedPauses = Pauses.Where(p => p.End is not null).ToList();
        Segments = context.Segments
            .Select(s => new SegmentOption(s.SegmentId, $"{s.TableName} · {s.HourlyRate.Format()}/hr", s.HourlyRate))
            .ToList();

        _selectedPause = Pauses.Count > 0 ? Pauses[0] : null;
        _selectedClosedPause = ClosedPauses.Count > 0 ? ClosedPauses[0] : null;
        _selectedSegment = Segments.Count > 0 ? Segments[0] : null;

        RecomputePreview();
    }

    public int SessionNumber { get; }
    public string StatusText { get; }
    public string CurrentStartText { get; }
    public string OldChargeText { get; }
    public bool IsBillingFixed { get; }
    public string CurrentBillingText { get; }

    public IReadOnlyList<PauseOption> Pauses { get; }
    public IReadOnlyList<PauseOption> ClosedPauses { get; }
    public IReadOnlyList<SegmentOption> Segments { get; }

    public bool HasPauses => Pauses.Count > 0;
    public bool HasClosedPauses => ClosedPauses.Count > 0;
    public bool HasSegments => Segments.Count > 0;

    /// <summary>Hourly-rate correction only applies to hourly sessions.</summary>
    public bool CanCorrectRate => HasSegments && !IsBillingFixed;

    /// <summary>Fixed-amount correction only applies to fixed sessions.</summary>
    public bool CanCorrectFixed => IsBillingFixed;

    public bool CanSwitchToFixed => !IsBillingFixed;
    public bool CanSwitchToHourly => IsBillingFixed;

    // Correction type (mutually exclusive).
    [ObservableProperty] private bool _isStartTime = true;
    [ObservableProperty] private bool _isPauseStart;
    [ObservableProperty] private bool _isPauseEnd;
    [ObservableProperty] private bool _isRate;
    [ObservableProperty] private bool _isFixedAmount;
    [ObservableProperty] private bool _isSwitchToFixed;
    [ObservableProperty] private bool _isSwitchToHourly;
    [ObservableProperty] private bool _isChargeAdjustment;
    [ObservableProperty] private bool _isVoid;

    private bool _switching;

    partial void OnIsStartTimeChanged(bool value) => Choose(value, nameof(IsStartTime));
    partial void OnIsPauseStartChanged(bool value) => Choose(value, nameof(IsPauseStart));
    partial void OnIsPauseEndChanged(bool value) => Choose(value, nameof(IsPauseEnd));
    partial void OnIsRateChanged(bool value) => Choose(value, nameof(IsRate));
    partial void OnIsFixedAmountChanged(bool value) => Choose(value, nameof(IsFixedAmount));
    partial void OnIsSwitchToFixedChanged(bool value) => Choose(value, nameof(IsSwitchToFixed));
    partial void OnIsSwitchToHourlyChanged(bool value) => Choose(value, nameof(IsSwitchToHourly));
    partial void OnIsChargeAdjustmentChanged(bool value) => Choose(value, nameof(IsChargeAdjustment));
    partial void OnIsVoidChanged(bool value) => Choose(value, nameof(IsVoid));

    private void Choose(bool value, string chosen)
    {
        if (!value || _switching)
        {
            return;
        }

        _switching = true;
        foreach (var (name, setter) in Setters())
        {
            if (name != chosen)
            {
                setter(false);
            }
        }

        _switching = false;
        RecomputePreview();
    }

    private IEnumerable<(string, Action<bool>)> Setters() => new (string, Action<bool>)[]
    {
        (nameof(IsStartTime), v => IsStartTime = v),
        (nameof(IsPauseStart), v => IsPauseStart = v),
        (nameof(IsPauseEnd), v => IsPauseEnd = v),
        (nameof(IsRate), v => IsRate = v),
        (nameof(IsFixedAmount), v => IsFixedAmount = v),
        (nameof(IsSwitchToFixed), v => IsSwitchToFixed = v),
        (nameof(IsSwitchToHourly), v => IsSwitchToHourly = v),
        (nameof(IsChargeAdjustment), v => IsChargeAdjustment = v),
        (nameof(IsVoid), v => IsVoid = v),
    };

    // Inputs.
    [ObservableProperty] private string _startShiftMinutes = "0";
    [ObservableProperty] private string _pauseStartShiftMinutes = "0";
    [ObservableProperty] private string _pauseEndShiftMinutes = "0";
    [ObservableProperty] private string _newRateRupees = "0";
    [ObservableProperty] private string _newFixedRupees = "0";
    [ObservableProperty] private string _switchFixedRupees = "0";
    [ObservableProperty] private string _adjustmentRupees = "0";
    [ObservableProperty] private string _reason = string.Empty;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private PauseOption? _selectedPause;
    [ObservableProperty] private PauseOption? _selectedClosedPause;
    [ObservableProperty] private SegmentOption? _selectedSegment;

    partial void OnStartShiftMinutesChanged(string value) => RecomputePreview();
    partial void OnPauseStartShiftMinutesChanged(string value) => RecomputePreview();
    partial void OnPauseEndShiftMinutesChanged(string value) => RecomputePreview();
    partial void OnNewRateRupeesChanged(string value) => RecomputePreview();
    partial void OnNewFixedRupeesChanged(string value) => RecomputePreview();
    partial void OnSwitchFixedRupeesChanged(string value) => RecomputePreview();
    partial void OnAdjustmentRupeesChanged(string value) => RecomputePreview();
    partial void OnSelectedPauseChanged(PauseOption? value) { RecomputePreview(); OnPropertyChanged(nameof(SelectedPauseStartText)); }
    partial void OnSelectedClosedPauseChanged(PauseOption? value) { RecomputePreview(); OnPropertyChanged(nameof(SelectedPauseEndText)); }
    partial void OnSelectedSegmentChanged(SegmentOption? value) { RecomputePreview(); OnPropertyChanged(nameof(SelectedSegmentRateText)); }

    public string SelectedPauseStartText => SelectedPause is { } p ? DisplayFormat.LocalDateTime(p.Start) : "—";
    public string SelectedPauseEndText => SelectedClosedPause?.End is { } e ? DisplayFormat.LocalDateTime(e) : "—";
    public string SelectedSegmentRateText => SelectedSegment is { } s ? $"{s.HourlyRate.Format()}/hr" : "—";

    [ObservableProperty] private string _proposedValueText = "—";
    [ObservableProperty] private string _newChargeText = "—";

    private void RecomputePreview()
    {
        var segments = _context.Segments.Select(s => new SegmentTiming(s.HourlyRate, s.StartUtc, s.EndUtc)).ToList();
        var pauses = _context.Pauses.Select(p => new PauseInterval(p.PausedUtc, p.ResumedUtc)).ToList();
        var proposed = "—";
        bool chargeOverride = false;
        var charge = Money.Zero;

        if (IsStartTime && TryMinutes(StartShiftMinutes, out var m1) && segments.Count > 0)
        {
            var ns = _context.StartUtc.AddMinutes(m1);
            segments[0] = segments[0] with { Start = ns };
            proposed = DisplayFormat.LocalDateTime(ns);
        }
        else if (IsPauseStart && SelectedPause is { } sp && TryMinutes(PauseStartShiftMinutes, out var m2))
        {
            var idx = _context.Pauses.ToList().FindIndex(x => x.PauseId == sp.PauseId);
            var nt = sp.Start.AddMinutes(m2);
            if (idx >= 0) { pauses[idx] = pauses[idx] with { Start = nt }; }
            proposed = DisplayFormat.LocalDateTime(nt);
        }
        else if (IsPauseEnd && SelectedClosedPause is { End: { } end } scp && TryMinutes(PauseEndShiftMinutes, out var m3))
        {
            var idx = _context.Pauses.ToList().FindIndex(x => x.PauseId == scp.PauseId);
            var nt = end.AddMinutes(m3);
            if (idx >= 0) { pauses[idx] = pauses[idx] with { End = nt }; }
            proposed = DisplayFormat.LocalDateTime(nt);
        }
        else if (IsRate && SelectedSegment is { } seg && TryRupees(NewRateRupees, out var rate))
        {
            var idx = _context.Segments.ToList().FindIndex(x => x.SegmentId == seg.SegmentId);
            if (idx >= 0) { segments[idx] = segments[idx] with { Rate = rate }; }
            proposed = $"{rate.Format()}/hr";
        }
        else if (IsFixedAmount && TryRupees(NewFixedRupees, out var newFixed))
        {
            proposed = newFixed.Format();
            charge = newFixed;
            chargeOverride = true;
        }
        else if (IsSwitchToFixed && TryRupees(SwitchFixedRupees, out var switchFixed))
        {
            proposed = $"Fixed · {switchFixed.Format()}";
            charge = switchFixed;
            chargeOverride = true;
        }
        else if (IsSwitchToHourly)
        {
            proposed = "Hourly";
            // Charge recomputed from the snapshotted segment rates below (not an override).
        }
        else if (IsChargeAdjustment && TryRupees(AdjustmentRupees, out var amount, allowNegative: true))
        {
            proposed = amount.Format();
            charge = _context.CurrentCharge + amount;
            chargeOverride = true;
        }
        else if (IsVoid)
        {
            proposed = "Voided (no charge)";
            charge = Money.Zero;
            chargeOverride = true;
        }

        // A fixed session that is NOT being switched to hourly keeps its fixed charge.
        var keepFixed = IsBillingFixed && !IsSwitchToHourly && !IsFixedAmount && !IsChargeAdjustment && !IsVoid;

        ProposedValueText = proposed;
        if (chargeOverride)
        {
            NewChargeText = charge.Format();
        }
        else if (keepFixed)
        {
            NewChargeText = (_context.FixedAmount ?? Money.Zero).Format();
        }
        else
        {
            NewChargeText = _calculator.Calculate(_context.Policy, segments, pauses, _asOf).Charge.Format();
        }
    }

    public CorrectionRequest? Result { get; private set; }

    public bool TryConfirm()
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            ErrorMessage = "Please enter a reason.";
            return false;
        }

        if (IsStartTime)
        {
            if (!TryMinutes(StartShiftMinutes, out var m))
            {
                return Fail("Enter minutes to shift the start time (e.g. -10).");
            }

            Result = new CorrectionRequest(CorrectionKind.StartTime, null, _context.StartUtc.AddMinutes(m), Money.Zero, Reason.Trim());
        }
        else if (IsPauseStart)
        {
            if (SelectedPause is not { } p)
            {
                return Fail("Please choose a pause.");
            }

            if (!TryMinutes(PauseStartShiftMinutes, out var m))
            {
                return Fail("Enter minutes to shift the pause start.");
            }

            Result = new CorrectionRequest(CorrectionKind.PauseStart, p.PauseId, p.Start.AddMinutes(m), Money.Zero, Reason.Trim());
        }
        else if (IsPauseEnd)
        {
            if (SelectedClosedPause is not { End: { } end } p)
            {
                return Fail("Please choose a completed pause.");
            }

            if (!TryMinutes(PauseEndShiftMinutes, out var m))
            {
                return Fail("Enter minutes to shift the pause end.");
            }

            Result = new CorrectionRequest(CorrectionKind.PauseEnd, p.PauseId, end.AddMinutes(m), Money.Zero, Reason.Trim());
        }
        else if (IsRate)
        {
            if (SelectedSegment is not { } seg)
            {
                return Fail("Please choose a table period.");
            }

            if (!TryRupees(NewRateRupees, out var rate))
            {
                return Fail("Enter a valid rate (0 or more).");
            }

            Result = new CorrectionRequest(CorrectionKind.SegmentRate, seg.SegmentId, _context.StartUtc, rate, Reason.Trim());
        }
        else if (IsFixedAmount)
        {
            if (!TryRupees(NewFixedRupees, out var amount))
            {
                return Fail("Enter a valid fixed charge (0 or more).");
            }

            Result = new CorrectionRequest(CorrectionKind.FixedAmount, null, _context.StartUtc, amount, Reason.Trim());
        }
        else if (IsSwitchToFixed)
        {
            if (!TryRupees(SwitchFixedRupees, out var amount))
            {
                return Fail("Enter a valid fixed charge (0 or more).");
            }

            Result = new CorrectionRequest(CorrectionKind.SwitchToFixed, null, _context.StartUtc, amount, Reason.Trim());
        }
        else if (IsSwitchToHourly)
        {
            Result = new CorrectionRequest(CorrectionKind.SwitchToHourly, null, _context.StartUtc, Money.Zero, Reason.Trim());
        }
        else if (IsChargeAdjustment)
        {
            if (!TryRupees(AdjustmentRupees, out var amount, allowNegative: true) || amount.IsZero)
            {
                return Fail("Enter a non-zero amount (negative to reduce the charge).");
            }

            Result = new CorrectionRequest(CorrectionKind.ChargeAdjustment, null, _context.StartUtc, amount, Reason.Trim());
        }
        else // Void
        {
            Result = new CorrectionRequest(CorrectionKind.Void, null, _context.StartUtc, Money.Zero, Reason.Trim());
        }

        return true;
    }

    private bool Fail(string message)
    {
        ErrorMessage = message;
        return false;
    }

    private static bool TryMinutes(string? text, out int minutes) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out minutes);

    private static bool TryRupees(string? text, out Money money, bool allowNegative = false)
    {
        money = Money.Zero;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var rupees))
        {
            return false;
        }

        if (!allowNegative && rupees < 0)
        {
            return false;
        }

        money = Money.FromRupees(rupees);
        return true;
    }
}
