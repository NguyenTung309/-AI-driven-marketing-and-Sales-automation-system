using System.Security.Cryptography;
using System.Text.Json;
using Clawbot.Domain.Integrations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Integrations.Meta;

public sealed record MetaAssetSnapshot(
    Guid Id,
    string AssetType,
    string ExternalId,
    string Name,
    IReadOnlyList<string> Tasks,
    bool IsDefault,
    bool IsActive,
    DateTimeOffset LastSyncedAt);

public sealed record MetaIntegrationSnapshot(
    bool Connected,
    string Status,
    string ClientBusinessId,
    string SystemUserId,
    string TokenType,
    IReadOnlyList<string> GrantedScopes,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? DataAccessExpiresAt,
    DateTimeOffset? LastValidatedAt,
    string? LastError,
    IReadOnlyList<MetaAssetSnapshot> Assets);

public sealed record MetaPageCredential(
    Guid AssetId,
    string PageId,
    string PageName,
    string PageAccessToken);

public enum MetaInstagramResolutionStatus
{
    Disconnected,
    ReconnectRequired,
    PageUnavailable,
    MissingScopes,
    NotLinked,
    Resolved,
}

public enum MetaPageRefreshStatus
{
    ConnectionUnavailable,
    ReconnectRequired,
    TargetUnavailable,
    Resolved,
}

public sealed record MetaPageRefreshResult(
    MetaPageRefreshStatus Status,
    MetaPageCredential? Credential);

public sealed record MetaInstagramCredential(
    Guid PageAssetId,
    string InstagramUserId,
    string PageAccessToken);

public sealed record MetaInstagramResolution(
    MetaInstagramResolutionStatus Status,
    MetaInstagramCredential? Credential);

public interface IMetaIntegrationService
{
    Task CompleteAuthorizationAsync(Guid tenantId, string code, CancellationToken ct = default);
    Task<MetaIntegrationSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken ct = default);
    Task<IReadOnlyList<MetaAssetSnapshot>> GetPublishablePagesAsync(Guid tenantId, CancellationToken ct = default);
    Task SyncPagesAsync(Guid tenantId, CancellationToken ct = default);
    Task ValidateAsync(Guid tenantId, CancellationToken ct = default);
    Task SetDefaultPageAsync(Guid tenantId, Guid assetId, CancellationToken ct = default);
    Task DisconnectAsync(Guid tenantId, CancellationToken ct = default);
    Task<string?> ResolveRootTokenAsync(Guid tenantId, CancellationToken ct = default);
    Task<MetaPageCredential?> ResolvePageAsync(Guid tenantId, Guid? assetId, CancellationToken ct = default);
    Task<MetaInstagramResolution> ResolveInstagramAsync(Guid tenantId, Guid? assetId, CancellationToken ct = default);
    Task<MetaPageRefreshResult> RefreshPageAsync(Guid tenantId, Guid? assetId, CancellationToken ct = default);
    Task MarkReconnectRequiredAsync(Guid tenantId, string reason, CancellationToken ct = default);
}

