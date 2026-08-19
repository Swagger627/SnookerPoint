using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Security;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Offline Owner recovery. Generates a secure recovery code (stored only as a salted
/// hash), verifies it during recovery with rate limiting, rotates it on use, and lets an
/// authenticated Owner regenerate it. The plaintext code is returned to the caller once
/// and never stored or logged.
/// </summary>
public sealed class OwnerRecoveryService : IOwnerRecoveryService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ISecretHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<OwnerRecoveryService> _logger;

    public OwnerRecoveryService(
        IDbContextFactory<SnookerPointDbContext> factory,
        ISecretHasher hasher,
        IClock clock,
        ILogger<OwnerRecoveryService> logger)
    {
        _factory = factory;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public OwnerRecoveryStatus GetStatus()
    {
        using var db = _factory.CreateDbContext();
        var owners = db.Users.AsNoTracking().Where(u => u.Role == UserRole.Owner && u.IsActive).ToList();
        return new OwnerRecoveryStatus(owners.Count > 0, owners.Any(o => o.HasRecoveryCode));
    }

    public bool NeedsRecoveryCodePrompt(int userId)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.AsNoTracking().FirstOrDefault(u => u.Id == userId);
        return user is { Role: UserRole.Owner, IsActive: true } && !user.HasRecoveryCode;
    }

    public OperationResult<string> RegenerateCode(int ownerUserId, string currentPassword)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == ownerUserId);
        if (user is null || user.Role != UserRole.Owner)
        {
            return OperationResult<string>.Failure("Only an Owner account has a recovery code.");
        }

        if (!_hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash).IsValid)
        {
            return OperationResult<string>.Failure("Your current password is incorrect.");
        }

        var code = IssueNewCode(db, user);
        WriteAudit(db, AuditActions.OwnerRecoveryCodeGenerated, ownerUserId, user.Id, "Owner recovery code regenerated.");
        db.SaveChanges();
        return OperationResult<string>.Success(code);
    }

    public OperationResult<OwnerRecoveryResult> Recover(string username, string recoveryCode, string newPassword, string? newPin)
    {
        using var db = _factory.CreateDbContext();
        var normalizedUser = (username ?? string.Empty).Trim().ToLowerInvariant();
        var user = db.Users.FirstOrDefault(u => u.Username == normalizedUser);

        // Deliberately vague failures so recovery can't be used to probe accounts.
        if (user is null || user.Role != UserRole.Owner || !user.IsActive || !user.HasRecoveryCode)
        {
            return OperationResult<OwnerRecoveryResult>.Failure("Recovery is not available for that account.");
        }

        var now = _clock.UtcNow;
        if (user.IsRecoveryLockedOut(now))
        {
            return OperationResult<OwnerRecoveryResult>.Failure(
                "Too many recovery attempts. Please wait a while and try again.");
        }

        // Validate the new credentials before consuming the code, so a valid code is
        // never invalidated by an invalid new password/PIN.
        if (StaffCredentialRules.ValidatePassword(newPassword) is { } pwdError)
        {
            return OperationResult<OwnerRecoveryResult>.Failure(pwdError);
        }

        var pin = string.IsNullOrWhiteSpace(newPin) ? null : newPin.Trim();
        if (StaffCredentialRules.ValidatePin(pin) is { } pinError)
        {
            return OperationResult<OwnerRecoveryResult>.Failure(pinError);
        }

        var normalizedCode = RecoveryCodeGenerator.Normalize(recoveryCode);
        if (normalizedCode.Length == 0 || !_hasher.Verify(normalizedCode, user.RecoveryCodeHash!).IsValid)
        {
            RegisterFailure(db, user, now);
            db.SaveChanges();
            return OperationResult<OwnerRecoveryResult>.Failure("That recovery code is not correct.");
        }

        // Success: set new credentials, reset limits, rotate the code.
        user.PasswordHash = _hasher.Hash(newPassword);
        user.PinHash = pin is null ? user.PinHash : _hasher.Hash(pin);
        user.MustChangePassword = false;
        user.RecoveryFailedAttempts = 0;
        user.RecoveryLockedUntilUtc = null;
        user.UpdatedUtc = now;

        var replacement = IssueNewCode(db, user);

        WriteAudit(db, AuditActions.OwnerRecoveryUsed, user.Id, user.Id, "Owner account recovered with a recovery code.");
        WriteAudit(db, AuditActions.OwnerRecoveryCodeGenerated, user.Id, user.Id, "Replacement recovery code issued after recovery.");
        db.SaveChanges();

        return OperationResult<OwnerRecoveryResult>.Success(new OwnerRecoveryResult(replacement));
    }

    /// <summary>Generates a new code, stores its salted hash, and returns the plaintext.</summary>
    private string IssueNewCode(SnookerPointDbContext db, User user)
    {
        var code = RecoveryCodeGenerator.Generate();
        user.RecoveryCodeHash = _hasher.Hash(RecoveryCodeGenerator.Normalize(code));
        user.RecoveryCodeSetUtc = _clock.UtcNow;
        user.RecoveryFailedAttempts = 0;
        user.RecoveryLockedUntilUtc = null;
        user.UpdatedUtc = _clock.UtcNow;
        return code;
    }

    private void RegisterFailure(SnookerPointDbContext db, User user, DateTimeOffset now)
    {
        user.RecoveryFailedAttempts++;
        user.UpdatedUtc = now;

        if (user.RecoveryFailedAttempts >= OwnerRecoveryPolicy.MaxFailedAttempts)
        {
            user.RecoveryLockedUntilUtc = now + OwnerRecoveryPolicy.LockoutDuration;
            user.RecoveryFailedAttempts = 0;
        }
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int actorUserId, int userId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(User),
            EntityId = userId.ToString(),
            Details = details,
        });
    }
}
