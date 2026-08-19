using SnookerPoint.Application.Setup;

namespace SnookerPoint.Application.Staff;

/// <summary>
/// Pure validation for staff credentials, reusing the same length/format rules as
/// first-run setup (<see cref="SetupRules"/>) so passwords and PINs are consistent
/// everywhere. Centralised here so the service and UI share one source of truth.
/// </summary>
public static class StaffCredentialRules
{
    /// <summary>Returns an error message for an invalid password, or null when valid.</summary>
    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < SetupRules.MinPasswordLength)
        {
            return $"The password must be at least {SetupRules.MinPasswordLength} characters.";
        }

        return null;
    }

    /// <summary>
    /// Returns an error message for an invalid PIN, or null when valid. A null/blank PIN
    /// is valid (it means "no PIN"); callers decide whether that is allowed.
    /// </summary>
    public static string? ValidatePin(string? pin)
    {
        if (string.IsNullOrEmpty(pin))
        {
            return null;
        }

        if (!pin.All(char.IsDigit))
        {
            return "The PIN must contain digits only.";
        }

        if (pin.Length < SetupRules.MinPinLength || pin.Length > SetupRules.MaxPinLength)
        {
            return $"The PIN must be {SetupRules.MinPinLength} to {SetupRules.MaxPinLength} digits.";
        }

        return null;
    }
}
