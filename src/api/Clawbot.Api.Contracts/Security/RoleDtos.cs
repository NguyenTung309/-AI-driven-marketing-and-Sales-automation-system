namespace Clawbot.Api.Contracts.Security;

public sealed record RoleDto(Guid Id, string Name, string? Description, bool IsSystem);

public sealed record CreateRoleRequest(string Name, string? Description);

public sealed record UpdateRoleRequest(string Name, string? Description);

public sealed record PermissionDto(Guid Id, string Code, string? Description);

public sealed record SetRolePermissionsRequest(IReadOnlyList<Guid> PermissionIds);
