using SnookerPoint.Application.Common;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Application.Staff;

/// <summary>A staff account as shown on the Staff Management screen. Never carries secrets.</summary>
public sealed record StaffListItem(
    int Id,
    string DisplayName,
    string Username,
    UserRole Role,
    bool IsActive,
    bool HasPin,
    bool IsLockedOut,
    DateTimeOffset? LockedOutUntilUtc,
    bool IsLastActiveOwner);

/// <summary>Details to create a new staff account.</summary>
public sealed record CreateStaffRequest(
    string DisplayName,
    string Username,
    UserRole Role,
    string Password,
    string? Pin);

/// <summary>Details to edit an existing account's display name, username and role.</summary>
public sealed record UpdateStaffRequest(
    int UserId,
    string DisplayName,
    string Username,
    UserRole Role);

/// <summary>
/// Manages staff accounts for Owner/Administrator users. Enforces the domain safety
/// rules (only an Owner may create or promote another Owner; the last active Owner can
/// never be disabled or demoted), unique case-insensitive usernames, secure hashing,
/// and audits every account change. Passwords and PINs are never returned or logged.
/// </summary>
public interface IStaffManagementService
{
    IReadOnlyList<StaffListItem> GetAll();

    OperationResult<int> CreateStaff(CreateStaffRequest request, int actorUserId);

    OperationResult UpdateStaff(UpdateStaffRequest request, int actorUserId);

    /// <summary>Sets (resets) the account password.</summary>
    OperationResult SetPassword(int userId, string newPassword, int actorUserId);

    /// <summary>
    /// Generates a temporary password, sets it on the account, and requires the user to
    /// change it at next login. Returns the temporary password once (never stored plainly).
    /// </summary>
    OperationResult<string> GenerateTemporaryPassword(int userId, int actorUserId);

    /// <summary>Sets, changes or (when <paramref name="newPin"/> is null/blank) removes the PIN.</summary>
    OperationResult SetPin(int userId, string? newPin, int actorUserId);

    /// <summary>Enables or disables the account.</summary>
    OperationResult SetActive(int userId, bool active, int actorUserId);

    /// <summary>Clears any active lockout and resets the failed-attempt counter.</summary>
    OperationResult ClearLockout(int userId, int actorUserId);
}
