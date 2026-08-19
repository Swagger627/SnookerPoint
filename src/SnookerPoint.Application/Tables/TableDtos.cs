using SnookerPoint.Application.Billing;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Tables;

/// <summary>Dashboard status of a table.</summary>
public enum DashboardStatus
{
    Available,
    InUse,
    Paused,
}

/// <summary>
/// A live session's persisted timing data, enough for the UI to recompute the
/// elapsed time and estimated charge each second via the billing calculator without
/// touching the database.
/// </summary>
public sealed record LiveSessionSnapshot(
    int SessionId,
    int SessionNumber,
    int CurrentTableId,
    bool IsPaused,
    DateTimeOffset StartUtc,
    string StartedByName,
    string? CustomerLabel,
    string? Note,
    BillingPolicy Policy,
    BillingType BillingType,
    Money? FixedAmount,
    IReadOnlyList<SegmentTiming> Segments,
    IReadOnlyList<PauseInterval> Pauses);

/// <summary>A table card for the dashboard, with its live session if any.</summary>
public sealed record TableCard(
    int TableId,
    string Name,
    TableType Type,
    Money HourlyRate,
    DashboardStatus Status,
    LiveSessionSnapshot? Session);

/// <summary>Request to start a session.</summary>
public sealed record StartSessionRequest(
    int TableId,
    int UserId,
    int ShiftId,
    string? CustomerLabel,
    string? Note,
    BillingType BillingType = BillingType.Hourly,
    Money? FixedAmount = null);

/// <summary>One rate span for a finish/summary breakdown.</summary>
public sealed record RateSegmentLine(
    string TableName,
    Money HourlyRate,
    long ActiveSeconds);

/// <summary>A computed session summary for the finish dialog and history detail.</summary>
public sealed record SessionSummary(
    int SessionId,
    int SessionNumber,
    DateTimeOffset StartUtc,
    DateTimeOffset? FinishUtc,
    long ElapsedSeconds,
    long PausedSeconds,
    long BillableSeconds,
    Money Charge,
    IReadOnlyList<RateSegmentLine> Segments,
    string? CustomerLabel,
    string? Note,
    bool IsFrozen,
    BillingType BillingType = BillingType.Hourly,
    Money? FixedAmount = null);

/// <summary>A row in the completed-session history.</summary>
public sealed record SessionHistoryItem(
    int SessionId,
    int SessionNumber,
    DateTimeOffset StartUtc,
    DateTimeOffset? FinishUtc,
    string TableNames,
    long BillableSeconds,
    Money FinalCharge,
    SessionStatus Status,
    CheckoutStatus CheckoutStatus,
    string StartedByName,
    string? FinishedByName);

/// <summary>Filter for the history query (all optional).</summary>
public sealed record SessionHistoryFilter(
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    int? TableId = null,
    int? SessionNumber = null,
    SessionStatus? Status = null);

// ---------- Corrections ----------

/// <summary>A segment shown in the correction dialog (for rate correction).</summary>
public sealed record CorrectionSegmentInfo(
    int SegmentId,
    string TableName,
    Money HourlyRate,
    DateTimeOffset StartUtc,
    DateTimeOffset? EndUtc);

/// <summary>A pause shown in the correction dialog (for timestamp corrections).</summary>
public sealed record CorrectionPauseInfo(
    int PauseId,
    DateTimeOffset PausedUtc,
    DateTimeOffset? ResumedUtc);

/// <summary>
/// Everything the correction dialog needs: the session's identity/status, its policy
/// and timings (so the dialog can preview the effect via the billing calculator), and
/// the current charge.
/// </summary>
public sealed record SessionCorrectionContext(
    int SessionId,
    int SessionNumber,
    SessionStatus Status,
    DateTimeOffset StartUtc,
    DateTimeOffset? FinishUtc,
    BillingPolicy Policy,
    Money CurrentCharge,
    BillingType BillingType,
    Money? FixedAmount,
    IReadOnlyList<CorrectionSegmentInfo> Segments,
    IReadOnlyList<CorrectionPauseInfo> Pauses);
