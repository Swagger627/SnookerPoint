using SnookerPoint.Domain.Entities;
using SnookerPoint.Domain.Enums;

namespace SnookerPoint.Application.Security;

/// <summary>
/// Answers "is this role/user allowed to do X?" so screens never test role names
/// directly. Backed by the domain's <see cref="Domain.Security.RolePermissions"/>.
/// </summary>
public interface IPermissionService
{
    bool HasPermission(UserRole role, Permission permission);

    /// <summary>True only when the user is active and their role grants the permission.</summary>
    bool HasPermission(User user, Permission permission);
}
