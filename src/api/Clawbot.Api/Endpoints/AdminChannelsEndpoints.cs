using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// SPEC-16 Module M-3/M-4: admin channel configuration — Pancake connect (list pages) + mint/store page tokens.
// Permissions: channels:manage (RbacSeeder). The admin pastes the Pancake user access token; the system lists
// pages, then mints + stores a per-page token on the inbox row (inboxes.encrypted_access_token) per selected page.
public sealed record ConnectPancakeRequest(string UserAccessToken);
public sealed record MintPancakePagesRequest(string UserAccessToken, IReadOnlyList<MintPancakePage> Pages);
public sealed record MintPancakePage(string PageId, string Name, string Platform);

public static class AdminChannelsEndpoints
{
    public static IEndpointRouteBuilder MapAdminChannelsEndpoints(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/channels")
            .RequireRateLimiting(Middleware.RateLimitingExtensions.GeneralPolicy)
            .RequirePermission("channels:manage");

        // M-3: validate the user token by listing pages (also surfaces page ids for the mint step).
        grp.MapPost("/pancake/connect", ConnectPancakeAsync);
        // M-4: mint + store a page access token per selected page.
        grp.MapPost("/pancake/pages", MintPancakePagesAsync);
        // List currently connected pages (status: connected/expired/not configured).
        grp.MapGet("/pancake/pages", ListConnectedPagesAsync);
        return app;
    }

    private static async Task<IResult> ConnectPancakeAsync(
        ConnectPancakeRequest body, IPageListGateway gateway, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.UserAccessToken))
            return Results.BadRequest(new { error = "user_access_token_required" });
        try
        {
            var pages = await gateway.ListAsync(body.UserAccessToken, ct).ConfigureAwait(false);
            return Results.Ok(new { items = pages });
        }
        catch (HttpRequestException ex)
        {
            // 401/403 from Pancake → token invalid/expired; surface a clear status for the re-auth prompt (M-6).
            return Results.Json(new { error = "pancake_connect_failed", detail = ex.Message },
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> MintPancakePagesAsync(
        MintPancakePagesRequest body, IPancakePageTokenService tokenService, ITenantAccessor tenants, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body?.UserAccessToken) || body.Pages is null || body.Pages.Count == 0)
            return Results.BadRequest(new { error = "user_access_token_and_pages_required" });
        var tenantId = tenants.Require().TenantId;
        var minted = new List<object>();
        foreach (var page in body.Pages)
        {
            if (string.IsNullOrWhiteSpace(page.PageId)) continue;
            try
            {
                var token = await tokenService.MintAndStoreAsync(
                    tenantId, page.PageId, page.Name, page.Platform, body.UserAccessToken, ct).ConfigureAwait(false);
                minted.Add(new { page.PageId, token.Name, token.Platform, status = "connected" });
            }
            catch (HttpRequestException ex)
            {
                minted.Add(new { page.PageId, status = "failed", error = ex.Message });
            }
        }
        return Results.Ok(new { items = minted });
    }

    private static async Task<IResult> ListConnectedPagesAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        // EARS[WHEN listing connected pages THE SYSTEM SHALL return each stored page with a connected status,
        // never exposing the token (audit-read only)]
        // Tokens live on the inbox row (single per-channel store for inbound + outbound).
        var pages = await db.Inboxes.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.DeletedAt == null && i.IsActive)
            .OrderBy(i => i.Name)
            .Select(i => new
            {
                PageId = i.ExternalPageId,
                i.Name,
                i.Platform,
                status = i.EncryptedAccessToken != null ? "connected" : "not_configured",
                mintedAt = (DateTimeOffset?)i.UpdatedAt,
            })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items = pages });
    }
}
