using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.ValueObjects;

namespace SnookerPoint.Application.Shifts;

/// <summary>Per-method payment total within a shift (Phase 4).</summary>
public sealed record ShiftPaymentTotal(string MethodName, PaymentMethodKind Kind, Money Total);

/// <summary>
/// A snapshot of an open shift's cash position and sales. ExpectedCash =
/// Opening + Cash sale payments + CashIn − CashOut − Expenses − Drops. Electronic
/// payments count toward gross sales but never toward physical cash.
/// </summary>
public sealed record ShiftSummary(
    int ShiftId,
    int UserId,
    string UserDisplayName,
    DateTimeOffset OpenedUtc,
    Money OpeningCash,
    Money CashInTotal,
    Money CashOutTotal,
    Money ExpenseTotal,
    Money DropTotal,
    Money ExpectedCash,
    string? OpeningNote,
    Money GrossSales,
    Money CashSales,
    Money ElectronicSales,
    Money DiscountTotal,
    int SaleCount,
    IReadOnlyList<ShiftPaymentTotal> PaymentTotals);

/// <summary>A single recorded cash movement for display.</summary>
public sealed record CashMovementLine(
    CashMovementType Type,
    Money Amount,
    string Reason,
    DateTimeOffset CreatedUtc);

/// <summary>The result of closing a shift.</summary>
public sealed record ShiftCloseResult(
    Money ExpectedCash,
    Money CountedCash,
    Money Variance);
