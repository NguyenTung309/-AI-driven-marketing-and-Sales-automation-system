using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ChannelHealthServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetPancakeAsync_returns_three_ok_checks_for_active_config_with_credentials()
    {
        using var fx = new TestApiAppDb(TenantId);
        var config = PancakeConfig.Create(TenantId, Now);
        config.UpdateAccessToken("encrypted-access-token", Now);
        config.UpdateWebhookSecret("encrypted-webhook-secret", Now);
        fx.Db.PancakeConfigs.Add(config);
        await fx.Db.SaveChangesAsync();
        var sut = new ChannelHealthService(fx.Db);

        var result = await sut.GetPancakeAsync(CancellationToken.None);

        result.Status.Should().Be("ok");
        result.Adapter.Should().Be("PancakeChannelAdapter");
        result.ConfiguredTenants.Should().Be(1);
        result.Checks.Should().HaveCount(3);
        result.Checks.Select(c => c.Name).Should().BeEquivalentTo("config", "outbound", "webhook");
        result.Checks.Should().OnlyContain(c => c.Status == "ok");
    }

    [Fact]
    public async Task GetPancakeAsync_reports_degraded_when_webhook_secret_is_missing()
    {
        using var fx = new TestApiAppDb(TenantId);
        var config = PancakeConfig.Create(TenantId, Now);
        config.UpdateAccessToken("encrypted-access-token", Now);
        fx.Db.PancakeConfigs.Add(config);
        await fx.Db.SaveChangesAsync();
        var sut = new ChannelHealthService(fx.Db);

        var result = await sut.GetPancakeAsync(CancellationToken.None);

        result.Status.Should().Be("degraded");
        result.Checks.Should().ContainSingle(c => c.Name == "webhook")
            .Which.Status.Should().Be("degraded");
    }

    [Fact]
    public async Task GetPancakeAsync_reports_degraded_when_no_channel_config_exists()
    {
        using var fx = new TestApiAppDb(TenantId);
        var sut = new ChannelHealthService(fx.Db);

        var result = await sut.GetPancakeAsync(CancellationToken.None);

        result.Status.Should().Be("degraded");
        result.TotalConfigs.Should().Be(0);
        result.ConfiguredTenants.Should().Be(0);
        result.Checks.Should().HaveCount(3);
    }
}
