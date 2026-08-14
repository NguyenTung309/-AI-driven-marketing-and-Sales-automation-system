using System.Security.Claims;
using Grpc.Core;

namespace Clawbot.Infrastructure.Security;

public static class AgentServiceTenantBinding
{
    public static Guid ReadRequiredRequestTenantId(object request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantProperty = request.GetType().GetProperty("TenantId");
        var value = tenantProperty?.GetValue(request) as string;
        if (!Guid.TryParse(value, out var tenantId) || tenantId == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "agent_service_tenant_required"));
        }

        return tenantId;
    }

    public static void EnsurePrincipalMatchesRequest(
        ClaimsPrincipal principal,
        object request)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(request);
        var requestTenantId = ReadRequiredRequestTenantId(request);
        if (!Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var principalTenantId) ||
            principalTenantId == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.Unauthenticated,
                "agent_service_caller_required"));
        }

        if (principalTenantId != requestTenantId)
        {
            throw new RpcException(new Status(
                StatusCode.PermissionDenied,
                "agent_service_tenant_mismatch"));
        }
    }
}
