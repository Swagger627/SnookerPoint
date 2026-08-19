using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Entities;

/// <summary>
/// A staff account. Passwords and PINs are stored only as salted, versioned hashes
/// (never plaintext, never reversible). Account state supports lockout after
/// repeated failures and disabling without deletion.
/// </summary>
public sealed class User
{
    public int Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    /// <summary>Encoded, salted password hash (algorithm/version/iterations/salt/hash).</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Optional encoded, salted PIN hash for fast login. Null when no PIN is set.</summary>
    public string? PinHash { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// True when the account must change its password at next login (e.g. after an
    /// administrator issued a temporary password). Cleared once the user sets their own.
    /// </summary>
    public bool MustChangePassword { get; set; }

    public int FailedLoginAttempts { get; set; }

    /// <summary>When set and in the future, the account is temporarily locked out.</summary>
    public DateTimeOffset? LockedOutUntilUtc { get; set; }

    // --- Owner offline recovery (only meaningful for Owner accounts) ---

    /// <summary>Salted hash of the offline Owner recovery code. Never the plaintext code.</summary>
    public string? RecoveryCodeHash { get; set; }

    /// <summary>When the current recovery code was generated.</summary>
    public DateTimeOffset? RecoveryCodeSetUtc { get; set; }

    /// <summary>Consecutive failed recovery attempts, for rate limiting.</summary>
    public int RecoveryFailedAttempts { get; set; }

    /// <summary>When set and in the future, recovery attempts are temporarily blocked.</summary>
    public DateTimeOffset? RecoveryLockedUntilUtc { get; set; }

    public DateTimeOffset? LastLoginUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>True when a PIN has been configured for this user.</summary>
    public bool HasPin => !string.IsNullOrEmpty(PinHash);

    /// <summary>True when an offline recovery code has been set for this account.</summary>
    public bool HasRecoveryCode => !string.IsNullOrEmpty(RecoveryCodeHash);

    /// <summary>True when recovery attempts are currently rate-limited at the given instant.</summary>
    public bool IsRecoveryLockedOut(DateTimeOffset now) =>
        RecoveryLockedUntilUtc is { } until && until > now;

    /// <summary>True when the account is currently locked out at the given instant.</summary>
    public bool IsLockedOut(DateTimeOffset now) =>
        LockedOutUntilUtc is { } until && until > now;
}
