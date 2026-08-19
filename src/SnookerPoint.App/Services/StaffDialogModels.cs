using SnookerPoint.Domain.Enums;

namespace SnookerPoint.App.Services;

/// <summary>What the staff editor dialog needs to open in create or edit mode.</summary>
public sealed record StaffEditContext(
    bool IsNew,
    string DisplayName,
    string Username,
    UserRole Role,
    IReadOnlyList<UserRole> RoleOptions);

/// <summary>The staff editor's result. Password/PIN are only populated when creating.</summary>
public sealed record StaffEditInput(
    string DisplayName,
    string Username,
    UserRole Role,
    string? Password,
    string? Pin);

/// <summary>What the credential dialog needs (reset password, or set/remove PIN).</summary>
public sealed record SetCredentialContext(bool IsPin, string StaffName);

/// <summary>
/// The credential dialog's result. For a PIN, a null <see cref="Value"/> means "remove
/// the PIN". For a password, the value is always the new password.
/// </summary>
public sealed record SetCredentialInput(string? Value);
