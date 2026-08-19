using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Domain.Security;

/// <summary>
/// Domain rules that protect the club from being locked out of its own system.
/// The last active Owner must never be silently deleted or disabled, so at least
/// one Owner can always sign in. Staff-management screens (a later phase) enforce
/// these rules; the policy lives here so it is testable and reused everywhere.
/// </summary>
public static class AccountProtection
{
    /// <summary>The number of currently active Owner accounts in the set.</summary>
    public static int ActiveOwnerCount(IEnumerable<User> users) =>
        users.Count(u => u.Role == UserRole.Owner && u.IsActive);

    /// <summary>True when this user is the only active Owner remaining.</summary>
    public static bool IsLastActiveOwner(User user, IEnumerable<User> users) =>
        user.Role == UserRole.Owner && user.IsActive && ActiveOwnerCount(users) <= 1;

    /// <summary>False when deactivating this user would remove the last active Owner.</summary>
    public static bool CanDeactivate(User user, IEnumerable<User> users) =>
        !IsLastActiveOwner(user, users);

    /// <summary>False when deleting this user would remove the last active Owner.</summary>
    public static bool CanDelete(User user, IEnumerable<User> users) =>
        !IsLastActiveOwner(user, users);
}
