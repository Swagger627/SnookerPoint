using SnookerPoint.Application.Common;

namespace SnookerPoint.Application.Security;

/// <summary>
/// Lets a signed-in user manage their own credentials. Every change requires the
/// current password, validates the new secret, replaces the hash only after validation
/// succeeds, and is audited without ever recording the secret. Never returns secrets.
/// </summary>
public interface IAccountSecurityService
{
    /// <summary>Changes the user's own password after verifying the current one.</summary>
    OperationResult ChangePassword(int userId, string currentPassword, string newPassword);

    /// <summary>Adds or changes the user's own PIN after verifying the current password.</summary>
    OperationResult ChangePin(int userId, string currentPassword, string newPin);

    /// <summary>Removes the user's own PIN after verifying the current password.</summary>
    OperationResult RemovePin(int userId, string currentPassword);
}
