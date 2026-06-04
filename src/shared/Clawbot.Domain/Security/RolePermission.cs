using System.Diagnostics.CodeAnalysis;

namespace Clawbot.Domain.Security;

// role_permissions — many-to-many join between roles and permissions.
// Composite PK (role_id, permission_id); not an aggregate root.
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Domain term — RBAC role↔permission join entity.")]
public sealed class RolePermission
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    private RolePermission() { }

    public static RolePermission Create(Guid roleId, Guid permissionId) =>
        new()
        {
            RoleId = roleId,
            PermissionId = permissionId,
        };
}
