using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SnookerPoint.Application.Abstractions;
using SnookerPoint.Application.Common;
using SnookerPoint.Application.Security;
using SnookerPoint.Application.Staff;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Security;
using SnookerPoint.Infrastructure.Persistence;
using SnookerPoint.Infrastructure.Security;

namespace SnookerPoint.Infrastructure.Services;

/// <summary>
/// Manages staff accounts. Requires the <see cref="Permission.ManageStaff"/> capability
/// (Owner/Administrator). Enforces the domain protections — only an Owner may create,
/// promote to, or change an Owner account, and the last active Owner can never be
/// disabled or demoted — plus unique case-insensitive usernames. Secrets are hashed via
/// <see cref="ISecretHasher"/> and never returned or logged; every change is audited.
/// </summary>
public sealed class StaffManagementService : IStaffManagementService
{
    private readonly IDbContextFactory<SnookerPointDbContext> _factory;
    private readonly ISecretHasher _hasher;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly ILogger<StaffManagementService> _logger;

    public StaffManagementService(
        IDbContextFactory<SnookerPointDbContext> factory,
        ISecretHasher hasher,
        IPermissionService permissions,
        IClock clock,
        ILogger<StaffManagementService> logger)
    {
        _factory = factory;
        _hasher = hasher;
        _permissions = permissions;
        _clock = clock;
        _logger = logger;
    }

    public IReadOnlyList<StaffListItem> GetAll()
    {
        using var db = _factory.CreateDbContext();
        var users = db.Users.AsNoTracking().ToList();
        var now = _clock.UtcNow;

        return users
            .OrderBy(u => u.Role)
            .ThenBy(u => u.DisplayName)
            .Select(u => new StaffListItem(
                u.Id,
                u.DisplayName,
                u.Username,
                u.Role,
                u.IsActive,
                u.HasPin,
                u.IsLockedOut(now),
                u.IsLockedOut(now) ? u.LockedOutUntilUtc : null,
                AccountProtection.IsLastActiveOwner(u, users)))
            .ToList();
    }

    public OperationResult<int> CreateStaff(CreateStaffRequest request, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var actor = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (actor is null || !_permissions.HasPermission(actor, Permission.ManageStaff))
        {
            return OperationResult<int>.Failure("You do not have permission to manage staff.");
        }

        var username = Normalize(request.Username);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors.Add("Please enter a display name.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            errors.Add("Please enter a username.");
        }
        else if (db.Users.Any(u => u.Username == username))
        {
            errors.Add("That username is already taken. Please choose another.");
        }

        if (request.Role == UserRole.Owner && actor.Role != UserRole.Owner)
        {
            errors.Add("Only an Owner can create another Owner account.");
        }

        if (StaffCredentialRules.ValidatePassword(request.Password) is { } pwdError)
        {
            errors.Add(pwdError);
        }

        if (StaffCredentialRules.ValidatePin(request.Pin) is { } pinError)
        {
            errors.Add(pinError);
        }

        if (errors.Count > 0)
        {
            return OperationResult<int>.Failure(errors);
        }

        var now = _clock.UtcNow;
        var user = new User
        {
            DisplayName = request.DisplayName.Trim(),
            Username = username,
            Role = request.Role,
            PasswordHash = _hasher.Hash(request.Password),
            PinHash = string.IsNullOrEmpty(request.Pin) ? null : _hasher.Hash(request.Pin!),
            IsActive = true,
            CreatedUtc = now,
            UpdatedUtc = now,
        };
        db.Users.Add(user);
        db.SaveChanges();

        WriteAudit(db, AuditActions.StaffCreated, actorUserId, user.Id,
            $"Staff account '{user.Username}' ({DescribeRole(user.Role)}) created.");
        db.SaveChanges();

        return OperationResult<int>.Success(user.Id);
    }

