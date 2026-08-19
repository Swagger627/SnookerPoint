using SnookerPoint.Application.Authentication;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

public class AuthenticationServiceTests
{
    private static Phase1Environment SetUpWithOwner(string pin = "1234")
    {
        var env = new Phase1Environment();
        var result = env.Setup.CompleteSetup(
            SetupRequests.Valid(username: "owner", password: "secret123", pin: pin));
        Assert.True(result.Succeeded, result.ErrorMessage);
        return env;
    }

    [Fact]
    public void LoginWithPassword_Correct_Succeeds()
    {
        using var env = SetUpWithOwner();

        var result = env.Auth.LoginWithPassword("owner", "secret123");

        Assert.True(result.Succeeded);
        Assert.NotNull(result.User);
        Assert.Equal("owner", result.User!.Username);
    }

    [Fact]
    public void LoginWithPassword_IsCaseInsensitiveOnUsername()
    {
        using var env = SetUpWithOwner();

        var result = env.Auth.LoginWithPassword("OWNER", "secret123");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void LoginWithPassword_Wrong_Fails()
    {
        using var env = SetUpWithOwner();

        var result = env.Auth.LoginWithPassword("owner", "wrongpassword");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.Reason);
    }

    [Fact]
    public void LoginWithPassword_UnknownUser_Fails()
    {
        using var env = SetUpWithOwner();

        var result = env.Auth.LoginWithPassword("nobody", "secret123");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.Reason);
    }

    [Fact]
    public void LoginWithPin_Correct_Succeeds()
    {
        using var env = SetUpWithOwner(pin: "4321");

        var result = env.Auth.LoginWithPin("owner", "4321");

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void LoginWithPin_Wrong_Fails()
    {
        using var env = SetUpWithOwner(pin: "4321");

        var result = env.Auth.LoginWithPin("owner", "0000");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, result.Reason);
    }

    [Fact]
    public void DisabledUser_CannotLogIn()
    {
        using var env = SetUpWithOwner();

        using (var db = env.NewContext())
        {
            var owner = db.Users.Single();
            owner.IsActive = false;
            db.SaveChanges();
        }

        var result = env.Auth.LoginWithPassword("owner", "secret123");

        Assert.False(result.Succeeded);
        Assert.Equal(LoginFailureReason.AccountDisabled, result.Reason);
    }

    [Fact]
    public void RepeatedWrongPassword_LocksAccount()
    {
        using var env = SetUpWithOwner();

        LoginResult last = LoginResult.Failure(LoginFailureReason.InvalidCredentials);
        for (var i = 0; i < AccountSecurityPolicy.MaxFailedAttempts; i++)
        {
            last = env.Auth.LoginWithPassword("owner", "wrongpassword");
        }

        Assert.Equal(LoginFailureReason.AccountLockedOut, last.Reason);

        // Even the correct password is refused while locked out.
        var correctWhileLocked = env.Auth.LoginWithPassword("owner", "secret123");
        Assert.Equal(LoginFailureReason.AccountLockedOut, correctWhileLocked.Reason);
    }

    [Fact]
    public void Lockout_Expires_AfterWaiting()
    {
        using var env = SetUpWithOwner();

        for (var i = 0; i < AccountSecurityPolicy.MaxFailedAttempts; i++)
        {
            env.Auth.LoginWithPassword("owner", "wrongpassword");
        }

        env.Clock.Advance(AccountSecurityPolicy.LockoutDuration + TimeSpan.FromSeconds(1));

        var result = env.Auth.LoginWithPassword("owner", "secret123");
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void SuccessfulLogin_ResetsFailedAttempts()
    {
        using var env = SetUpWithOwner();

        env.Auth.LoginWithPassword("owner", "wrongpassword");
        env.Auth.LoginWithPassword("owner", "wrongpassword");
        Assert.True(env.Auth.LoginWithPassword("owner", "secret123").Succeeded);

        using var db = env.NewContext();
        Assert.Equal(0, db.Users.Single().FailedLoginAttempts);
    }
}
