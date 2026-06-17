using System.Net;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Api.Endpoints;

public static partial class WebhookEndpoints
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning,
        Message = "Webhook HMAC rejected for tenant {TenantSlug} from {Ip}")]
    private static partial void LogHmacRejected(ILogger logger, string tenantSlug, string ip);

    public static IEndpointRouteBuilder MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/pancake/{tenantSlug}", async (
            string tenantSlug,
            HttpRequest req,
            IChannelAdapter adapter,
            IChannelMessageIngestor ingestor,
            AppDbContext db,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var logger = loggerFactory.CreateLogger("WebhookEndpoints");
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            var headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

            var ok = await adapter.VerifyWebhookSignatureAsync(body, headers, ct).ConfigureAwait(false);
            if (!ok)
            {
                var ip = req.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                LogHmacRejected(logger, tenantSlug, ip);

                db.AuditLogs.Add(AuditLog.Create(
                    tenantId: Guid.Empty,
                    userId: null,
                    action: "webhook.hmac.reject",
                    resourceType: "webhook",
                    resourceId: null,
                    occurredAt: DateTimeOffset.UtcNow,
                    diffJson: $"{{\"tenant_slug\":\"{tenantSlug}\",\"reason\":\"hmac_mismatch\"}}",
                    ip: req.HttpContext.Connection.RemoteIpAddress,
                    userAgent: req.Headers.UserAgent.ToString()));
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                return Results.Unauthorized();
            }

            var tenant = await db.Tenants
                .IgnoreQueryFilters()
                .Where(t => t.Slug == tenantSlug)
                .Select(t => new { t.Id })
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (tenant is null) return Results.NotFound(new { error = "tenant not found" });

            var messages = await adapter.ParseAsync(body, ct).ConfigureAwait(false);
            foreach (var msg in messages)
            {
                var result = await ingestor.IngestAsync(tenant.Id, msg, ct).ConfigureAwait(false);
                if (!result.Deduplicated
                    && result.MessageId is { } messageId
                    && string.Equals(msg.MessageType, "comment", StringComparison.OrdinalIgnoreCase))
                {
                    BackgroundJob.Enqueue<CommentAutoReplyJob>(
                        j => j.RunAsync(tenant.Id, messageId, CancellationToken.None));
                }
            }

            return Results.Accepted();
        }).AllowAnonymous().RequireRateLimiting(RateLimitingExtensions.WebhookPolicy);

        return app;
    }
}
