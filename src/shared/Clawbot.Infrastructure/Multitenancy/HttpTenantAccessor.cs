using System.Security.Claims;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace Clawbot.Infrastructure.Multitenancy;

public sealed class HttpTenantAccessor(IHttpContextAccessor httpAccessor) : ITenantAccessor
{
    public TenantContext? Current
    {
        get
        {
            var user = httpAccessor.HttpContext?.User;
            var idClaim = user?.FindFirstValue("tenant_id");
            var slugClaim = user?.FindFirstValue("tenant_slug");
            if (Guid.TryParse(idClaim, out var id) && !string.IsNullOrWhiteSpace(slugClaim))
                return new TenantContext(id, slugClaim);
            return null;
        }
    }

    public TenantContext Require() =>
        Current ?? throw new InvalidOperationException("Tenant context is required but was not found in the current request.");
}
