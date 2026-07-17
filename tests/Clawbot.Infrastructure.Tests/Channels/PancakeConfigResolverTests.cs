using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

// M06/M13 � PancakeConfigResolver tenant-DB ? appsettings ? defaults cascade.
public sealed class PancakeConfigResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private static IConfiguration Config(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task Returns_decrypted_db_row_when_present()
    {
        using var fx = new TestAppDb();
        var row = PancakeConfig.Create(fx.TenantId, Now);
        row.UpdateAccessToken("CIPHER", Now);
        fx.Db.PancakeConfigs.Add(row);
        await fx.Db.SaveChangesAsync();

        var enc = Substitute.For<IEncryptor>();
        enc.Decrypt("CIPHER").Returns("PLAIN");

        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()), NullLogger<PancakeConfigResolver>.Instance);
        var result = await sut.ResolveAsync(fx.TenantId);

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("PLAIN");
        result.BaseUrl.Should().Be("https://pages.fm/api/public_api/v1"); // legacy pancake.vn host is never used for send
    }

    [Fact]
    public async Task Falls_back_to_appsettings_when_no_db_row()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>
        {
            ["Channels:Pancake:BaseUrl"] = "https://custom.example",
            ["Channels:Pancake:AuthMode"] = "BEARER",
        }), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(Guid.NewGuid());

        result.Should().NotBeNull();
        result!.BaseUrl.Should().Be("https://custom.example");
        result.AuthMode.Should().Be("bearer"); // normalized to lowercase
    }

    [Fact]
    public async Task Tenant_only_resolution_never_inherits_global_secrets()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>
        {
            ["Channels:Pancake:AccessToken"] = "global-token",
            ["Channels:Pancake:WebhookSecret"] = "global-secret",
        }), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveTenantOnlyAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Empty_tenant_skips_db_and_uses_appsettings()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>
        {
            ["Channels:Pancake:BaseUrl"] = "https://fallback",
        }), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(Guid.Empty);

        result.Should().NotBeNull();
        result!.BaseUrl.Should().Be("https://fallback");
    }

    [Fact]
    public async Task Returns_null_when_no_db_row_and_no_section()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>()), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(Guid.NewGuid());

        result.Should().BeNull();
    }

    [Fact]
    public async Task Falls_back_to_env_vars_when_no_db_and_no_section()
    {
        using var fx = new TestAppDb();
        var enc = Substitute.For<IEncryptor>();
        var envConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PANCAKE_PAGE_ACCESS_TOKEN"] = "env_page_token",
                ["PANCAKE_PAGE_ID"] = "env_page_123",
                ["PANCAKE_WEBHOOK_SECRET"] = "env_secret",
            })
            .Build();

        var sut = new PancakeConfigResolver(fx.Db, enc, envConfig, NullLogger<PancakeConfigResolver>.Instance);
        var result = await sut.ResolveAsync(Guid.NewGuid());

        result.Should().NotBeNull();
        result!.AccessToken.Should().Be("env_page_token");
        result.PageId.Should().Be("env_page_123");
        result.WebhookSecret.Should().Be("env_secret");
    }

    [Fact]
    public async Task Inactive_db_row_still_resolves_endpoint_template_without_tenant_token()
    {
        // AgentService send uses page token from inbox; pancake_configs may be is_active=0
        // but still must yield BaseUrl/SendPathTemplate so SendAsync does not fail early.
        using var fx = new TestAppDb();
        var row = PancakeConfig.Create(fx.TenantId, Now);
        row.UpdateEndpoint("https://pancake.vn", "/pages/{page_id}/conversations/{thread_id}/messages", "query", Now);
        row.Deactivate(Now);
        fx.Db.PancakeConfigs.Add(row);
        await fx.Db.SaveChangesAsync();

        var enc = Substitute.For<IEncryptor>();
        var sut = new PancakeConfigResolver(fx.Db, enc, Config(new Dictionary<string, string?>
        {
            ["Channels:Pancake:BaseUrl"] = "https://pages.fm/api/public_api/v1",
        }), NullLogger<PancakeConfigResolver>.Instance);

        var result = await sut.ResolveAsync(fx.TenantId);

        result.Should().NotBeNull();
        result!.BaseUrl.Should().Be("https://pages.fm/api/public_api/v1"); // legacy pancake.vn skipped
        result.AccessToken.Should().BeEmpty();
        result.AuthMode.Should().Be("query");
        result.SendPathTemplate.Should().Be("/pages/{page_id}/conversations/{thread_id}/messages");
    }
}
