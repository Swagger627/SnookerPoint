using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Self-service credential management: a user changes their own password or PIN after
/// re-entering their current password. The stored hash is replaced only after the
/// current password is verified and the new secret validated. Secrets are never logged.
/// </summary>
public sealed class AccountSecurityService : IAccountSecurityService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ISecretHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<AccountSecurityService> _logger;

    public AccountSecurityService(
        IDbContextFactory<SnookerPointDbContext> factory,
        ISecretHasher hasher,
        IClock clock,
        ILogger<AccountSecurityService> logger)
    {
        _factory = factory;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public OperationResult ChangePassword(int userId, string currentPassword, string newPassword)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("Your account was not found.");
        }

        if (!_hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash).IsValid)
        {
            return OperationResult.Failure("Your current password is incorrect.");
        }

        if (StaffCredentialRules.ValidatePassword(newPassword) is { } error)
        {
            return OperationResult.Failure(error);
        }

        // Reject reusing the current password (compare against the stored hash so it
        // also catches a differently-typed but equal value).
        if (_hasher.Verify(newPassword, user.PasswordHash).IsValid)
        {
            return OperationResult.Failure("Your new password must be different from your current password.");
        }

        var now = _clock.UtcNow;
        user.PasswordHash = _hasher.Hash(newPassword);
        user.MustChangePassword = false;
        user.UpdatedUtc = now;

        WriteAudit(db, AuditActions.AccountPasswordChanged, userId, "User changed their own password.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult ChangePin(int userId, string currentPassword, string newPin)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("Your account was not found.");
        }

        if (!_hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash).IsValid)
        {
            return OperationResult.Failure("Your current password is incorrect.");
        }

        var pin = string.IsNullOrWhiteSpace(newPin) ? null : newPin.Trim();
        if (pin is null)
        {
            return OperationResult.Failure("Please enter a PIN.");
        }

        if (StaffCredentialRules.ValidatePin(pin) is { } error)
        {
            return OperationResult.Failure(error);
        }

        // Reject reusing the current PIN when one already exists.
        if (user.PinHash is not null && _hasher.Verify(pin, user.PinHash).IsValid)
        {
            return OperationResult.Failure("Your new PIN must be different from your current PIN.");
        }

        var now = _clock.UtcNow;
        user.PinHash = _hasher.Hash(pin);
        user.UpdatedUtc = now;

        WriteAudit(db, AuditActions.AccountPinChanged, userId, "User set or changed their own PIN.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult RemovePin(int userId, string currentPassword)
    {
        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("Your account was not found.");
        }

        if (!_hasher.Verify(currentPassword ?? string.Empty, user.PasswordHash).IsValid)
        {
            return OperationResult.Failure("Your current password is incorrect.");
        }

        var now = _clock.UtcNow;
        user.PinHash = null;
        user.UpdatedUtc = now;

        WriteAudit(db, AuditActions.AccountPinRemoved, userId, "User removed their own PIN.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int userId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = userId,
            Entity = nameof(User),
            EntityId = userId.ToString(),
            Details = details,
        });
    }
}
