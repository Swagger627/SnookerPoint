using SnookerPoint.Application.Security;

namespace SnookerPoint.App.Services;

/// <summary>
/// What the "Forgot password or PIN?" dialog returns when an Owner chooses to recover
/// their account offline. Null result means the user just read the guidance and closed.
/// </summary>
public sealed record ForgotRecoveryInput(
    string Username,
    string RecoveryCode,
    string NewPassword,
    string? NewPin);

/// <summary>Context for the forgot-password dialog (whether Owner recovery is available).</summary>
public sealed record ForgotPasswordContext(OwnerRecoveryStatus Status);
