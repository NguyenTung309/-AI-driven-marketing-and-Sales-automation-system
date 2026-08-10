using Grpc.Core;

namespace Clawbot.AgentService.Services;

public sealed record OrchestratorCaller(
    Guid TenantId,
    Guid UserId,
    IReadOnlySet<string> Permissions);

public interface IOrchestratorCallerAuthorizer : IOrchestratorPermissionResolver
{
    Task<OrchestratorCaller> AuthorizeAsync(
        ServerCallContext context,
        string requestedTenantId,
        string? requestedUserId,
        string requiredPermission,
        CancellationToken cancellationToken = default);

}
