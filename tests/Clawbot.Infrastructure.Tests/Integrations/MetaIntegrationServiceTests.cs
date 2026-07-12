using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Integrations;

public sealed class MetaIntegrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CompleteAuthorizationAsync_stores_encrypted_tokens_and_exposes_only_publishable_pages()
    {
        var tenant = Tenant.Create("meta-test", "Meta Test", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.GetPagesAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>()).Returns(
        [
            new MetaPageToken("page-read", "Read only", "read-token", ["ANALYZE"]),
            new MetaPageToken("page-write", "Writable", "write-token", ["ANALYZE", "CREATE_CONTENT"]),
        ]);
        var service = BuildService(fx, graph);

        await service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        var connection = await fx.Db.MetaConnections.IgnoreQueryFilters().SingleAsync();
        connection.AccessTokenEncrypted.Should().Be("enc:root-token");
        connection.ClientBusinessId.Should().Be("business-1");
        connection.TokenType.Should().Be(MetaConnectionTokenTypes.BusinessIntegrationSystemUser);
        var storedAssets = await fx.Db.MetaAssets.IgnoreQueryFilters().OrderBy(x => x.ExternalId).ToListAsync();
        storedAssets.Should().HaveCount(2);
        storedAssets.Should().OnlyContain(x => x.AccessTokenEncrypted.StartsWith("enc:", StringComparison.Ordinal));

        var publishable = await service.GetPublishablePagesAsync(tenant.Id);
        var page = publishable.Should().ContainSingle().Which;
        page.ExternalId.Should().Be("page-write");
        page.IsDefault.Should().BeTrue();
        (await service.ResolveRootTokenAsync(tenant.Id)).Should().Be("root-token");
        var credential = await service.ResolvePageAsync(tenant.Id, null);
        credential.Should().NotBeNull();
        credential!.PageId.Should().Be("page-write");
        credential.PageAccessToken.Should().Be("write-token");
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_rejects_token_issued_for_another_app()
    {
        var tenant = Tenant.Create("meta-invalid", "Meta Invalid", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph(appId: "other-app");
        var service = BuildService(fx, graph);

        var action = () => service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Message.Should().Be("meta_oauth_token_invalid");
        (await fx.Db.MetaConnections.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_rejects_configuration_without_page_permissions()
    {
        var tenant = Tenant.Create("meta-scopes", "Meta Scopes", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.DebugTokenAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaDebugToken(true, "app-123", string.Empty, "system-user-1", ["pages_show_list"], null, null));
        var service = BuildService(fx, graph);

        var action = () => service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Message.Should().Contain("pages_manage_posts");
        (await fx.Db.MetaConnections.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_rejects_user_token_without_client_business_id()
    {
        var tenant = Tenant.Create("meta-user-token", "Meta User Token", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.DebugTokenAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaDebugToken(true, "app-123", "USER", "user-1", ["pages_show_list", "pages_read_engagement", "pages_manage_posts"], null, null));
        graph.GetIdentityAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaIdentity("user-1", string.Empty));
        var service = BuildService(fx, graph);

        var action = () => service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        var error = await action.Should().ThrowAsync<MetaGraphException>();
        error.Which.Message.Should().Be("meta_business_system_user_token_required");
        (await fx.Db.MetaConnections.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_accepts_user_token_in_development_mode()
    {
        var tenant = Tenant.Create("meta-development-user", "Meta Development User", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.DebugTokenAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaDebugToken(true, "app-123", "USER", "user-1", ["pages_show_list", "pages_read_engagement", "pages_manage_posts"], Now.AddDays(60), Now.AddDays(90)));
        graph.GetIdentityAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaIdentity("user-1", string.Empty));
        graph.GetPagesAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>()).Returns(
            [new MetaPageToken("page-write", "Development Page", "page-token", ["CREATE_CONTENT"])]);
        var service = BuildService(fx, graph, MetaAuthorizationModes.DevelopmentUser);

        await service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        var connection = await fx.Db.MetaConnections.IgnoreQueryFilters().SingleAsync();
        connection.ClientBusinessId.Should().BeEmpty();
        connection.SystemUserId.Should().Be("user-1");
        connection.TokenType.Should().Be(MetaConnectionTokenTypes.User);
        var snapshot = await service.GetSnapshotAsync(tenant.Id);
        snapshot.Connected.Should().BeTrue();
        snapshot.TokenType.Should().Be(MetaConnectionTokenTypes.User);
        (await service.ResolvePageAsync(tenant.Id, null))!.PageId.Should().Be("page-write");
    }

    [Fact]
    public async Task GetSnapshotAsync_requires_reconnect_when_configuration_mode_changes()
    {
        var tenant = Tenant.Create("meta-mode-mismatch", "Meta Mode Mismatch", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        var businessService = BuildService(fx, graph);
        await businessService.CompleteAuthorizationAsync(tenant.Id, "oauth-code");
        var developmentService = BuildService(fx, graph, MetaAuthorizationModes.DevelopmentUser);

        var snapshot = await developmentService.GetSnapshotAsync(tenant.Id);

        snapshot.Connected.Should().BeFalse();
        snapshot.Status.Should().Be("reconnect_required");
        (await developmentService.ResolveRootTokenAsync(tenant.Id)).Should().BeNull();
    }

    [Fact]
    public async Task CompleteAuthorizationAsync_does_not_persist_connection_when_page_sync_fails()
    {
        var tenant = Tenant.Create("meta-sync-fail", "Meta Sync Fail", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.GetPagesAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<MetaPageToken>>(new MetaGraphException("permission denied", code: 200)));
        var service = BuildService(fx, graph);

        var action = () => service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        await action.Should().ThrowAsync<MetaGraphException>();
        (await fx.Db.MetaConnections.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await fx.Db.MetaAssets.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MarkReconnectRequiredAsync_blocks_root_and_page_credentials()
    {
        var tenant = Tenant.Create("meta-reconnect", "Meta Reconnect", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.GetPagesAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>()).Returns(
            [new MetaPageToken("page-write", "Writable", "write-token", ["CREATE_CONTENT"])]);
        var service = BuildService(fx, graph);
        await service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");

        await service.MarkReconnectRequiredAsync(tenant.Id, "meta_token_190_463");

        (await service.ResolveRootTokenAsync(tenant.Id)).Should().BeNull();
        (await service.ResolvePageAsync(tenant.Id, null)).Should().BeNull();
        (await service.GetPublishablePagesAsync(tenant.Id)).Should().BeEmpty();
        var snapshot = await service.GetSnapshotAsync(tenant.Id);
        snapshot.Connected.Should().BeFalse();
        snapshot.Status.Should().Be("reconnect_required");
    }

    [Fact]
    public async Task SyncPagesAsync_marks_connection_for_reconnect_on_token_error()
    {
        var tenant = Tenant.Create("meta-sync-token", "Meta Sync Token", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var graph = AuthorizedGraph();
        graph.GetPagesAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>()).Returns(
            [new MetaPageToken("page-write", "Writable", "write-token", ["CREATE_CONTENT"])]);
        var service = BuildService(fx, graph);
        await service.CompleteAuthorizationAsync(tenant.Id, "oauth-code");
        graph.GetPagesAsync(tenant.Id, "root-token", Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<MetaPageToken>>(new MetaGraphException("expired", code: 190, subcode: 463)));

        var action = () => service.SyncPagesAsync(tenant.Id);

        await action.Should().ThrowAsync<MetaGraphException>();
        var snapshot = await service.GetSnapshotAsync(tenant.Id);
        snapshot.Status.Should().Be("reconnect_required");
        snapshot.Connected.Should().BeFalse();
    }

    private static IMetaGraphClient AuthorizedGraph(string appId = "app-123")
    {
        var graph = Substitute.For<IMetaGraphClient>();
        graph.ExchangeCodeAsync(Arg.Any<Guid>(), "oauth-code", Arg.Any<CancellationToken>())
            .Returns(new MetaTokenResponse("root-token", "bearer", null));
        graph.DebugTokenAsync(Arg.Any<Guid>(), "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaDebugToken(true, appId, "BUSINESS_INTEGRATION_SYSTEM_USER", "system-user-1", ["pages_show_list", "pages_read_engagement", "pages_manage_posts"], null, null));
        graph.GetIdentityAsync(Arg.Any<Guid>(), "root-token", Arg.Any<CancellationToken>())
            .Returns(new MetaIdentity("system-user-1", "business-1"));
        graph.GetPagesAsync(Arg.Any<Guid>(), "root-token", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MetaPageToken>());
        return graph;
    }

    private static MetaIntegrationService BuildService(
        TestAppDb fx,
        IMetaGraphClient graph,
        string authorizationMode = MetaAuthorizationModes.BusinessSystemUser) =>
        new(
            fx.Db,
            new PrefixEncryptor(),
            graph,
            Configurations(authorizationMode),
            new FixedClock(Now));

    private static IMetaGraphConfigurationResolver Configurations(string authorizationMode)
    {
        var configurations = Substitute.For<IMetaGraphConfigurationResolver>();
        configurations.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new MetaGraphOptions
            {
                AppId = "app-123",
                AppSecret = "secret",
                ConfigurationId = "config-123",
                AuthorizationMode = authorizationMode,
                RedirectUri = "https://api.example/api/admin/meta/callback",
                FrontendReturnUrl = "https://app.example/system",
            });
        return configurations;
    }

    private sealed class PrefixEncryptor : IEncryptor
    {
        public string Encrypt(string plaintext) => $"enc:{plaintext}";
        public string Decrypt(string ciphertext) => ciphertext.StartsWith("enc:", StringComparison.Ordinal) ? ciphertext[4..] : throw new FormatException();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
