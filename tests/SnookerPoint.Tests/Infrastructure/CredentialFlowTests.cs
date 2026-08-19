using SnookerPoint.Application.Authentication;
using SnookerPoint.Tests.TestSupport;

namespace SnookerPoint.Tests.Infrastructure;

/// <summary>
/// End-to-end credential pipeline: the exact password/PIN the user enters at setup
/// must be hashed, stored, and then accepted at login (the defect that was fixed).
/// </summary>
public class CredentialFlowTests
{
    [Fact]
    public void Setup_StoresHash_ThatVerifiesTheExactPassword()
    {
        using var env = new Phase1Environment();
        const string password = "P@ssw0rd!";

        Assert.True(env.Setup.CompleteSetup(
            SetupRequests.Valid(username: "owner", password: password, pin: "2468")).Succeeded);

        using var db = env.NewContext();
        var owner = db.Users.Single();

        Assert.True(env.Hasher.Verify(password, owner.PasswordHash).IsValid);
        Assert.False(env.Hasher.Verify("not-the-password", owner.PasswordHash).IsValid);
    }

    [Fact]
    public void Setup_StoresPinHash_ThatVerifiesTheExactPin()
    {
        using var env = new Phase1Environment();
        const string pin = "2468";

        Assert.True(env.Setup.CompleteSetup(
            SetupRequests.Valid(username: "owner", password: "secret123", pin: pin)).Succeeded);

        using var db = env.NewContext();
        var owner = db.Users.Single();

        Assert.NotNull(owner.PinHash);
        Assert.True(env.Hasher.Verify(pin, owner.PinHash!).IsValid);
        Assert.False(env.Hasher.Verify("0000", owner.PinHash!).IsValid);
    }

    [Fact]
    public void PasswordLogin_SucceedsWithCorrect_FailsWithWrong()
    {
        using var env = new Phase1Environment();
        const string password = "P@ssw0rd!";
        env.Setup.CompleteSetup(SetupRequests.Valid(username: "owner", password: password));

        Assert.True(env.Auth.LoginWithPassword("owner", password).Succeeded);
        Assert.False(env.Auth.LoginWithPassword("owner", "wrong-one").Succeeded);
    }

    [Fact]
    public void PinLogin_SucceedsWithCorrect_FailsWithWrong()
    {
        using var env = new Phase1Environment();
        env.Setup.CompleteSetup(SetupRequests.Valid(username: "owner", password: "secret123", pin: "2468"));

        Assert.True(env.Auth.LoginWithPin("owner", "2468").Succeeded);
        Assert.Equal(LoginFailureReason.InvalidCredentials, env.Auth.LoginWithPin("owner", "1111").Reason);
    }

    [Fact]
    public void UsernameMatching_IsCaseInsensitive()
    {
        using var env = new Phase1Environment();
        env.Setup.CompleteSetup(SetupRequests.Valid(username: "Owner", password: "secret123"));

        Assert.True(env.Auth.LoginWithPassword("OWNER", "secret123").Succeeded);
        Assert.True(env.Auth.LoginWithPassword("owner", "secret123").Succeeded);
    }
}