public sealed class MetaIntegrationService(
    AppDbContext db,
    IEncryptor encryptor,
    IMetaGraphClient graph,
    IMetaGraphConfigurationResolver configurations,
    IClock clock) : IMetaIntegrationService
{
    private static readonly string[] RequiredPageScopes = ["pages_manage_posts", "pages_read_engagement", "pages_show_list"];
    private static readonly string[] RequiredInstagramScopes = ["instagram_basic", "instagram_content_publish"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly IMetaGraphClient _graph = graph;
    private readonly IMetaGraphConfigurationResolver _configurations = configurations;
    private readonly IClock _clock = clock;

    public async Task CompleteAuthorizationAsync(Guid tenantId, string code, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("tenantId and OAuth code are required.");
        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!configuration.IsConfigured)
            throw new InvalidOperationException("Meta Graph is not configured.");

        var token = await _graph.ExchangeCodeAsync(tenantId, code, ct).ConfigureAwait(false);
        var debug = await _graph.DebugTokenAsync(tenantId, token.AccessToken, ct).ConfigureAwait(false);
        if (!debug.IsValid || !string.Equals(debug.AppId, configuration.AppId, StringComparison.Ordinal))
            throw new MetaGraphException("meta_oauth_token_invalid");
        var missingScopes = MissingPageScopes(debug.Scopes);
        if (missingScopes.Length > 0)
            throw new MetaGraphException($"meta_required_permissions_missing:{string.Join(',', missingScopes)}");

        var connectionTokenType = MetaConnectionTokenTypes.FromDebugToken(debug.Type);
        if (!TokenTypeMatchesConfiguration(connectionTokenType, configuration))
            throw new MetaGraphException(TokenTypeMismatchError(configuration));

        var identity = await _graph.GetIdentityAsync(tenantId, token.AccessToken, ct).ConfigureAwait(false);
        if (MetaAuthorizationModes.NormalizeOrDefault(configuration.AuthorizationMode) == MetaAuthorizationModes.BusinessSystemUser
            && string.IsNullOrWhiteSpace(identity.ClientBusinessId))
            throw new MetaGraphException("meta_business_system_user_token_required");
        var pages = await _graph.GetPagesAsync(tenantId, token.AccessToken, ct).ConfigureAwait(false);

        var now = _clock.UtcNow;
        var expiresAt = debug.ExpiresAt
            ?? (token.ExpiresIn is > 0 ? now.AddSeconds(token.ExpiresIn.Value) : null);
        var scopesJson = JsonSerializer.Serialize(debug.Scopes.Order(StringComparer.Ordinal), JsonOptions);
        var encryptedToken = _encryptor.Encrypt(token.AccessToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        await AcquireAuthorizationLockAsync(tenantId, ct).ConfigureAwait(false);
        var connection = await ConnectionQuery(tenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (connection is null)
        {
            connection = MetaConnection.Create(
                tenantId,
                identity.ClientBusinessId,
                identity.Id,
                connectionTokenType,
                encryptedToken,
                scopesJson,
                expiresAt,
                debug.DataAccessExpiresAt,
                now);
            _db.MetaConnections.Add(connection);
        }
        else
        {
            connection.UpdateAuthorization(
                identity.ClientBusinessId,
                identity.Id,
                connectionTokenType,
                encryptedToken,
                scopesJson,
                expiresAt,
                debug.DataAccessExpiresAt,
                now);
        }

        await ApplyPagesCoreAsync(connection, pages, ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<MetaIntegrationSnapshot> GetSnapshotAsync(Guid tenantId, CancellationToken ct = default)
    {
        var connection = await ConnectionQuery(tenantId).AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (connection is null)
            return new MetaIntegrationSnapshot(false, "disconnected", string.Empty, string.Empty, string.Empty, [], null, null, null, null, []);

        var assets = await AssetQuery(tenantId)
            .AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        var connected = IsConnectionUsable(connection, configuration);
        var status = connection.Status == "active" && !connected ? "reconnect_required" : connection.Status;
        return new MetaIntegrationSnapshot(
            connected,
            status,
            connection.ClientBusinessId,
            connection.SystemUserId,
            connection.TokenType,
            DeserializeStrings(connection.GrantedScopesJson),
            connection.ExpiresAt,
            connection.DataAccessExpiresAt,
            connection.LastValidatedAt,
            connection.LastError,
            assets.Select(ToSnapshot).ToList());
    }

    public async Task<IReadOnlyList<MetaAssetSnapshot>> GetPublishablePagesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var connection = await GetUsableConnectionAsync(tenantId, ct).ConfigureAwait(false);
        if (connection is null)
            return [];

        var assets = await AssetQuery(tenantId)
            .AsNoTracking()
            .Where(x => x.ConnectionId == connection.Id && x.AssetType == "page" && x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return assets
            .Select(ToSnapshot)
            .Where(CanPublish)
            .ToList();
    }

    public async Task SyncPagesAsync(Guid tenantId, CancellationToken ct = default)
    {
        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!configuration.IsConfigured)
            throw new InvalidOperationException("Meta Graph is not configured.");
        var connection = await ConnectionQuery(tenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Meta connection not found.");
        if (!IsConnectionUsable(connection, configuration))
            throw new InvalidOperationException("Meta connection is not active.");
        var token = Decrypt(connection.AccessTokenEncrypted)
            ?? throw new InvalidOperationException("Meta connection token is unavailable.");
        try
        {
            await SyncPagesCoreAsync(connection, token, ct).ConfigureAwait(false);
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            connection.RequireReconnect($"meta_token_{ex.Code}_{ex.Subcode}", _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ValidateAsync(Guid tenantId, CancellationToken ct = default)
    {
        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!configuration.IsConfigured)
            throw new InvalidOperationException("Meta Graph is not configured.");
        var connection = await ConnectionQuery(tenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Meta connection not found.");
        var token = Decrypt(connection.AccessTokenEncrypted);
        if (string.IsNullOrWhiteSpace(token))
        {
            connection.RequireReconnect("meta_token_missing", _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        MetaDebugToken debug;
        try
        {
            debug = await _graph.DebugTokenAsync(tenantId, token, ct).ConfigureAwait(false);
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            connection.RequireReconnect($"meta_token_{ex.Code}_{ex.Subcode}", _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }
        catch (MetaGraphException ex)
        {
            // App/BM bị Meta block, rate-limit, App Secret sai… — không phải user token hết hạn.
            // Chỉ NoteError (giữ status active) để publish page token vẫn thử được; reconnect OAuth không gỡ block.
            connection.NoteError(
                $"meta_validate_debug:{ex.Code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ex.HttpStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}:{TruncateMetaMessage(ex.Message)}",
                _clock.UtcNow,
                restoreActive: ShouldKeepConnectionActive(connection));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw;
        }

        if (!debug.IsValid || !string.Equals(debug.AppId, configuration.AppId, StringComparison.Ordinal))
        {
            connection.RequireReconnect(
                !debug.IsValid ? "meta_token_invalid" : "meta_token_app_mismatch",
                _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var missingScopes = MissingPageScopes(debug.Scopes);
        if (missingScopes.Length > 0)
        {
            connection.RequireReconnect($"meta_required_permissions_missing:{string.Join(',', missingScopes)}", _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var connectionTokenType = MetaConnectionTokenTypes.FromDebugToken(debug.Type);
        if (!TokenTypeMatchesConfiguration(connectionTokenType, configuration)
            || (connectionTokenType == MetaConnectionTokenTypes.BusinessIntegrationSystemUser
                && string.IsNullOrWhiteSpace(connection.ClientBusinessId)))
        {
            connection.RequireReconnect(TokenTypeMismatchError(configuration), _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return;
        }

        var scopesJson = JsonSerializer.Serialize(debug.Scopes.Order(StringComparer.Ordinal), JsonOptions);
        connection.UpdateAuthorization(
            connection.ClientBusinessId,
            connection.SystemUserId,
            connectionTokenType,
            connection.AccessTokenEncrypted,
            scopesJson,
            debug.ExpiresAt ?? connection.ExpiresAt,
            debug.DataAccessExpiresAt ?? connection.DataAccessExpiresAt,
            _clock.UtcNow);
        // Lưu trạng thái token trước khi sync page — tránh mất cập nhật scope/expiry nếu me/accounts fail.
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        try
        {
            await SyncPagesCoreAsync(connection, token, ct).ConfigureAwait(false);
        }
        catch (MetaGraphException ex) when (ex.IsTokenError)
        {
            connection.RequireReconnect($"meta_token_{ex.Code}_{ex.Subcode}", _clock.UtcNow);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (MetaGraphException ex)
        {
            connection.NoteError(
                $"meta_validate_pages:{ex.Code?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? ex.HttpStatus?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"}:{TruncateMetaMessage(ex.Message)}",
                _clock.UtcNow,
                restoreActive: ShouldKeepConnectionActive(connection));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private bool ShouldKeepConnectionActive(MetaConnection connection)
    {
        var now = _clock.UtcNow;
        if (string.IsNullOrWhiteSpace(Decrypt(connection.AccessTokenEncrypted)))
            return false;
        if (connection.ExpiresAt.HasValue && connection.ExpiresAt <= now)
            return false;
        if (connection.DataAccessExpiresAt.HasValue && connection.DataAccessExpiresAt <= now)
            return false;
        return true;
    }

    private static string TruncateMetaMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "unknown";
        var trimmed = message.Trim().Replace('\n', ' ').Replace('\r', ' ');
        return trimmed.Length <= 120 ? trimmed : trimmed[..120];
    }

    public async Task SetDefaultPageAsync(Guid tenantId, Guid assetId, CancellationToken ct = default)
    {
        var connection = await GetUsableConnectionAsync(tenantId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Meta connection is not active.");
        var assets = await AssetQuery(tenantId)
            .Where(x => x.ConnectionId == connection.Id && x.AssetType == "page" && x.IsActive)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var selected = assets.FirstOrDefault(x => x.Id == assetId);
        if (selected is null)
            throw new KeyNotFoundException("Meta Page asset not found.");
        if (!CanPublish(ToSnapshot(selected)))
            throw new InvalidOperationException("Meta Page does not grant the CREATE_CONTENT task.");

        var now = _clock.UtcNow;
        foreach (var asset in assets)
            asset.SetDefault(asset.Id == assetId, now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task DisconnectAsync(Guid tenantId, CancellationToken ct = default)
    {
        var connection = await ConnectionQuery(tenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (connection is null)
            return;

        var now = _clock.UtcNow;
        connection.Disconnect(now);
        var assets = await AssetQuery(tenantId).ToListAsync(ct).ConfigureAwait(false);
        foreach (var asset in assets)
            asset.Deactivate(now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<string?> ResolveRootTokenAsync(Guid tenantId, CancellationToken ct = default)
    {
        var connection = await GetUsableConnectionAsync(tenantId, ct).ConfigureAwait(false);
        if (connection is null)
            return null;
        return Decrypt(connection.AccessTokenEncrypted);
    }

    public async Task<MetaPageCredential?> ResolvePageAsync(Guid tenantId, Guid? assetId, CancellationToken ct = default)
    {
        var connection = await GetUsableConnectionAsync(tenantId, ct).ConfigureAwait(false);
        if (connection is null)
            return null;

        var query = AssetQuery(tenantId)
            .AsNoTracking()
            .Where(x => x.ConnectionId == connection.Id && x.AssetType == "page" && x.IsActive);
        MetaAsset? asset;
        if (assetId.HasValue)
        {
            asset = await query.FirstOrDefaultAsync(x => x.Id == assetId.Value, ct).ConfigureAwait(false);
        }
        else
        {
            var candidates = await query
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            asset = candidates.FirstOrDefault(x => CanPublish(ToSnapshot(x)));
        }
        if (asset is null || !CanPublish(ToSnapshot(asset)))
            return null;

        var token = Decrypt(asset.AccessTokenEncrypted);
        return string.IsNullOrWhiteSpace(token)
            ? null
            : new MetaPageCredential(asset.Id, asset.ExternalId, asset.Name, token);
    }

    public async Task<MetaInstagramResolution> ResolveInstagramAsync(
        Guid tenantId,
        Guid? assetId,
        CancellationToken ct = default)
    {
        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!configuration.IsConfigured)
            return new MetaInstagramResolution(MetaInstagramResolutionStatus.Disconnected, null);

        var connection = await ConnectionQuery(tenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (connection is null || string.Equals(connection.Status, "disconnected", StringComparison.Ordinal))
            return new MetaInstagramResolution(MetaInstagramResolutionStatus.Disconnected, null);
        if (!IsConnectionUsable(connection, configuration))
            return new MetaInstagramResolution(MetaInstagramResolutionStatus.ReconnectRequired, null);

        var page = await ResolvePageAsync(tenantId, assetId, ct).ConfigureAwait(false);
        if (page is null)
            return new MetaInstagramResolution(MetaInstagramResolutionStatus.PageUnavailable, null);

        if (MissingInstagramScopes(DeserializeStrings(connection.GrantedScopesJson)).Length > 0)
            return new MetaInstagramResolution(MetaInstagramResolutionStatus.MissingScopes, null);

        var instagramUserId = await _graph.ResolveInstagramAccountAsync(
            tenantId,
            page.PageId,
            page.PageAccessToken,
            ct).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(instagramUserId)
            ? new MetaInstagramResolution(MetaInstagramResolutionStatus.NotLinked, null)
            : new MetaInstagramResolution(
                MetaInstagramResolutionStatus.Resolved,
                new MetaInstagramCredential(page.AssetId, instagramUserId, page.PageAccessToken));
    }

    public async Task<MetaPageRefreshResult> RefreshPageAsync(
        Guid tenantId,
        Guid? assetId,
        CancellationToken ct = default)
    {
        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!configuration.IsConfigured)
            return new MetaPageRefreshResult(MetaPageRefreshStatus.ConnectionUnavailable, null);

        var connection = await ConnectionQuery(tenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (connection is null || string.Equals(connection.Status, "disconnected", StringComparison.Ordinal))
            return new MetaPageRefreshResult(MetaPageRefreshStatus.ConnectionUnavailable, null);
        if (!IsConnectionUsable(connection, configuration))
            return new MetaPageRefreshResult(MetaPageRefreshStatus.ReconnectRequired, null);

        var rootToken = Decrypt(connection.AccessTokenEncrypted);
        if (string.IsNullOrWhiteSpace(rootToken))
            return new MetaPageRefreshResult(MetaPageRefreshStatus.ReconnectRequired, null);

        await SyncPagesCoreAsync(connection, rootToken, ct).ConfigureAwait(false);
        var credential = await ResolvePageAsync(tenantId, assetId, ct).ConfigureAwait(false);
        return credential is null
            ? new MetaPageRefreshResult(MetaPageRefreshStatus.TargetUnavailable, null)
            : new MetaPageRefreshResult(MetaPageRefreshStatus.Resolved, credential);
    }

    public async Task MarkReconnectRequiredAsync(Guid tenantId, string reason, CancellationToken ct = default)
    {
        var connection = await ConnectionQuery(tenantId).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (connection is null)
            return;
        connection.RequireReconnect(reason, _clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task AcquireAuthorizationLockAsync(Guid tenantId, CancellationToken ct)
    {
        if (!_db.Database.IsSqlServer())
            return;

        var resource = $"clawbot:meta-authorization:{tenantId:N}";
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource={resource},
                @LockMode='Exclusive',
                @LockOwner='Transaction',
                @LockTimeout=15000;
            IF @result < 0
                THROW 51000, 'meta_authorization_lock_failed', 1;
            """, ct).ConfigureAwait(false);
    }

    private async Task SyncPagesCoreAsync(MetaConnection connection, string rootToken, CancellationToken ct)
    {
        var pages = await _graph.GetPagesAsync(connection.TenantId, rootToken, ct).ConfigureAwait(false);
        await ApplyPagesCoreAsync(connection, pages, ct).ConfigureAwait(false);
    }

    private async Task ApplyPagesCoreAsync(
        MetaConnection connection,
        IReadOnlyList<MetaPageToken> pages,
        CancellationToken ct)
    {
        var existing = await AssetQuery(connection.TenantId)
            .Where(x => x.ConnectionId == connection.Id && x.AssetType == "page")
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byExternalId = existing.ToDictionary(x => x.ExternalId, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var syncedAssets = new List<MetaAsset>();
        var now = _clock.UtcNow;

        foreach (var page in pages)
        {
            seen.Add(page.Id);
            var tasksJson = JsonSerializer.Serialize(page.Tasks.Order(StringComparer.Ordinal), JsonOptions);
            var encryptedToken = _encryptor.Encrypt(page.AccessToken);
            if (byExternalId.TryGetValue(page.Id, out var asset))
            {
                asset.UpdatePage(page.Name, tasksJson, encryptedToken, now);
            }
            else
            {
                asset = MetaAsset.CreatePage(
                    connection.TenantId,
                    connection.Id,
                    page.Id,
                    page.Name,
                    tasksJson,
                    encryptedToken,
                    isDefault: false,
                    now);
                _db.MetaAssets.Add(asset);
            }
            syncedAssets.Add(asset);
        }

        foreach (var asset in existing.Where(x => !seen.Contains(x.ExternalId)))
            asset.Deactivate(now);

        var publishableAssets = syncedAssets
            .Where(x => CanPublish(ToSnapshot(x)))
            .ToList();
        var defaultAsset = publishableAssets.FirstOrDefault(x => x.IsDefault)
            ?? publishableAssets.FirstOrDefault();
        foreach (var asset in syncedAssets)
            asset.SetDefault(defaultAsset is not null && asset.Id == defaultAsset.Id, now);

        connection.MarkHealthy(now);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private IQueryable<MetaConnection> ConnectionQuery(Guid tenantId) =>
        _db.MetaConnections.IgnoreQueryFilters().Where(x => x.TenantId == tenantId);

    private IQueryable<MetaAsset> AssetQuery(Guid tenantId) =>
        _db.MetaAssets.IgnoreQueryFilters().Where(x => x.TenantId == tenantId);

    private async Task<MetaConnection?> GetUsableConnectionAsync(Guid tenantId, CancellationToken ct)
    {
        var configuration = await _configurations.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        if (!configuration.IsConfigured)
            return null;

        var connection = await ConnectionQuery(tenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Status == "active"
                && x.AccessTokenEncrypted != string.Empty
                && (!x.ExpiresAt.HasValue || x.ExpiresAt > _clock.UtcNow)
                && (!x.DataAccessExpiresAt.HasValue || x.DataAccessExpiresAt > _clock.UtcNow), ct)
            .ConfigureAwait(false);
        return connection is not null && IsConnectionUsable(connection, configuration)
            ? connection
            : null;
    }

    private bool IsConnectionUsable(MetaConnection connection, MetaGraphOptions configuration) =>
        configuration.IsConfigured
        && TokenTypeMatchesConfiguration(connection.TokenType, configuration)
        && connection.Status == "active"
        && !string.IsNullOrWhiteSpace(Decrypt(connection.AccessTokenEncrypted))
        && (!connection.ExpiresAt.HasValue || connection.ExpiresAt > _clock.UtcNow)
        && (!connection.DataAccessExpiresAt.HasValue || connection.DataAccessExpiresAt > _clock.UtcNow);

    private static bool TokenTypeMatchesConfiguration(string tokenType, MetaGraphOptions configuration) =>
        string.Equals(
            tokenType,
            MetaAuthorizationModes.ExpectedTokenType(configuration.AuthorizationMode),
            StringComparison.Ordinal);

    private static string TokenTypeMismatchError(MetaGraphOptions configuration) =>
        MetaAuthorizationModes.NormalizeOrDefault(configuration.AuthorizationMode) == MetaAuthorizationModes.DevelopmentUser
            ? "meta_user_access_token_required"
            : "meta_business_system_user_token_required";

    private static bool CanPublish(MetaAssetSnapshot asset) =>
        asset.Tasks.Any(task => string.Equals(task, "CREATE_CONTENT", StringComparison.OrdinalIgnoreCase));

    private static string[] MissingPageScopes(IReadOnlyList<string> scopes)
    {
        var grantedScopes = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return RequiredPageScopes.Where(scope => !grantedScopes.Contains(scope)).ToArray();
    }

    private static string[] MissingInstagramScopes(IReadOnlyList<string> scopes)
    {
        var grantedScopes = scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return RequiredInstagramScopes.Where(scope => !grantedScopes.Contains(scope)).ToArray();
    }

    private string? Decrypt(string encrypted)
    {
        if (string.IsNullOrWhiteSpace(encrypted))
            return null;
        try { return _encryptor.Decrypt(encrypted); }
        catch (FormatException) { return null; }
        catch (CryptographicException) { return null; }
    }

    private static MetaAssetSnapshot ToSnapshot(MetaAsset asset) =>
        new(
            asset.Id,
            asset.AssetType,
            asset.ExternalId,
            asset.Name,
            DeserializeStrings(asset.TasksJson),
            asset.IsDefault,
            asset.IsActive,
            asset.LastSyncedAt);

    private static string[] DeserializeStrings(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }
}
