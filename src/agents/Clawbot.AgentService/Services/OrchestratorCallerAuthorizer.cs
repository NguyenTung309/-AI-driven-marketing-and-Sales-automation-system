using System.Security.Claims;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed class OrchestratorCallerAuthorizer(
    AppDbContext db,
    IPermissionResolver permissionResolver) : IOrchestratorCallerAuthorizer
{
    public async Task<OrchestratorCaller> AuthorizeAsync(
        ServerCallContext context,
        string requestedTenantId,
        string? requestedUserId,
        string requiredPermission,
        CancellationToken cancellationToken = default)
    {
        var principal = context.GetHttpContext().User;
        var tenantId = ReadClaimGuid(principal, "tenant_id");
        var userId = ReadClaimGuid(principal, ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(requestedTenantId, out var requestedTenant)
            || requestedTenant != tenantId)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "tenant_mismatch"));
        }

        if (!string.IsNullOrWhiteSpace(requestedUserId)
            && (!Guid.TryParse(requestedUserId, out var requestedUser)
                || requestedUser != userId))
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "user_mismatch"));
        }

        // The JWT carries exactly one role_id — the role the API session was issued for.
        // Resolving from the claim is correct and avoids granting the union of every role the
        // account holds (the original bug: a multi-role user got wider authority in the agent
        // service than the API's own permission gate would have given them).
        if (!Guid.TryParse(principal.FindFirst("role_id")?.Value, out var callerRoleId)
            || callerRoleId == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, "orchestrator_caller_role_missing"));
        }

        var permissions = await permissionResolver
            .GetPermissionsAsync(callerRoleId, cancellationToken)
            .ConfigureAwait(false);
        if (!permissions.Contains(requiredPermission))
            throw new RpcException(new Status(StatusCode.PermissionDenied, "permission_denied"));

        return new OrchestratorCaller(tenantId, userId, permissions);
    }

    // Used by background contexts (AgentScheduleRunner, OrchestratorGrpcService background re-execution)
    // where no HTTP JWT is present. Background callers legitimately act on behalf of the user's
    // current active roles, so the union query is intentional here.
    public async Task<IReadOnlySet<string>> ResolvePermissionsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId
                && candidate.TenantId == tenantId
                && candidate.IsActive, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
            throw new RpcException(new Status(StatusCode.Unauthenticated, "orchestrator_caller_inactive"));

        var roleIds = await db.UserRoles.AsNoTracking()
            .Where(link => link.UserId == userId)
            .Select(link => link.RoleId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var permissions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var roleId in roleIds)
            permissions.UnionWith(await permissionResolver.GetPermissionsAsync(roleId, cancellationToken)
                .ConfigureAwait(false));

        return permissions;
    }

    private static Guid ReadClaimGuid(ClaimsPrincipal principal, string claimType)
    {
        if (!Guid.TryParse(principal.FindFirst(claimType)?.Value, out var value)
            || value == Guid.Empty)
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "orchestrator_caller_invalid"));
        }

        return value;
    }
}
