using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Tables;

/// <summary>
/// The live table-management service: starting, pausing/resuming, transferring,
/// finishing, correcting and querying sessions. All state changes require an open
/// shift and the appropriate permission, are transactional, and are audited. Charge
/// maths always goes through <see cref="Billing.ISessionBillingCalculator"/>.
/// </summary>
public interface ITableSessionService
{
    /// <summary>Active tables with their live session state for the dashboard.</summary>
    IReadOnlyList<TableCard> GetDashboard();

    /// <summary>Starts a session on an available table. Returns the session number.</summary>
    OperationResult<int> StartSession(StartSessionRequest request);

    OperationResult PauseSession(int sessionId, int userId, int shiftId);

    OperationResult ResumeSession(int sessionId, int userId, int shiftId);

    OperationResult TransferSession(int sessionId, int destinationTableId, int userId, int shiftId, string reason);

    /// <summary>A live (unfrozen) summary for the finish dialog.</summary>
    OperationResult<SessionSummary> GetFinishPreview(int sessionId);

    /// <summary>Finishes a session, freezing the final charge as Awaiting Checkout.</summary>
    OperationResult<SessionSummary> FinishSession(int sessionId, int userId, int shiftId, string? closingNote);

    /// <summary>A computed summary for any session (frozen for completed sessions).</summary>
    SessionSummary? GetSessionSummary(int sessionId);

    IReadOnlyList<SessionHistoryItem> GetHistory(SessionHistoryFilter filter);

    // --- Corrections (Owner/Administrator/Manager only) ---

    /// <summary>The data the correction dialog needs, or null if the session is not found.</summary>
    SessionCorrectionContext? GetCorrectionContext(int sessionId);

    OperationResult CorrectStartTime(int sessionId, DateTimeOffset newStartUtc, string reason, int actorUserId, int shiftId);

    OperationResult CorrectPauseStart(int pauseId, DateTimeOffset newPausedUtc, string reason, int actorUserId, int shiftId);

    OperationResult CorrectPauseEnd(int pauseId, DateTimeOffset newResumedUtc, string reason, int actorUserId, int shiftId);

    OperationResult CorrectSegmentRate(int segmentId, Money newRate, string reason, int actorUserId, int shiftId);

    /// <summary>Corrects the fixed-amount snapshot of a fixed-billing session.</summary>
    OperationResult CorrectFixedAmount(int sessionId, Money newFixedAmount, string reason, int actorUserId, int shiftId);

    /// <summary>
    /// Switches a session's billing type. When switching to Fixed a non-negative fixed
    /// amount is required; when switching to Hourly the snapshotted segment rates are used.
    /// </summary>
    OperationResult CorrectBillingType(int sessionId, BillingType newType, Money? newFixedAmount, string reason, int actorUserId, int shiftId);

    OperationResult AddChargeAdjustment(int sessionId, Money amount, string reason, int actorUserId, int shiftId);

    OperationResult VoidSession(int sessionId, string reason, int actorUserId, int shiftId);
}
