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
    // Verified (SPEC-16 §5.1): page ops run under pages.fm/api/public_api/v1 with the page_access_token.
    // The previous default (pancake.vn/api/v1) targeted the wrong host and sent the user token where a page token is required.
    private const string DefaultBaseUrl = "https://pages.fm/api/public_api/v1";
    private const string DefaultSendPath = "/pages/{page_id}/conversations/{thread_id}/messages";
    private const string DefaultSigHeader = "x-pancake-signature";

    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly IConfiguration _cfg = cfg;
    private readonly ILogger<PancakeConfigResolver> _logger = logger;

    public async Task<PancakeRuntimeConfig?> ResolveTenantOnlyAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return null;

        var row = await _db.PancakeConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.IsActive, ct).ConfigureAwait(false);
        return row is null
            ? null
            : new PancakeRuntimeConfig(
                BaseUrl: PreferSendBaseUrl(row.BaseUrl, _cfg["Channels:Pancake:BaseUrl"]),
                AccessToken: SafeDecrypt(row.AccessTokenEncrypted),
                WebhookSecret: SafeDecrypt(row.WebhookSecretEncrypted),
                SignatureHeader: row.SignatureHeader,
                SignatureAlgo: row.SignatureAlgo,
                SignatureEncoding: row.SignatureEncoding,
                SendPathTemplate: row.SendPathTemplate,
                AuthMode: row.AuthMode,
                PageId: row.PageId ?? string.Empty);
    }

    public async Task<PancakeRuntimeConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenantConfig = await ResolveTenantOnlyAsync(tenantId, ct).ConfigureAwait(false);
        if (tenantConfig is not null) return tenantConfig;

        // Send path chỉ cần BaseUrl/SendPathTemplate/AuthMode; page token lấy từ inbox.
        // Tenant có row pancake_configs nhưng is_active=0 vẫn dùng template của row đó
        // (không phụ thuộc AccessToken tenant-wide — token thật resolve theo page).
        // BaseUrl legacy "pancake.vn" bị loại — send API phải pages.fm.
        var section = _cfg.GetSection("Channels:Pancake");
        var inactiveRow = tenantId == Guid.Empty
            ? null
            : await _db.PancakeConfigs
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct).ConfigureAwait(false);
        if (inactiveRow is not null)
        {
            return new PancakeRuntimeConfig(
                // Row đang inactive không được quyền đổi outbound host. Chỉ dùng host từ cấu hình
                // process hoặc default pages.fm; row chỉ đóng góp path/auth metadata.
                BaseUrl: PreferSendBaseUrl(section["BaseUrl"], null),
                AccessToken: string.Empty,
                WebhookSecret: SafeDecrypt(inactiveRow.WebhookSecretEncrypted),
                SignatureHeader: inactiveRow.SignatureHeader,
                SignatureAlgo: inactiveRow.SignatureAlgo,
                SignatureEncoding: inactiveRow.SignatureEncoding,
                SendPathTemplate: string.IsNullOrWhiteSpace(inactiveRow.SendPathTemplate) ? DefaultSendPath : inactiveRow.SendPathTemplate,
                // Inactive/legacy rows frequently contain bearer from the old pancake.vn endpoint.
                // pages.fm page operations require page_access_token in the query string.
                AuthMode: "query",
                PageId: inactiveRow.PageId ?? string.Empty);
        }

        if (section.Exists())
        {
            return new PancakeRuntimeConfig(
                BaseUrl: PreferSendBaseUrl(section["BaseUrl"], null),
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

    // pages.fm public_api là host send đúng; pancake.vn (legacy seed) gửi sẽ 404/401.
    private static string PreferSendBaseUrl(string? primary, string? secondary)
    {
        foreach (var candidate in new[] { primary, secondary })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (candidate.Contains("pancake.vn", StringComparison.OrdinalIgnoreCase)) continue;
            return candidate;
        }
        return DefaultBaseUrl;
    }

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "PancakeConfigResolver: using env-var fallback (demo mode)")]
    private static partial void LogEnvFallback(ILogger logger);
}
