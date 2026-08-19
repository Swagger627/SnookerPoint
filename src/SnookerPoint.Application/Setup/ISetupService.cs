using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Setup;

/// <summary>
/// Drives first-run setup. Determines whether setup has completed and persists the
/// wizard payload atomically (all-or-nothing) marking setup complete only on success.
/// </summary>
public interface ISetupService
{
    /// <summary>True when first-run setup has been completed.</summary>
    bool IsSetupComplete();

    /// <summary>
    /// Validates and saves the entire setup in a single transaction. On any failure
    /// nothing is persisted, setup stays incomplete, and friendly errors are returned.
    /// </summary>
    OperationResult CompleteSetup(SetupRequest request);
}

/// <summary>Setup validation constants shared by the service and the wizard UI.</summary>
public static class SetupRules
{
    public const int MinPasswordLength = 6;
    public const int MinPinLength = 4;
    public const int MaxPinLength = 8;
}
