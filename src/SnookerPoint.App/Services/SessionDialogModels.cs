using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.App.Services;

public sealed record StartSessionInput(
    string? CustomerLabel,
    string? Note,
    BillingType BillingType,
    Money? FixedAmount);

public sealed record TransferDestination(int TableId, string Name, Money HourlyRate);

public sealed record TransferInput(int DestinationTableId, string Reason);

public sealed record FinishInput(string? ClosingNote);

public enum CorrectionKind
{
    StartTime,
    PauseStart,
    PauseEnd,
    SegmentRate,
    FixedAmount,
    SwitchToFixed,
    SwitchToHourly,
    ChargeAdjustment,
    Void,
}

/// <summary>What the correction dialog returns for the session service to apply.</summary>
public sealed record CorrectionRequest(
    CorrectionKind Kind,
    int? TargetId,               // pause id or segment id, where applicable
    DateTimeOffset NewTimestamp, // for time corrections
    Money NewAmount,             // new rate or charge adjustment
    string Reason);

public sealed record BillingSettingsInput(
    BillingMethod Method,
    int RoundingIncrementMinutes,
    int MinimumBillableMinutes,
    int GracePeriodMinutes);
