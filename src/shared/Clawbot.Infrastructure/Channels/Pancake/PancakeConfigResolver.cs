using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Channels.Pancake;

public sealed partial class PancakeConfigResolver(
    AppDbContext db,
    IEncryptor encryptor,
    IConfiguration cfg,
    ILogger<PancakeConfigResolver> logger) : IPancakeConfigResolver
{
    private const string DefaultBaseUrl = "https://pages.fm/api/public_api/v1";
    private const string DefaultSendPath = "/pages/{page_id}/conversations/{thread_id}/messages";
    private const string DefaultSigHeader = "x-pancake-signature";

    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly IConfiguration _cfg = cfg;
    private readonly ILogger<PancakeConfigResolver> _logger = logger;

    public async Task<PancakeRuntimeConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId != Guid.Empty)
        {
            var row = await _db.PancakeConfigs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.IsActive, ct).ConfigureAwait(false);

            if (row is not null)
            {
                return new PancakeRuntimeConfig(
                    BaseUrl: row.BaseUrl,
                    AccessToken: SafeDecrypt(row.AccessTokenEncrypted),
                    WebhookSecret: SafeDecrypt(row.WebhookSecretEncrypted),
                    SignatureHeader: row.SignatureHeader,
                    SignatureAlgo: row.SignatureAlgo,
                    SignatureEncoding: row.SignatureEncoding,
                    SendPathTemplate: row.SendPathTemplate,
                    AuthMode: row.AuthMode,
                    PageId: string.Empty);
            }
        }

        var section = _cfg.GetSection("Channels:Pancake");
        if (section.Exists())
        {
            return new PancakeRuntimeConfig(
                BaseUrl: section["BaseUrl"] ?? DefaultBaseUrl,
                AccessToken: section["AccessToken"] ?? string.Empty,
                WebhookSecret: section["WebhookSecret"] ?? string.Empty,
                SignatureHeader: (section["SignatureHeader"] ?? DefaultSigHeader).ToLowerInvariant(),
                SignatureAlgo: (section["SignatureAlgo"] ?? "hmac-sha256").ToLowerInvariant(),
                SignatureEncoding: (section["SignatureEncoding"] ?? "hex").ToLowerInvariant(),
                SendPathTemplate: section["SendPathTemplate"] ?? DefaultSendPath,
                AuthMode: (section["AuthMode"] ?? "query").ToLowerInvariant(),
                PageId: section["PageId"] ?? string.Empty);
        }

        // 3rd fallback: env vars (demo mode). .NET host builder includes env vars in IConfiguration.
        var envPageToken = _cfg["PANCAKE_PAGE_ACCESS_TOKEN"];
        var envPageId = _cfg["PANCAKE_PAGE_ID"];
        var envWebhookSecret = _cfg["PANCAKE_WEBHOOK_SECRET"];

        if (!string.IsNullOrEmpty(envPageToken))
        {
            LogEnvFallback(_logger);
            return new PancakeRuntimeConfig(
                BaseUrl: DefaultBaseUrl,
                AccessToken: envPageToken,
                WebhookSecret: envWebhookSecret ?? string.Empty,
                SignatureHeader: DefaultSigHeader,
                SignatureAlgo: "hmac-sha256",
                SignatureEncoding: "hex",
                SendPathTemplate: DefaultSendPath,
                AuthMode: "query",
                PageId: envPageId ?? string.Empty);
        }

        return null;
    }

    private string SafeDecrypt(string cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return string.Empty;
        try { return _encryptor.Decrypt(cipher); }
        catch (FormatException) { return string.Empty; }
        catch (System.Security.Cryptography.CryptographicException) { return string.Empty; }
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "PancakeConfigResolver: using env-var fallback (demo mode)")]
    private static partial void LogEnvFallback(ILogger logger);
}
