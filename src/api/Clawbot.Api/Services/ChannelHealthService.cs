using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Services;

public sealed class ChannelHealthService(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public async Task<ChannelHealthReport> GetPancakeAsync(CancellationToken ct = default)
    {
        var configs = await _db.PancakeConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var active = configs.Where(c => c.IsActive).ToList();
        var configuredCount = active.Count;
        var outboundCount = active.Count(c => !string.IsNullOrEmpty(c.AccessTokenEncrypted));
        var webhookCount = active.Count(HasSupportedWebhookSignature);

        var checks = new[]
        {
            Check("config", configuredCount > 0, $"{configuredCount} active tenant config(s)"),
            Check("outbound", outboundCount > 0, $"{outboundCount}/{configuredCount} active config(s) have access token"),
            Check("webhook", webhookCount > 0, $"{webhookCount}/{configuredCount} active config(s) have webhook secret and supported signature"),
        };

        var status = checks.All(c => c.Status == "ok") ? "ok" : "degraded";
        return new ChannelHealthReport(
            "PancakeChannelAdapter",
            status,
            configs.Count,
            configuredCount,
            checks);
    }

    private static bool HasSupportedWebhookSignature(PancakeConfig config) =>
        !string.IsNullOrEmpty(config.WebhookSecretEncrypted)
        && string.Equals(config.SignatureAlgo, "hmac-sha256", StringComparison.Ordinal)
        && (string.Equals(config.SignatureEncoding, "hex", StringComparison.Ordinal)
            || string.Equals(config.SignatureEncoding, "base64", StringComparison.Ordinal));

    private static ChannelHealthCheck Check(string name, bool ok, string detail) =>
        new(name, ok ? "ok" : "degraded", detail);
}

public sealed record ChannelHealthReport(
    string Adapter,
    string Status,
    int TotalConfigs,
    int ConfiguredTenants,
    IReadOnlyList<ChannelHealthCheck> Checks);

public sealed record ChannelHealthCheck(string Name, string Status, string Detail);
