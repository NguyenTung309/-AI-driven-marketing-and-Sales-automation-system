using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Channels;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ChannelsEndpoints
{
    public static IEndpointRouteBuilder MapChannels(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/channels/pancake").RequirePermission("channels:manage").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/config", GetAsync);
        grp.MapPut("/config", UpsertAsync);
        grp.MapDelete("/config", DeleteAsync);
        grp.MapGet("/webhook-url", WebhookUrlAsync);

        return app;
    }

    private static async Task<Results<Ok<PancakeConfigDto>, NotFound>> GetAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var row = await db.PancakeConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId, ct).ConfigureAwait(false);
        if (row is null) return TypedResults.NotFound();
        return TypedResults.Ok(Map(row));
    }

    private static async Task<IResult> UpsertAsync(
        UpdatePancakeConfigRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var row = await db.PancakeConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId, ct).ConfigureAwait(false);

        if (row is null)
        {
            row = PancakeConfig.Create(tenant.TenantId, clock.UtcNow);
            db.PancakeConfigs.Add(row);
        }

        if (!PancakeEndpointPolicy.TryNormalizeBaseUrl(
                body.BaseUrl ?? row.BaseUrl,
                out var normalizedBaseUrl))
        {
            return Results.BadRequest(new
            {
                error = "pancake_base_url_not_allowed",
            });
        }

        row.UpdateEndpoint(normalizedBaseUrl, body.SendPathTemplate ?? row.SendPathTemplate,
            body.AuthMode ?? row.AuthMode, clock.UtcNow);
        row.UpdateSignature(body.SignatureHeader ?? row.SignatureHeader,
            body.SignatureAlgo ?? row.SignatureAlgo,
            body.SignatureEncoding ?? row.SignatureEncoding, clock.UtcNow);

        if (!string.IsNullOrEmpty(body.AccessToken))
            row.UpdateAccessToken(encryptor.Encrypt(body.AccessToken), clock.UtcNow);
        if (!string.IsNullOrEmpty(body.WebhookSecret))
            row.UpdateWebhookSecret(encryptor.Encrypt(body.WebhookSecret), clock.UtcNow);

        if (body.IsActive.HasValue)
        {
            if (body.IsActive.Value) row.Activate(clock.UtcNow);
            else row.Deactivate(clock.UtcNow);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Map(row));
    }

    private static async Task<IResult> DeleteAsync(
        AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenant = tenants.Require();
        var row = await db.PancakeConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenant.TenantId, ct).ConfigureAwait(false);
        if (row is null) return Results.NoContent();
        db.PancakeConfigs.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static IResult WebhookUrlAsync(HttpRequest req, ITenantAccessor tenants)
    {
        var tenant = tenants.Require();
        var baseUrl = $"{req.Scheme}://{req.Host}";
        return Results.Ok(new PancakeWebhookUrlResponse(
            WebhookUrl: $"{baseUrl}/webhooks/pancake/{tenant.TenantSlug}",
            TenantSlug: tenant.TenantSlug));
    }

    private static PancakeConfigDto Map(PancakeConfig row) =>
        new(row.Id, row.BaseUrl,
            HasAccessToken: !string.IsNullOrEmpty(row.AccessTokenEncrypted),
            HasWebhookSecret: !string.IsNullOrEmpty(row.WebhookSecretEncrypted),
            row.SignatureHeader, row.SignatureAlgo, row.SignatureEncoding,
            row.SendPathTemplate, row.AuthMode, row.IsActive, row.UpdatedAt);
}




