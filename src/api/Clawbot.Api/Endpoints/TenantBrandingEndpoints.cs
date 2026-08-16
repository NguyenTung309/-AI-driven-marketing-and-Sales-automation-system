using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Tenants;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.SharedKernel.Multitenancy;

namespace Clawbot.Api.Endpoints;

public static class TenantBrandingEndpoints
{
    public static IEndpointRouteBuilder MapTenantBranding(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/tenant/branding")
            .RequirePermission("system:config")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        group.MapGet("/", GetAsync);
        group.MapPut("/", UpdateAsync);

        return app;
    }

    private static async Task<IResult> GetAsync(
        ITenantAccessor tenants,
        TenantBrandingService branding,
        CancellationToken ct)
    {
        var result = await branding.GetAsync(tenants.Require().TenantId, ct).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateAsync(
        UpdateTenantBrandingRequest request,
        ITenantAccessor tenants,
        TenantBrandingService branding,
        CancellationToken ct)
    {
        try
        {
            var result = await branding.UpdateAsync(tenants.Require().TenantId, request, ct).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}
