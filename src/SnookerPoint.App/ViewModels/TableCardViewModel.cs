using CommunityToolkit.Mvvm.ComponentModel;
using SnookerPoint.App.Services;
using SnookerPoint.Application.Billing;
using SnookerPoint.Application.Tables;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.ViewModels;

/// <summary>
/// One table card on the dashboard. Static details come from the snapshot; the
/// elapsed time and estimated charge are recomputed each second by the shared
/// billing calculator from persisted timestamps (never written to the DB per tick).
/// </summary>
public partial class TableCardViewModel : ObservableObject
{
    private readonly TableCard _card;
    private readonly ISessionBillingCalculator _calculator;

    public TableCardViewModel(TableCard card, ISessionBillingCalculator calculator)
    {
        _card = card;
        _calculator = calculator;
        Update(DateTimeOffset.UtcNow);
    }

    public int TableId => _card.TableId;
    public string Name => _card.Name;
    public string TypeText => _card.Type.ToString();
    public string RateText => _card.Session is { BillingType: BillingType.Fixed, FixedAmount: { } fixedAmount }
        ? $"Fixed {fixedAmount.Format()}"
        : $"{_card.HourlyRate.Format()}/hr";

    /// <summary>True for a fixed-charge session (its charge never changes with time).</summary>
    public bool IsFixedBilling => _card.Session?.BillingType == BillingType.Fixed;

    public DashboardStatus Status => _card.Status;
    public string StatusText => Status switch
    {
        DashboardStatus.Available => "Available",
        DashboardStatus.InUse => "In use",
        DashboardStatus.Paused => "Paused",
        _ => string.Empty,
    };

    public bool IsAvailable => Status == DashboardStatus.Available;
    public bool IsInUse => Status == DashboardStatus.InUse;
    public bool IsPaused => Status == DashboardStatus.Paused;
    public bool HasSession => _card.Session is not null;

    public int? SessionId => _card.Session?.SessionId;
    public DateTimeOffset? StartUtc => _card.Session?.StartUtc;
    public string SessionNumberText => _card.Session is { } s ? $"Session #{s.SessionNumber}" : string.Empty;
    public string StartedByText => _card.Session?.StartedByName ?? string.Empty;
    public string? CustomerLabel => _card.Session?.CustomerLabel;
    public bool HasCustomerLabel => !string.IsNullOrWhiteSpace(_card.Session?.CustomerLabel);
    public string StartedAtText => _card.Session is { } s ? $"Started {DisplayFormat.LocalTime(s.StartUtc)}" : string.Empty;

    [ObservableProperty] private string _elapsedText = "0:00:00";
    [ObservableProperty] private string _chargeText = "Rs 0";
    [ObservableProperty] private string _pausedText = string.Empty;
    [ObservableProperty] private bool _hasPaused;

    /// <summary>Recomputes the live elapsed/charge/paused display for the given instant.</summary>
    public void Update(DateTimeOffset now)
    {
        if (_card.Session is not { } session)
        {
            return;
        }

        var charge = _calculator.Calculate(session.Policy, session.Segments, session.Pauses, now);
        ElapsedText = DisplayFormat.Duration(charge.ElapsedSeconds);
        // Fixed-billing sessions show the agreed charge; elapsed time keeps ticking above.
        ChargeText = BillingResolution.BaseCharge(session.BillingType, session.FixedAmount, charge.Charge).Format();
        HasPaused = charge.PausedSeconds > 0;
        PausedText = HasPaused ? $"Paused {DisplayFormat.DurationShort(charge.PausedSeconds)}" : string.Empty;
    }
}
