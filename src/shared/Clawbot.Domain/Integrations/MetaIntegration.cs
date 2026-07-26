using Clawbot.Domain.Common;

namespace Clawbot.Domain.Integrations;

public sealed class MetaConnection : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string ClientBusinessId { get; private set; } = string.Empty;
    public string SystemUserId { get; private set; } = string.Empty;
    public string TokenType { get; private set; } = "business_integration_system_user";
    public string AccessTokenEncrypted { get; private set; } = string.Empty;
    public string GrantedScopesJson { get; private set; } = "[]";
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? DataAccessExpiresAt { get; private set; }
    public DateTimeOffset? LastValidatedAt { get; private set; }
    public string Status { get; private set; } = "active";
    public string? LastError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MetaConnection() { }

    public static MetaConnection Create(
        Guid tenantId,
        string clientBusinessId,
        string systemUserId,
        string tokenType,
        string encryptedAccessToken,
        string grantedScopesJson,
        DateTimeOffset? expiresAt,
        DateTimeOffset? dataAccessExpiresAt,
        DateTimeOffset at) =>
        Create(
            Guid.NewGuid(),
            tenantId,
            clientBusinessId,
            systemUserId,
            tokenType,
            encryptedAccessToken,
            grantedScopesJson,
            expiresAt,
            dataAccessExpiresAt,
            at);

    public static MetaConnection Create(
        Guid id,
        Guid tenantId,
        string clientBusinessId,
        string systemUserId,
        string tokenType,
        string encryptedAccessToken,
        string grantedScopesJson,
        DateTimeOffset? expiresAt,
        DateTimeOffset? dataAccessExpiresAt,
        DateTimeOffset at) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ClientBusinessId = clientBusinessId.Trim(),
            SystemUserId = systemUserId.Trim(),
            TokenType = tokenType.Trim(),
            AccessTokenEncrypted = encryptedAccessToken,
            GrantedScopesJson = grantedScopesJson,
            ExpiresAt = expiresAt,
            DataAccessExpiresAt = dataAccessExpiresAt,
            LastValidatedAt = at,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void UpdateAuthorization(
        string clientBusinessId,
        string systemUserId,
        string tokenType,
        string encryptedAccessToken,
        string grantedScopesJson,
        DateTimeOffset? expiresAt,
        DateTimeOffset? dataAccessExpiresAt,
        DateTimeOffset at)
    {
        ClientBusinessId = clientBusinessId.Trim();
        SystemUserId = systemUserId.Trim();
        TokenType = tokenType.Trim();
        AccessTokenEncrypted = encryptedAccessToken;
        GrantedScopesJson = grantedScopesJson;
        ExpiresAt = expiresAt;
        DataAccessExpiresAt = dataAccessExpiresAt;
        LastValidatedAt = at;
        Status = "active";
        LastError = null;
        UpdatedAt = at;
    }

    public void ReprotectAccessToken(string encryptedAccessToken, DateTimeOffset at)
    {
        AccessTokenEncrypted = encryptedAccessToken;
        UpdatedAt = at;
    }

    public void MarkHealthy(DateTimeOffset at)
    {
        Status = "active";
        LastError = null;
        LastValidatedAt = at;
        UpdatedAt = at;
    }

    public void RequireReconnect(string error, DateTimeOffset at)
    {
        Status = "reconnect_required";
        LastError = string.IsNullOrWhiteSpace(error) ? "meta_token_invalid" : error.Trim();
        LastValidatedAt = at;
        UpdatedAt = at;
    }

    // Ghi lỗi validate/sync app-level (vd. "API access blocked") mà KHÔNG ép reconnect —
    // token/page vẫn có thể publish được; reconnect OAuth không gỡ block Meta.
    // restoreActive: gỡ reconnect_required do validate nhầm trước đó (token còn hạn).
    public void NoteError(string error, DateTimeOffset at, bool restoreActive = false)
    {
        LastError = string.IsNullOrWhiteSpace(error) ? "meta_error" : error.Trim();
        if (restoreActive && Status == "reconnect_required")
            Status = "active";
        LastValidatedAt = at;
        UpdatedAt = at;
    }

    public void Disconnect(DateTimeOffset at)
    {
        AccessTokenEncrypted = string.Empty;
        Status = "disconnected";
        LastError = null;
        ExpiresAt = null;
        DataAccessExpiresAt = null;
        UpdatedAt = at;
    }
}

public sealed class MetaAsset : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string AssetType { get; private set; } = string.Empty;
    public string ExternalId { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string TasksJson { get; private set; } = "[]";
    public string AccessTokenEncrypted { get; private set; } = string.Empty;
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset LastSyncedAt { get; private set; }
    // Lần cuối Page này đăng ký thành công webhook feed. Null = chưa từng đăng ký được
    // (thường do thiếu scope pages_manage_metadata) nên comment chỉ về qua job đối soát.
    public DateTimeOffset? FeedSubscribedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private MetaAsset() { }

    public void MarkFeedSubscribed(DateTimeOffset at)
    {
        FeedSubscribedAt = at;
        UpdatedAt = at;
    }

    public static MetaAsset CreatePage(
        Guid tenantId,
        Guid connectionId,
        string externalId,
        string name,
        string tasksJson,
        string encryptedAccessToken,
        bool isDefault,
        DateTimeOffset at) =>
        CreatePage(
            Guid.NewGuid(),
            tenantId,
            connectionId,
            externalId,
            name,
            tasksJson,
            encryptedAccessToken,
            isDefault,
            at);

    public static MetaAsset CreatePage(
        Guid id,
        Guid tenantId,
        Guid connectionId,
        string externalId,
        string name,
        string tasksJson,
        string encryptedAccessToken,
        bool isDefault,
        DateTimeOffset at) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            ConnectionId = connectionId,
            AssetType = "page",
            ExternalId = externalId.Trim(),
            Name = name.Trim(),
            TasksJson = tasksJson,
            AccessTokenEncrypted = encryptedAccessToken,
            IsDefault = isDefault,
            LastSyncedAt = at,
            CreatedAt = at,
            UpdatedAt = at,
        };

    public void UpdatePage(string name, string tasksJson, string encryptedAccessToken, DateTimeOffset at)
    {
        Name = name.Trim();
        TasksJson = tasksJson;
        AccessTokenEncrypted = encryptedAccessToken;
        IsActive = true;
        LastSyncedAt = at;
        UpdatedAt = at;
    }

    public void ReprotectAccessToken(string encryptedAccessToken, DateTimeOffset at)
    {
        AccessTokenEncrypted = encryptedAccessToken;
        UpdatedAt = at;
    }

    public void SetDefault(bool isDefault, DateTimeOffset at)
    {
        IsDefault = isDefault;
        UpdatedAt = at;
    }

    public void Deactivate(DateTimeOffset at)
    {
        IsActive = false;
        IsDefault = false;
        AccessTokenEncrypted = string.Empty;
        UpdatedAt = at;
    }
}

public sealed class MetaOAuthState : AggregateRoot<Guid>, ITenantOwned, IAuditExempt
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string StateHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConsumedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private MetaOAuthState() { }

    public static MetaOAuthState Create(
        Guid tenantId,
        Guid userId,
        string stateHash,
        DateTimeOffset expiresAt,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            StateHash = stateHash,
            ExpiresAt = expiresAt,
            CreatedAt = createdAt,
        };

    public bool TryConsume(DateTimeOffset at)
    {
        if (ConsumedAt.HasValue || ExpiresAt <= at)
            return false;

        ConsumedAt = at;
        return true;
    }
}
