namespace Clawbot.AgentService.Services;

public interface IOrchestratorPermissionResolver
{
    Task<IReadOnlySet<string>> ResolvePermissionsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
