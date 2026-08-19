namespace SnookerPoint.Application.Setup;

/// <summary>
/// Pure validation for the owner-account step (display name, username, password +
/// confirmation, optional PIN + confirmation). Centralised here so the same rules
/// are enforced by the wizard and can be unit-tested without the UI.
/// </summary>
public static class OwnerCredentialValidator
{
    public static List<string> Validate(
        string? displayName,
        string? username,
        string? password,
        string? confirmPassword,
        string? pin,
        string? confirmPin)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(displayName))
        {
            errors.Add("Please enter the owner's display name.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            errors.Add("Please enter a username.");
        }

        var pwd = password ?? string.Empty;
        if (pwd.Length < SetupRules.MinPasswordLength)
        {
            errors.Add($"The password must be at least {SetupRules.MinPasswordLength} characters.");
        }

        if (!string.Equals(pwd, confirmPassword ?? string.Empty, StringComparison.Ordinal))
        {
            errors.Add("The password and confirmation do not match.");
        }

        if (!string.IsNullOrEmpty(pin))
        {
            if (!pin.All(char.IsDigit))
            {
                errors.Add("The PIN must contain digits only.");
            }
            else if (pin.Length < SetupRules.MinPinLength || pin.Length > SetupRules.MaxPinLength)
            {
                errors.Add($"The PIN must be {SetupRules.MinPinLength} to {SetupRules.MaxPinLength} digits.");
            }

            if (!string.Equals(pin, confirmPin ?? string.Empty, StringComparison.Ordinal))
            {
                errors.Add("The PIN and confirmation do not match.");
            }
        }

        return errors;
    }
}