    public OperationResult UpdateStaff(UpdateStaffRequest request, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var guard = Authorize(db, actorUserId, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == request.UserId);
        if (user is null)
        {
            return OperationResult.Failure("That staff account was not found.");
        }

        // An Administrator may not alter an existing Owner account.
        if (GuardOwnerTarget(actor, user) is { } ownerGuard)
        {
            return ownerGuard;
        }

        var username = Normalize(request.Username);
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            errors.Add("Please enter a display name.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            errors.Add("Please enter a username.");
        }
        else if (db.Users.Any(u => u.Username == username && u.Id != user.Id))
        {
            errors.Add("That username is already taken. Please choose another.");
        }

        var roleChanged = user.Role != request.Role;
        var touchesOwner = user.Role == UserRole.Owner || request.Role == UserRole.Owner;
        if (roleChanged && touchesOwner && actor.Role != UserRole.Owner)
        {
            errors.Add("Only an Owner can assign or change an Owner account.");
        }

        if (roleChanged && request.Role != UserRole.Owner)
        {
            var users = db.Users.ToList();
            if (AccountProtection.IsLastActiveOwner(user, users))
            {
                errors.Add("This is the last active Owner and cannot be demoted.");
            }
        }

        if (errors.Count > 0)
        {
            return OperationResult.Failure(errors);
        }

        var now = _clock.UtcNow;
        var changes = new List<string>();

        if (!string.Equals(user.DisplayName, request.DisplayName.Trim(), StringComparison.Ordinal))
        {
            changes.Add($"name '{user.DisplayName}' → '{request.DisplayName.Trim()}'");
            user.DisplayName = request.DisplayName.Trim();
        }

        if (!string.Equals(user.Username, username, StringComparison.Ordinal))
        {
            changes.Add($"username '{user.Username}' → '{username}'");
            user.Username = username;
        }

        var oldRole = user.Role;
        if (roleChanged)
        {
            user.Role = request.Role;
        }

        if (changes.Count == 0 && !roleChanged)
        {
            return OperationResult.Success();
        }

        user.UpdatedUtc = now;

        if (changes.Count > 0)
        {
            WriteAudit(db, AuditActions.StaffUpdated, actorUserId, user.Id,
                $"Staff account '{user.Username}' updated: {string.Join(", ", changes)}.");
        }

        if (roleChanged)
        {
            WriteAudit(db, AuditActions.StaffRoleChanged, actorUserId, user.Id,
                $"Staff account '{user.Username}' role {DescribeRole(oldRole)} → {DescribeRole(user.Role)}.");
        }

        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult SetPassword(int userId, string newPassword, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var guard = Authorize(db, actorUserId, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("That staff account was not found.");
        }

        if (GuardOwnerTarget(actor, user) is { } ownerGuard)
        {
            return ownerGuard;
        }

        if (StaffCredentialRules.ValidatePassword(newPassword) is { } error)
        {
            return OperationResult.Failure(error);
        }

        var now = _clock.UtcNow;
        user.PasswordHash = _hasher.Hash(newPassword);
        user.UpdatedUtc = now;

        WriteAudit(db, AuditActions.StaffPasswordReset, actorUserId, user.Id,
            $"Password reset for '{user.Username}'.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult<string> GenerateTemporaryPassword(int userId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var user = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (user is null || !_permissions.HasPermission(user, Permission.ManageStaff))
        {
            return OperationResult<string>.Failure("You do not have permission to manage staff.");
        }

        var target = db.Users.FirstOrDefault(u => u.Id == userId);
        if (target is null)
        {
            return OperationResult<string>.Failure("That staff account was not found.");
        }

        if (GuardOwnerTarget(user, target) is { } ownerGuard)
        {
            return OperationResult<string>.Failure(ownerGuard.ErrorMessage);
        }

        var temp = TemporaryPasswordGenerator.Generate();
        var now = _clock.UtcNow;
        target.PasswordHash = _hasher.Hash(temp);
        target.MustChangePassword = true;
        target.UpdatedUtc = now;

        WriteAudit(db, AuditActions.StaffTemporaryPasswordIssued, actorUserId, target.Id,
            $"Temporary password issued for '{target.Username}' (must change at next login).");
        db.SaveChanges();
        return OperationResult<string>.Success(temp);
    }

    public OperationResult SetPin(int userId, string? newPin, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var guard = Authorize(db, actorUserId, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("That staff account was not found.");
        }

        if (GuardOwnerTarget(actor, user) is { } ownerGuard)
        {
            return ownerGuard;
        }

        if (StaffCredentialRules.ValidatePin(newPin) is { } error)
        {
            return OperationResult.Failure(error);
        }

        var now = _clock.UtcNow;
        var removing = string.IsNullOrEmpty(newPin);
        user.PinHash = removing ? null : _hasher.Hash(newPin!);
        user.UpdatedUtc = now;

        WriteAudit(db,
            removing ? AuditActions.StaffPinRemoved : AuditActions.StaffPinChanged,
            actorUserId, user.Id,
            removing ? $"PIN removed for '{user.Username}'." : $"PIN set for '{user.Username}'.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult SetActive(int userId, bool active, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var guard = Authorize(db, actorUserId, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("That staff account was not found.");
        }

        if (GuardOwnerTarget(actor, user) is { } ownerGuard)
        {
            return ownerGuard;
        }

        if (user.IsActive == active)
        {
            return OperationResult.Success();
        }

        if (!active)
        {
            var users = db.Users.ToList();
            if (!AccountProtection.CanDeactivate(user, users))
            {
                return OperationResult.Failure("This is the last active Owner and cannot be disabled.");
            }
        }

        var now = _clock.UtcNow;
        user.IsActive = active;
        user.UpdatedUtc = now;

        WriteAudit(db,
            active ? AuditActions.StaffEnabled : AuditActions.StaffDisabled,
            actorUserId, user.Id,
            $"Staff account '{user.Username}' {(active ? "enabled" : "disabled")}.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    public OperationResult ClearLockout(int userId, int actorUserId)
    {
        using var db = _factory.CreateDbContext();

        var guard = Authorize(db, actorUserId, out var actor);
        if (guard is not null)
        {
            return guard;
        }

        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            return OperationResult.Failure("That staff account was not found.");
        }

        if (GuardOwnerTarget(actor, user) is { } ownerGuard)
        {
            return ownerGuard;
        }

        var now = _clock.UtcNow;
        user.LockedOutUntilUtc = null;
        user.FailedLoginAttempts = 0;
        user.UpdatedUtc = now;

        WriteAudit(db, AuditActions.StaffLockoutCleared, actorUserId, user.Id,
            $"Lockout cleared for '{user.Username}'.");
        db.SaveChanges();
        return OperationResult.Success();
    }

    private OperationResult? Authorize(SnookerPointDbContext db, int actorUserId, out User actor)
    {
        actor = null!;
        var user = db.Users.FirstOrDefault(u => u.Id == actorUserId);
        if (user is null || !_permissions.HasPermission(user, Permission.ManageStaff))
        {
            return OperationResult.Failure("You do not have permission to manage staff.");
        }

        actor = user;
        return null;
    }

    /// <summary>
    /// Only an Owner may reset, disable or alter another Owner account. An Administrator
    /// (or Manager) is refused when the target is an Owner.
    /// </summary>
    private static OperationResult? GuardOwnerTarget(User actor, User target) =>
        target.Role == UserRole.Owner && actor.Role != UserRole.Owner
            ? OperationResult.Failure("Only an Owner can reset or change another Owner account.")
            : null;

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

    private static string Normalize(string? username) =>
        (username ?? string.Empty).Trim().ToLowerInvariant();

    private static string DescribeRole(UserRole role) => role switch
    {
        UserRole.Owner => "Owner",
        UserRole.Administrator => "Administrator",
        UserRole.Manager => "Manager",
        UserRole.Cashier => "Cashier",
        UserRole.FloorStaff => "Floor Staff",
        _ => role.ToString(),
    };
}
