using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Security;

namespace SnookerPoint.Tests.Domain;

public class AccountProtectionTests
{
    private static User Owner(bool active = true) =>
        new() { Role = UserRole.Owner, IsActive = active, Username = "owner" };

    private static User Manager() =>
        new() { Role = UserRole.Manager, IsActive = true, Username = "manager" };

    [Fact]
    public void SoleActiveOwner_IsLastActiveOwner()
    {
        var owner = Owner();
        var users = new[] { owner, Manager() };

        Assert.True(AccountProtection.IsLastActiveOwner(owner, users));
        Assert.False(AccountProtection.CanDeactivate(owner, users));
        Assert.False(AccountProtection.CanDelete(owner, users));
    }

    [Fact]
    public void OneOfTwoActiveOwners_CanBeDeactivated()
    {
        var owner1 = Owner();
        var owner2 = Owner();
        var users = new[] { owner1, owner2 };

        Assert.False(AccountProtection.IsLastActiveOwner(owner1, users));
        Assert.True(AccountProtection.CanDeactivate(owner1, users));
    }

    [Fact]
    public void NonOwner_IsNeverTheLastActiveOwner()
    {
        var manager = Manager();
        var users = new[] { Owner(), manager };

        Assert.False(AccountProtection.IsLastActiveOwner(manager, users));
        Assert.True(AccountProtection.CanDeactivate(manager, users));
    }
}
