namespace Clawbot.Infrastructure.Auth;

/// <summary>
/// SPEC-11 Phương án A — resolves a role's permission codes at runtime (Redis cache →
/// role_permissions fallback) so changing a role's permissions takes effect immediately.
/// </summary>
public interface IPermissionResolver
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default);

    /// <summary>Drop the cached permissions for a role (call after editing role_permissions).</summary>
    Task InvalidateAsync(Guid roleId, CancellationToken ct = default);
}
