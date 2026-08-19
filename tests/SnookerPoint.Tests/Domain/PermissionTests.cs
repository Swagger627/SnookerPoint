using SnookerPoint.Application.Security;
using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Tests.Domain;

public class PermissionTests
{
    private readonly PermissionService _permissions = new();

    [Fact]
    public void Owner_CanAccessAdvancedMode_AndAudit()
    {
        Assert.True(_permissions.HasPermission(UserRole.Owner, Permission.AccessAdvancedMode));
        Assert.True(_permissions.HasPermission(UserRole.Owner, Permission.ViewAuditLog));
    }

    [Fact]
    public void Cashier_CanRunShift_ButNotAdvancedMode()
    {
        Assert.True(_permissions.HasPermission(UserRole.Cashier, Permission.OpenShift));
        Assert.True(_permissions.HasPermission(UserRole.Cashier, Permission.RecordCashMovement));
        Assert.False(_permissions.HasPermission(UserRole.Cashier, Permission.AccessAdvancedMode));
    }

    [Fact]
    public void InactiveUser_HasNoPermissions()
    {
        var user = new User { Role = UserRole.Owner, IsActive = false };

        Assert.False(_permissions.HasPermission(user, Permission.OpenShift));
    }
}
