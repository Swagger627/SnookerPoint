using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;
using SnookerPoint.Domain.Security;

namespace SnookerPoint.Application.Security;

/// <summary>Pure permission resolver over <see cref="RolePermissions"/>.</summary>
public sealed class PermissionService : IPermissionService
{
    public bool HasPermission(UserRole role, Permission permission) =>
        RolePermissions.Has(role, permission);

    public bool HasPermission(User user, Permission permission) =>
        user.IsActive && RolePermissions.Has(user.Role, permission);
}
