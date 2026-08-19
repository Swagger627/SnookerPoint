using SnookerPoint.Licensing;

namespace SnookerPoint.App.Licensing;

/// <summary>
/// The single, central runtime licensing gate. Screens call <see cref="EnsureCanOperate"/> before
/// starting new operational work (opening a shift, starting a table session, creating a sale,
/// creating/starting a booking, changing inventory or settings). If the trial has expired it
/// routes to Activation and blocks the new work, while already-open drafts/sessions can still be
/// completed so no money or data is lost. A background re-check keeps the state fresh so the
/// 72-hour limit cannot be bypassed by leaving the app open.
/// </summary>
public interface ILicenseGate
{
    /// <summary>Re-evaluates licensing now and caches the result.</summary>
    LicenseEvaluation Evaluate();

    /// <summary>The most recent evaluation's operational allowance (cheap; does not re-evaluate).</summary>
    bool OperationsAllowed { get; }

    /// <summary>
    /// Returns true if new operational work may proceed. When blocked, records a safe audit event
    /// and routes to the Activation screen, then returns false.
    /// </summary>
    bool EnsureCanOperate();
}
