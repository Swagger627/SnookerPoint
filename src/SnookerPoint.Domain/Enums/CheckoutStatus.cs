namespace SnookerPoint.Domain.Enums;

/// <summary>
/// Whether a completed session's charge has been taken to checkout. Phase 2 only
/// produces <see cref="AwaitingCheckout"/>; payment/sale creation is a later phase.
/// </summary>
public enum CheckoutStatus
{
    /// <summary>Not applicable — the session is not completed.</summary>
    NotCompleted = 0,

    /// <summary>Completed and its charge is stored, waiting to be attached to a sale.</summary>
    AwaitingCheckout = 1,

    /// <summary>The charge has been checked out (reserved for the checkout phase).</summary>
    CheckedOut = 2,
}
