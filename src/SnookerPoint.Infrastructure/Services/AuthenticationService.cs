using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Authentication;
using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Infrastructure.Persistence;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Authenticates by password or PIN with failed-attempt lockout, disabled-account
/// checks, secret rehashing, and audit logging. Never stores or logs secrets.
/// </summary>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ISecretHasher _hasher;
    private readonly IClock _clock;
    private readonly ILogger<AuthenticationService> _logger;

    public AuthenticationService(
        IDbContextFactory<SnookerPointDbContext> factory,
        ISecretHasher hasher,
        IClock clock,
        ILogger<AuthenticationService> logger)
    {
        _factory = factory;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public LoginResult LoginWithPassword(string username, string password) =>
        Login(username, password, usePin: false);

    public LoginResult LoginWithPin(string username, string pin) =>
        Login(username, pin, usePin: true);

    public void Logout(int userId)
    {
        using var db = _factory.CreateDbContext();
        WriteAudit(db, AuditActions.Logout, userId, "User logged out.");
        db.SaveChanges();
    }

    private LoginResult Login(string username, string secret, bool usePin)
    {
        var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
        var now = _clock.UtcNow;

        using var db = _factory.CreateDbContext();
        var user = db.Users.FirstOrDefault(u => u.Username == normalized);

        if (user is null)
        {
            WriteAudit(db, AuditActions.LoginFailed, null, $"Unknown username '{normalized}'.");
            db.SaveChanges();
            return LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            WriteAudit(db, AuditActions.LoginFailed, user.Id, "Account is disabled.");
            db.SaveChanges();
            return LoginResult.Failure(LoginFailureReason.AccountDisabled);
        }

        if (user.IsLockedOut(now))
        {
            var remaining = user.LockedOutUntilUtc!.Value - now;
            return LoginResult.Failure(LoginFailureReason.AccountLockedOut, remaining);
        }

        if (usePin && !user.HasPin)
        {
            return LoginResult.Failure(LoginFailureReason.PinNotSet);
        }

        var storedHash = usePin ? user.PinHash! : user.PasswordHash;
        var verification = _hasher.Verify(secret, storedHash);

        if (!verification.IsValid)
        {
            RegisterFailure(db, user, now, usePin);
            db.SaveChanges();

            return user.IsLockedOut(now)
                ? LoginResult.Failure(LoginFailureReason.AccountLockedOut, user.LockedOutUntilUtc!.Value - now)
                : LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        }

        // Success — reset counters, rehash if needed, record login.
        user.FailedLoginAttempts = 0;
        user.LockedOutUntilUtc = null;
        user.LastLoginUtc = now;
        user.UpdatedUtc = now;

        if (verification.NeedsRehash)
        {
            if (usePin)
            {
                user.PinHash = _hasher.Hash(secret);
            }
            else
            {
                user.PasswordHash = _hasher.Hash(secret);
            }
        }

        WriteAudit(db, AuditActions.LoginSucceeded, user.Id, usePin ? "PIN login." : "Password login.");
        db.SaveChanges();

        return LoginResult.Success(new AuthenticatedUser(
            user.Id, user.DisplayName, user.Username, user.Role, user.HasPin, user.MustChangePassword));
    }

    private void RegisterFailure(SnookerPointDbContext db, User user, DateTimeOffset now, bool usePin)
    {
        user.FailedLoginAttempts++;
        user.UpdatedUtc = now;

        WriteAudit(db, AuditActions.LoginFailed, user.Id, usePin ? "Incorrect PIN." : "Incorrect password.");

        if (user.FailedLoginAttempts >= AccountSecurityPolicy.MaxFailedAttempts)
        {
            user.LockedOutUntilUtc = now + AccountSecurityPolicy.LockoutDuration;
            user.FailedLoginAttempts = 0;
            WriteAudit(db, AuditActions.AccountLockedOut, user.Id,
                $"Locked out for {AccountSecurityPolicy.LockoutDuration.TotalMinutes:0} minutes after repeated failures.");
        }
    }

    private void WriteAudit(SnookerPointDbContext db, string action, int? actorUserId, string details)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Utc = _clock.UtcNow,
            Action = action,
            ActorUserId = actorUserId,
            Entity = nameof(User),
            EntityId = actorUserId?.ToString(),
            Details = details,
        });
    }
}
