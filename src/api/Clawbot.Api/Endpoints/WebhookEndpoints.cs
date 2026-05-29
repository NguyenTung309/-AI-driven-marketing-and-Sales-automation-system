using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/pancake/{tenantSlug}", async (
            string tenantSlug,
            HttpRequest req,
            IChannelAdapter adapter,
            IChannelMessageIngestor ingestor,
            AppDbContext db,
            CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var ok = await adapter.VerifyWebhookSignatureAsync(body, headers, ct).ConfigureAwait(false);
            if (!ok) return Results.Unauthorized();

            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Slug == tenantSlug)
                .Select(t => new { t.Id })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (tenant is null) return Results.NotFound(new { error = "tenant not found" });

            var messages = await adapter.ParseAsync(body, ct).ConfigureAwait(false);
            foreach (var msg in messages)
            {
                await ingestor.IngestAsync(tenant.Id, msg, ct).ConfigureAwait(false);
            }

            return Results.Accepted();
        }).AllowAnonymous();

        return app;
    }
}
