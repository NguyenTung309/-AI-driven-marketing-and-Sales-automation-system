using Clawbot.Domain.Integrations;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Integrations;

public sealed class MetaIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // ── MetaConnection ────────────────────────────────────────────────

    [Fact]
    public void MetaConnection_Create_SetsAllFields()
    {
        var conn = MetaConnection.Create(TenantId, "biz-1", "sys-1", "business_integration_system_user",
            "enc-token", "[\"pages_read_engagement\"]", Now.AddDays(30), Now.AddDays(60), Now);

        conn.TenantId.Should().Be(TenantId);
        conn.ClientBusinessId.Should().Be("biz-1");
        conn.SystemUserId.Should().Be("sys-1");
        conn.TokenType.Should().Be("business_integration_system_user");
        conn.AccessTokenEncrypted.Should().Be("enc-token");
        conn.GrantedScopesJson.Should().Be("[\"pages_read_engagement\"]");
        conn.ExpiresAt.Should().Be(Now.AddDays(30));
        conn.DataAccessExpiresAt.Should().Be(Now.AddDays(60));
        conn.LastValidatedAt.Should().Be(Now);
        conn.Status.Should().Be("active");
        conn.LastError.Should().BeNull();
    }

    [Fact]
    public void MetaConnection_UpdateAuthorization_ResetsStatusAndError()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", null, null, Now);
        conn.RequireReconnect("expired", Now.AddMinutes(1));

        conn.UpdateAuthorization("b2", "s2", "t2", "tok2", "[\"read\"]", Now.AddDays(30), null, Now.AddMinutes(5));

        conn.ClientBusinessId.Should().Be("b2");
        conn.AccessTokenEncrypted.Should().Be("tok2");
        conn.Status.Should().Be("active");
        conn.LastError.Should().BeNull();
        conn.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MetaConnection_MarkHealthy_ClearsError()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", null, null, Now);
        conn.NoteError("some error", Now.AddMinutes(1));

        conn.MarkHealthy(Now.AddMinutes(5));

        conn.Status.Should().Be("active");
        conn.LastError.Should().BeNull();
        conn.LastValidatedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MetaConnection_RequireReconnect_SetsStatusAndError()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", null, null, Now);

        conn.RequireReconnect("token_expired", Now);

        conn.Status.Should().Be("reconnect_required");
        conn.LastError.Should().Be("token_expired");
    }

    [Fact]
    public void MetaConnection_RequireReconnect_DefaultsBlankError()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", null, null, Now);

        conn.RequireReconnect("", Now);

        conn.LastError.Should().Be("meta_token_invalid");
    }

    [Fact]
    public void MetaConnection_NoteError_DoesNotForceReconnect()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", null, null, Now);

        conn.NoteError("api_blocked", Now);

        conn.Status.Should().Be("active");
        conn.LastError.Should().Be("api_blocked");
    }

    [Fact]
    public void MetaConnection_NoteError_RestoresActiveWhenRequested()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", null, null, Now);
        conn.RequireReconnect("err", Now);

        conn.NoteError("false_alarm", Now.AddMinutes(1), restoreActive: true);

        conn.Status.Should().Be("active");
        conn.LastError.Should().Be("false_alarm");
    }

    [Fact]
    public void MetaConnection_Disconnect_ClearsTokenAndExpires()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "tok", "[]", Now.AddDays(1), Now.AddDays(2), Now);

        conn.Disconnect(Now.AddMinutes(5));

        conn.AccessTokenEncrypted.Should().BeEmpty();
        conn.Status.Should().Be("disconnected");
        conn.LastError.Should().BeNull();
        conn.ExpiresAt.Should().BeNull();
        conn.DataAccessExpiresAt.Should().BeNull();
    }

    [Fact]
    public void MetaConnection_ReprotectAccessToken_UpdatesTokenOnly()
    {
        var conn = MetaConnection.Create(TenantId, "b", "s", "t", "old", "[]", null, null, Now);

        conn.ReprotectAccessToken("new-enc", Now.AddMinutes(1));

        conn.AccessTokenEncrypted.Should().Be("new-enc");
        conn.UpdatedAt.Should().Be(Now.AddMinutes(1));
        conn.Status.Should().Be("active");
    }

    // ── MetaAsset ─────────────────────────────────────────────────────

    [Fact]
    public void MetaAsset_CreatePage_SetsDefaults()
    {
        var connId = Guid.NewGuid();
        var asset = MetaAsset.CreatePage(TenantId, connId, "ext-1", "My Page", "[\"manage\"]", "enc", true, Now);

        asset.TenantId.Should().Be(TenantId);
        asset.ConnectionId.Should().Be(connId);
        asset.AssetType.Should().Be("page");
        asset.ExternalId.Should().Be("ext-1");
        asset.Name.Should().Be("My Page");
        asset.IsDefault.Should().BeTrue();
        asset.IsActive.Should().BeTrue();
        asset.FeedSubscribedAt.Should().BeNull();
    }

    [Fact]
    public void MetaAsset_UpdatePage_RefreshesFields()
    {
        var asset = MetaAsset.CreatePage(TenantId, Guid.NewGuid(), "e", "Old", "[]", "t", false, Now);

        asset.UpdatePage("New Name", "[\"read\"]", "t2", Now.AddMinutes(5));

        asset.Name.Should().Be("New Name");
        asset.TasksJson.Should().Be("[\"read\"]");
        asset.AccessTokenEncrypted.Should().Be("t2");
        asset.IsActive.Should().BeTrue();
        asset.LastSyncedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MetaAsset_Deactivate_ClearsTokenAndDefault()
    {
        var asset = MetaAsset.CreatePage(TenantId, Guid.NewGuid(), "e", "P", "[]", "tok", true, Now);

        asset.Deactivate(Now.AddMinutes(1));

        asset.IsActive.Should().BeFalse();
        asset.IsDefault.Should().BeFalse();
        asset.AccessTokenEncrypted.Should().BeEmpty();
    }

    [Fact]
    public void MetaAsset_MarkFeedSubscribed_SetsTimestamp()
    {
        var asset = MetaAsset.CreatePage(TenantId, Guid.NewGuid(), "e", "P", "[]", "t", false, Now);

        asset.MarkFeedSubscribed(Now.AddHours(1));

        asset.FeedSubscribedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void MetaAsset_SetDefault_TogglesFlag()
    {
        var asset = MetaAsset.CreatePage(TenantId, Guid.NewGuid(), "e", "P", "[]", "t", false, Now);

        asset.SetDefault(true, Now.AddMinutes(1));

        asset.IsDefault.Should().BeTrue();
    }

    // ── MetaOAuthState ────────────────────────────────────────────────

    [Fact]
    public void MetaOAuthState_Create_SetsFields()
    {
        var state = MetaOAuthState.Create(TenantId, UserId, "hash-abc", Now.AddMinutes(10), Now);

        state.TenantId.Should().Be(TenantId);
        state.UserId.Should().Be(UserId);
        state.StateHash.Should().Be("hash-abc");
        state.ExpiresAt.Should().Be(Now.AddMinutes(10));
        state.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public void MetaOAuthState_TryConsume_SucceedsBeforeExpiry()
    {
        var state = MetaOAuthState.Create(TenantId, UserId, "h", Now.AddMinutes(10), Now);

        var consumed = state.TryConsume(Now.AddMinutes(5));

        consumed.Should().BeTrue();
        state.ConsumedAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void MetaOAuthState_TryConsume_FailsAfterExpiry()
    {
        var state = MetaOAuthState.Create(TenantId, UserId, "h", Now.AddMinutes(10), Now);

        var consumed = state.TryConsume(Now.AddMinutes(15));

        consumed.Should().BeFalse();
        state.ConsumedAt.Should().BeNull();
    }

    [Fact]
    public void MetaOAuthState_TryConsume_FailsWhenAlreadyConsumed()
    {
        var state = MetaOAuthState.Create(TenantId, UserId, "h", Now.AddMinutes(10), Now);
        state.TryConsume(Now.AddMinutes(1));

        var consumed = state.TryConsume(Now.AddMinutes(2));

        consumed.Should().BeFalse();
        state.ConsumedAt.Should().Be(Now.AddMinutes(1));
    }
}
