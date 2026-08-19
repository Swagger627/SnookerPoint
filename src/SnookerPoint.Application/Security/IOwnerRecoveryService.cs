using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Security;

/// <summary>Whether an Owner exists and whether they have an offline recovery code.</summary>
public sealed record OwnerRecoveryStatus(bool HasOwner, bool OwnerHasRecoveryCode);

/// <summary>The outcome of a successful recovery: a fresh replacement recovery code.</summary>
public sealed record OwnerRecoveryResult(string NewRecoveryCode);

/// <summary>
/// The offline Owner recovery workflow. A cryptographically secure recovery code is
/// generated for an Owner, shown once, and stored only as a salted hash. It lets an
/// Owner who has lost their password recover offline: verifying the code allows setting
/// a new password (and optional PIN), invalidates the used code, and issues a fresh one.
/// Failed attempts are rate-limited. The plaintext code is never stored or logged.
/// </summary>
public interface IOwnerRecoveryService
{
    /// <summary>For the login screen: is there an Owner, and does it have a recovery code?</summary>
    OwnerRecoveryStatus GetStatus();

    /// <summary>True when this user is an Owner without a recovery code (one-time prompt).</summary>
    bool NeedsRecoveryCodePrompt(int userId);

    /// <summary>
    /// Regenerates the recovery code for an authenticated Owner after confirming their
    /// password. Returns the new plaintext code once; the previous code is invalidated.
    /// </summary>
    OperationResult<string> RegenerateCode(int ownerUserId, string currentPassword);

    /// <summary>
    /// Recovers an Owner account with a valid recovery code, setting a new password and
    /// optional PIN. On success the used code is invalidated and a replacement returned.
    /// </summary>
    OperationResult<OwnerRecoveryResult> Recover(string username, string recoveryCode, string newPassword, string? newPin);
}

/// <summary>Rate-limit policy for offline Owner recovery attempts.</summary>
public static class OwnerRecoveryPolicy
{
    /// <summary>Failed recovery attempts allowed before a temporary block.</summary>
    public const int MaxFailedAttempts = 5;

    /// <summary>How long recovery stays blocked after hitting the threshold.</summary>
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
}
