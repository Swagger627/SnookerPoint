namespace SnookerPoint.Domain.Enums;

/// <summary>
/// How a table session is charged. Chosen when the session starts and snapshotted onto
/// the session so later configuration changes never alter an existing session.
/// </summary>
public enum BillingType
{
    /// <summary>Charge by elapsed playing time at the snapshotted hourly rate and policy.</summary>
    Hourly = 0,

    /// <summary>A single fixed charge agreed up front; elapsed time never changes it.</summary>
    Fixed = 1,
}
