using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Shifts;

/// <summary>
/// Manages cashier shifts and their append-only cash movements. Enforces one open
/// shift per user and prevents reopening or editing a closed shift.
/// </summary>
public interface IShiftService
{
    /// <summary>Opens a new shift for the user. Fails if one is already open.</summary>
    OperationResult<ShiftSummary> OpenShift(int userId, Money openingCash, string? note);

    /// <summary>The user's currently open shift, or null if none is open.</summary>
    ShiftSummary? GetCurrentShift(int userId);

    /// <summary>Records an append-only cash movement against an open shift.</summary>
    OperationResult RecordCashMovement(
        int shiftId,
        CashMovementType type,
        Money amount,
        string reason,
        int actorUserId,
        int? approverUserId = null);

    /// <summary>All cash movements recorded against a shift, newest first.</summary>
    IReadOnlyList<CashMovementLine> GetCashMovements(int shiftId);

    /// <summary>
    /// Closes an open shift: freezes expected cash, records counted cash and
    /// variance, and writes an audit event. A closed shift cannot be reopened.
    /// </summary>
    OperationResult<ShiftCloseResult> CloseShift(int shiftId, Money countedCash, string? note);
}
