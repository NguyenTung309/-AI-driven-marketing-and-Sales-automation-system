using System.Text;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Integrations;

public sealed class MetaGraphConfigurationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UpdateAsync_encrypts_tenant_configuration_and_masks_secrets_in_snapshot()
    {
        var tenant = Tenant.Create("meta-config", "Meta Config", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var store = BuildStore(fx);

        var result = await store.UpdateAsync(tenant.Id, Update(appSecret: "db-secret", webhookToken: "verify-secret"));

        result.Snapshot.Source.Should().Be("database");
        result.Snapshot.Configured.Should().BeTrue();
        result.Snapshot.AuthorizationMode.Should().Be(MetaAuthorizationModes.BusinessSystemUser);
        result.Snapshot.HasAppSecret.Should().BeTrue();
        result.Snapshot.HasWebhookVerifyToken.Should().BeTrue();
        var row = await fx.Db.SocialCredentials.IgnoreQueryFilters().SingleAsync();
        row.Provider.Should().Be(MetaGraphConfigurationStore.Provider);
        row.CredentialsEncrypted.Should().NotContain("db-secret").And.NotContain("verify-secret");

        var resolved = await store.ResolveAsync(tenant.Id);
        resolved.AppId.Should().Be("app-123");
        resolved.AppSecret.Should().Be("db-secret");
        resolved.ConfigurationId.Should().Be("config-123");
        resolved.AuthorizationMode.Should().Be(MetaAuthorizationModes.BusinessSystemUser);
        resolved.ApiVersion.Should().Be("v25.0");
    }

    [Fact]
    public async Task UpdateAsync_persists_development_mode_and_disables_business_webhook()
    {
        var tenant = Tenant.Create("meta-development", "Meta Development", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var store = BuildStore(fx);

        var result = await store.UpdateAsync(
            tenant.Id,
            Update("db-secret", "stored-but-unused", MetaAuthorizationModes.DevelopmentUser));

        result.Snapshot.AuthorizationMode.Should().Be(MetaAuthorizationModes.DevelopmentUser);
        result.Snapshot.BusinessWebhookConfigured.Should().BeFalse();
        result.Snapshot.HasWebhookVerifyToken.Should().BeTrue();
        (await store.ResolveAsync(tenant.Id)).AuthorizationMode.Should().Be(MetaAuthorizationModes.DevelopmentUser);
        (await store.GetWebhookCandidatesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_defaults_legacy_payload_without_mode_to_business_system_user()
    {
        var tenant = Tenant.Create("meta-legacy", "Meta Legacy", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var encryptor = new Base64Encryptor();
        var legacyPayload = """
            {"appId":"legacy-app","appSecret":"legacy-secret","configurationId":"legacy-config","webhookVerifyToken":"legacy-verify","redirectUri":"https://api.example/api/admin/meta/callback","frontendReturnUrl":"https://app.example/system"}
            """;
        fx.Db.SocialCredentials.Add(SocialCredential.Create(
            tenant.Id,
            MetaGraphConfigurationStore.Provider,
            encryptor.Encrypt(legacyPayload),
            Now));
        await fx.Db.SaveChangesAsync();
        var store = BuildStore(fx);

        var resolved = await store.ResolveAsync(tenant.Id);

        resolved.AuthorizationMode.Should().Be(MetaAuthorizationModes.BusinessSystemUser);
        resolved.IsBusinessWebhookConfigured.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_marks_authorization_changed_when_mode_changes()
    {
        var tenant = Tenant.Create("meta-mode-change", "Meta Mode Change", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var first = BuildStore(fx);
        await first.UpdateAsync(tenant.Id, Update("db-secret", null));
        var second = BuildStore(fx);

        var result = await second.UpdateAsync(
            tenant.Id,
            Update(null, null, MetaAuthorizationModes.DevelopmentUser));

        result.AuthorizationChanged.Should().BeTrue();
        result.Snapshot.AuthorizationMode.Should().Be(MetaAuthorizationModes.DevelopmentUser);
    }

    [Fact]
    public async Task UpdateAsync_keeps_stored_secrets_when_password_fields_are_left_blank()
    {
        var tenant = Tenant.Create("meta-preserve", "Meta Preserve", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var first = BuildStore(fx);
        await first.UpdateAsync(tenant.Id, Update(appSecret: "first-secret", webhookToken: "first-verify"));

        var second = BuildStore(fx);
        await second.UpdateAsync(tenant.Id, Update(appSecret: null, webhookToken: null));

        var resolved = await second.ResolveAsync(tenant.Id);
        resolved.AppSecret.Should().Be("first-secret");
        resolved.WebhookVerifyToken.Should().Be("first-verify");
    }

    [Fact]
    public async Task ResolveAsync_uses_environment_only_when_tenant_has_no_database_override()
    {
        var tenant = Tenant.Create("meta-fallback", "Meta Fallback", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var store = BuildStore(fx, Fallback("env-app", "env-secret"));

        var snapshot = await store.GetSnapshotAsync(tenant.Id);
        var resolved = await store.ResolveAsync(tenant.Id);

        snapshot.Source.Should().Be("environment");
        snapshot.Configured.Should().BeTrue();
        resolved.AppId.Should().Be("env-app");
        resolved.AppSecret.Should().Be("env-secret");
    }

    [Fact]
    public async Task GetWebhookCandidatesAsync_returns_tenant_override_and_environment_fallback()
    {
        var tenant = Tenant.Create("meta-webhook", "Meta Webhook", "pro", Now);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        await fx.Db.SaveChangesAsync();
        var store = BuildStore(fx, Fallback("env-app", "env-secret"));
        await store.UpdateAsync(tenant.Id, Update(appSecret: "db-secret", webhookToken: "db-verify"));

        var candidates = await store.GetWebhookCandidatesAsync();

        candidates.Should().HaveCount(2);
        candidates.Should().ContainSingle(x => x.TenantId == tenant.Id && x.Options.AppId == "app-123");
        candidates.Should().ContainSingle(x => x.TenantId == null && x.Options.AppId == "env-app");
    }

    private static MetaGraphConfigurationStore BuildStore(TestAppDb fx, MetaGraphOptions? fallback = null) =>
        new(
            fx.Db,
            new Base64Encryptor(),
            Options.Create(fallback ?? Fallback("", "")),
            new FixedClock(Now),
            NullLogger<MetaGraphConfigurationStore>.Instance);

    private static MetaAppConfigurationUpdate Update(
        string? appSecret,
        string? webhookToken,
        string authorizationMode = MetaAuthorizationModes.BusinessSystemUser) =>
        new(
            "app-123",
            appSecret,
            "config-123",
            authorizationMode,
            webhookToken,
            "https://api.example/api/admin/meta/callback",
            "https://app.example/system");

    private static MetaGraphOptions Fallback(string appId, string appSecret) =>
        new()
        {
            AppId = appId,
            AppSecret = appSecret,
            ConfigurationId = string.IsNullOrWhiteSpace(appId) ? "" : "env-config",
            AuthorizationMode = MetaAuthorizationModes.BusinessSystemUser,
            WebhookVerifyToken = string.IsNullOrWhiteSpace(appId) ? "" : "env-verify",
            RedirectUri = "https://api.example/api/admin/meta/callback",
            FrontendReturnUrl = "https://app.example/system",
            ApiVersion = "v25.0",
        };

    private sealed class Base64Encryptor : IEncryptor
    {
        public string Encrypt(string plaintext) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));
        public string Decrypt(string ciphertext) => Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
