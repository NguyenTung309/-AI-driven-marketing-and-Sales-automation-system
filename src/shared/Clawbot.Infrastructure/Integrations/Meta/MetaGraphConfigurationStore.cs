using System.Security.Cryptography;
using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Integrations.Meta;

public sealed record MetaGraphConfigurationCandidate(Guid? TenantId, MetaGraphOptions Options);

public sealed record MetaAppConfigurationSnapshot(
    bool Configured,
    bool BusinessWebhookConfigured,
    string Source,
    string AppId,
    string ConfigurationId,
    string AuthorizationMode,
    bool HasAppSecret,
    bool HasWebhookVerifyToken,
    string RedirectUri,
    string FrontendReturnUrl,
    string ApiVersion,
    DateTimeOffset? UpdatedAt);

public sealed record MetaAppConfigurationUpdate(
    string AppId,
    string? AppSecret,
    string ConfigurationId,
    string? AuthorizationMode,
    string? WebhookVerifyToken,
    string RedirectUri,
    string FrontendReturnUrl);

public sealed record MetaAppConfigurationUpdateResult(
    MetaAppConfigurationSnapshot Snapshot,
    bool AuthorizationChanged);

public interface IMetaGraphConfigurationResolver
{
    Task<MetaGraphOptions> ResolveAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<MetaGraphConfigurationCandidate>> GetWebhookCandidatesAsync(CancellationToken ct = default);
}

public interface IMetaAppConfigurationService
{
    Task<MetaAppConfigurationSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken ct = default);
    Task<MetaAppConfigurationUpdateResult> UpdateAsync(
        Guid tenantId,
        MetaAppConfigurationUpdate update,
        CancellationToken ct = default);
}

