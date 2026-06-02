using System.Diagnostics.CodeAnalysis;

namespace Clawbot.Domain.Security;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Domain term — link table between RBAC Role and Permission.")]
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
