namespace SnookerPoint.Domain.Enums;

/// <summary>
/// A manual cash-drawer movement recorded during a shift. Sale-driven cash is
/// added in the checkout phase; Phase 1 covers only these manual movements.
/// </summary>
public enum CashMovementType
{
    /// <summary>Cash added to the drawer (e.g. a float top-up).</summary>
    CashIn = 0,

    /// <summary>Cash removed from the drawer for a non-expense reason.</summary>
    CashOut = 1,

    /// <summary>Money paid out of the drawer for a business expense.</summary>
    Expense = 2,

    /// <summary>Cash removed and banked/secured (a drop to the safe).</summary>
    Drop = 3,
}
