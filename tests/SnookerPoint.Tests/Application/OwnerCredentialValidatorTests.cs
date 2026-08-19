using SnookerPoint.Application.Setup;

namespace SnookerPoint.Tests.Application;

public class OwnerCredentialValidatorTests
{
    private static List<string> Validate(
        string password = "secret123",
        string confirmPassword = "secret123",
        string? pin = null,
        string? confirmPin = null,
        string displayName = "The Owner",
        string username = "owner") =>
        OwnerCredentialValidator.Validate(displayName, username, password, confirmPassword, pin, confirmPin);

    [Fact]
    public void ValidInputs_ProduceNoErrors()
    {
        Assert.Empty(Validate());
    }

    [Fact]
    public void PasswordConfirmationMismatch_IsRejected()
    {
        var errors = Validate(password: "secret123", confirmPassword: "secret124");
        Assert.Contains(errors, e => e.Contains("confirmation do not match"));
    }

    [Fact]
    public void EmptyPassword_IsRejected()
    {
        var errors = Validate(password: string.Empty, confirmPassword: string.Empty);
        Assert.Contains(errors, e => e.Contains("at least"));
    }

    [Fact]
    public void ShortPassword_IsRejected()
    {
        var errors = Validate(password: "123", confirmPassword: "123");
        Assert.Contains(errors, e => e.Contains("at least"));
    }

    [Fact]
    public void ValidPin_WithMatchingConfirmation_IsAccepted()
    {
        Assert.Empty(Validate(pin: "1234", confirmPin: "1234"));
    }

    [Fact]
    public void PinConfirmationMismatch_IsRejected()
    {
        var errors = Validate(pin: "1234", confirmPin: "4321");
        Assert.Contains(errors, e => e.Contains("PIN and confirmation do not match"));
    }

    [Fact]
    public void NonNumericPin_IsRejected()
    {
        var errors = Validate(pin: "12ab", confirmPin: "12ab");
        Assert.Contains(errors, e => e.Contains("digits only"));
    }

    [Fact]
    public void EmptyDisplayNameOrUsername_IsRejected()
    {
        Assert.Contains(Validate(displayName: "").ToList(), e => e.Contains("display name"));
        Assert.Contains(Validate(username: "").ToList(), e => e.Contains("username"));
    }
}