public sealed partial class MetaGraphConfigurationStore(
    AppDbContext db,
    IEncryptor encryptor,
    IOptions<MetaGraphOptions> fallbackOptions,
    IClock clock,
    ILogger<MetaGraphConfigurationStore> logger) : IMetaGraphConfigurationResolver, IMetaAppConfigurationService
{
    public const string Provider = "meta";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Guid, MetaGraphOptions> _cache = [];
    private readonly MetaGraphOptions _fallback = fallbackOptions.Value;

    public async Task<MetaGraphOptions> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(tenantId, out var cached))
            return cached;

        var row = await FindRowAsync(tenantId, tracking: false, ct).ConfigureAwait(false);
        var payload = row is null ? null : TryDecrypt(row);
        var resolved = payload is null && row is null
            ? Clone(_fallback)
            : BuildOptions(payload ?? MetaAppCredentialPayload.Empty);
        _cache[tenantId] = resolved;
        return resolved;
    }

    public async Task<MetaAppConfigurationSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = await FindRowAsync(tenantId, tracking: false, ct).ConfigureAwait(false);
        if (row is null)
            return ToSnapshot(Clone(_fallback), "environment", null);

        var payload = TryDecrypt(row) ?? MetaAppCredentialPayload.Empty;
        return ToSnapshot(BuildOptions(payload), "database", row.UpdatedAt);
    }

    public async Task<MetaAppConfigurationUpdateResult> UpdateAsync(
        Guid tenantId,
        MetaAppConfigurationUpdate update,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var row = await FindRowAsync(tenantId, tracking: true, ct).ConfigureAwait(false);
        var current = row is null
            ? FromOptions(_fallback)
            : TryDecrypt(row) ?? MetaAppCredentialPayload.Empty;
        var next = new MetaAppCredentialPayload(
            update.AppId.Trim(),
            string.IsNullOrWhiteSpace(update.AppSecret) ? current.AppSecret ?? string.Empty : update.AppSecret.Trim(),
            update.ConfigurationId.Trim(),
            MetaAuthorizationModes.NormalizeOrDefault(update.AuthorizationMode),
            update.WebhookVerifyToken is null ? current.WebhookVerifyToken ?? string.Empty : update.WebhookVerifyToken.Trim(),
            update.RedirectUri.Trim(),
            update.FrontendReturnUrl.Trim());
        var resolved = BuildOptions(next);
        if (!resolved.IsConfigured)
            throw new InvalidOperationException("meta_app_configuration_invalid");

        var authorizationChanged = CurrentAuthorizationWasConfigured(current)
            && (!string.Equals(current.AppId, next.AppId, StringComparison.Ordinal)
                || !string.Equals(current.AppSecret, next.AppSecret, StringComparison.Ordinal)
                || !string.Equals(current.ConfigurationId, next.ConfigurationId, StringComparison.Ordinal)
                || !string.Equals(
                    MetaAuthorizationModes.NormalizeOrDefault(current.AuthorizationMode),
                    next.AuthorizationMode,
                    StringComparison.Ordinal));
        var encrypted = encryptor.Encrypt(JsonSerializer.Serialize(next, JsonOptions));
        var now = clock.UtcNow;
        if (row is null)
        {
            row = SocialCredential.Create(tenantId, Provider, encrypted, now);
            db.SocialCredentials.Add(row);
        }
        else
        {
            row.UpdateCredentials(encrypted, now);
            row.Activate(now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _cache[tenantId] = resolved;
        return new MetaAppConfigurationUpdateResult(
            ToSnapshot(resolved, "database", row.UpdatedAt),
            authorizationChanged);
    }

    public async Task<IReadOnlyList<MetaGraphConfigurationCandidate>> GetWebhookCandidatesAsync(
        CancellationToken ct = default)
    {
        var rows = await db.SocialCredentials
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Provider == Provider && x.PageId == null && x.IsActive && x.DeletedAt == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var candidates = new List<MetaGraphConfigurationCandidate>(rows.Count + 1);
        foreach (var row in rows)
        {
            var payload = TryDecrypt(row);
            if (payload is null)
                continue;
            var options = BuildOptions(payload);
            if (options.IsBusinessWebhookConfigured)
                candidates.Add(new MetaGraphConfigurationCandidate(row.TenantId, options));
        }

        var fallback = Clone(_fallback);
        if (fallback.IsBusinessWebhookConfigured)
            candidates.Add(new MetaGraphConfigurationCandidate(null, fallback));
        return candidates;
    }

    private Task<SocialCredential?> FindRowAsync(Guid tenantId, bool tracking, CancellationToken ct)
    {
        var query = db.SocialCredentials
            .IgnoreQueryFilters()
            .Where(x => x.TenantId == tenantId
                && x.Provider == Provider
                && x.PageId == null
                && x.DeletedAt == null);
        return (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private MetaAppCredentialPayload? TryDecrypt(SocialCredential row)
    {
        try
        {
            var json = encryptor.Decrypt(row.CredentialsEncrypted);
            return JsonSerializer.Deserialize<MetaAppCredentialPayload>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException)
        {
            LogInvalidStoredConfiguration(logger, row.TenantId, ex);
            return null;
        }
    }

    private MetaGraphOptions BuildOptions(MetaAppCredentialPayload payload) =>
        new()
        {
            BaseUrl = _fallback.BaseUrl,
            DialogBaseUrl = _fallback.DialogBaseUrl,
            ApiVersion = string.IsNullOrWhiteSpace(_fallback.ApiVersion) ? "v25.0" : _fallback.ApiVersion,
            AppId = payload.AppId ?? string.Empty,
            AppSecret = payload.AppSecret ?? string.Empty,
            ConfigurationId = payload.ConfigurationId ?? string.Empty,
            AuthorizationMode = MetaAuthorizationModes.NormalizeOrDefault(payload.AuthorizationMode),
            WebhookVerifyToken = payload.WebhookVerifyToken ?? string.Empty,
            RedirectUri = payload.RedirectUri ?? string.Empty,
            FrontendReturnUrl = payload.FrontendReturnUrl ?? string.Empty,
            TimeoutSeconds = _fallback.TimeoutSeconds,
        };

    private static MetaGraphOptions Clone(MetaGraphOptions source) =>
        new()
        {
            BaseUrl = source.BaseUrl,
            DialogBaseUrl = source.DialogBaseUrl,
            ApiVersion = source.ApiVersion,
            AppId = source.AppId,
            AppSecret = source.AppSecret,
            ConfigurationId = source.ConfigurationId,
            AuthorizationMode = MetaAuthorizationModes.NormalizeOrDefault(source.AuthorizationMode),
            WebhookVerifyToken = source.WebhookVerifyToken,
            RedirectUri = source.RedirectUri,
            FrontendReturnUrl = source.FrontendReturnUrl,
            TimeoutSeconds = source.TimeoutSeconds,
        };

    private static MetaAppCredentialPayload FromOptions(MetaGraphOptions source) =>
        new(
            source.AppId,
            source.AppSecret,
            source.ConfigurationId,
            MetaAuthorizationModes.NormalizeOrDefault(source.AuthorizationMode),
            source.WebhookVerifyToken,
            source.RedirectUri,
            source.FrontendReturnUrl);

    private static bool CurrentAuthorizationWasConfigured(MetaAppCredentialPayload value) =>
        !string.IsNullOrWhiteSpace(value.AppId)
        && !string.IsNullOrWhiteSpace(value.AppSecret)
        && !string.IsNullOrWhiteSpace(value.ConfigurationId);

    private static MetaAppConfigurationSnapshot ToSnapshot(
        MetaGraphOptions options,
        string source,
        DateTimeOffset? updatedAt) =>
        new(
            options.IsConfigured,
            options.IsBusinessWebhookConfigured,
            source,
            options.AppId,
            options.ConfigurationId,
            MetaAuthorizationModes.NormalizeOrDefault(options.AuthorizationMode),
            !string.IsNullOrWhiteSpace(options.AppSecret),
            !string.IsNullOrWhiteSpace(options.WebhookVerifyToken),
            options.RedirectUri,
            options.FrontendReturnUrl,
            options.ApiVersion,
            updatedAt);

    [LoggerMessage(EventId = 5252, Level = LogLevel.Warning, Message = "Stored Meta App configuration is invalid for tenant {TenantId}")]
    private static partial void LogInvalidStoredConfiguration(ILogger logger, Guid tenantId, Exception exception);

    private sealed record MetaAppCredentialPayload(
        string? AppId,
        string? AppSecret,
        string? ConfigurationId,
        string? AuthorizationMode,
        string? WebhookVerifyToken,
        string? RedirectUri,
        string? FrontendReturnUrl)
    {
        public static readonly MetaAppCredentialPayload Empty = new("", "", "", MetaAuthorizationModes.BusinessSystemUser, "", "", "");
    }
}
