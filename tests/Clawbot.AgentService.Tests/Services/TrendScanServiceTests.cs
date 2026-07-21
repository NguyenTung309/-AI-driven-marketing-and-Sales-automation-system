using System.Text.Json;
using Clawbot.AgentService.Services;
using Clawbot.Domain.Content;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using NSubstitute;
using CoreResearch = Clawbot.Agents.Core.Research;

namespace Clawbot.AgentService.Tests.Services;

public sealed class TrendScanServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 22, 8, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ScanAndPersistAsync_forces_youtube_and_tiktok_disabled_while_preserving_google_settings()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var settings = new ContentTrendSettings(
            Geo: " us ",
            Google: new ContentTrendSourceSetting(Enabled: false, Url: "https://trends.example/rss"),
            YouTube: new ContentTrendSourceSetting(Enabled: true, ApiKey: "tenant-youtube-key"),
            TikTok: new ContentTrendSourceSetting(Enabled: true, Url: "https://trends.example/tiktok"));
        var encryptor = Substitute.For<IEncryptor>();
        encryptor.Decrypt("encrypted-settings").Returns(JsonSerializer.Serialize(settings, JsonOptions));
        fx.Db.SocialCredentials.Add(SocialCredential.Create(
            fx.TenantId,
            ContentTrendSettings.CredentialProvider,
            "encrypted-settings",
            Now));
        await fx.Db.SaveChangesAsync();
        var agent = Substitute.For<CoreResearch.IResearchAgent>();
        CoreResearch.ResearchScanRequest? captured = null;
        agent.ScanAsync(Arg.Any<CoreResearch.ResearchScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.ArgAt<CoreResearch.ResearchScanRequest>(0);
                return Task.FromResult<IReadOnlyList<CoreResearch.ScoredTrend>>([]);
            });
        var sut = new TrendScanService(fx.Db, encryptor, agent, new FixedClock(Now));

        await sut.ScanAndPersistAsync(fx.TenantId, "2026-W30");

        captured.Should().NotBeNull();
        captured!.Geo.Should().Be("US");
        captured.Overrides.Should().NotBeNull();
        captured.Overrides!.GoogleTrends.Should().BeEquivalentTo(
            new CoreResearch.TrendSourceOverride(false, ApiKey: null, Url: "https://trends.example/rss"));
        captured.Overrides.YouTube.Should().NotBeNull();
        captured.Overrides.YouTube!.Enabled.Should().BeFalse();
        captured.Overrides.TikTok.Should().NotBeNull();
        captured.Overrides.TikTok!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ScanAndPersistAsync_disables_hidden_sources_even_without_tenant_settings()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var agent = Substitute.For<CoreResearch.IResearchAgent>();
        CoreResearch.ResearchScanRequest? captured = null;
        agent.ScanAsync(Arg.Any<CoreResearch.ResearchScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.ArgAt<CoreResearch.ResearchScanRequest>(0);
                return Task.FromResult<IReadOnlyList<CoreResearch.ScoredTrend>>([]);
            });
        var sut = new TrendScanService(
            fx.Db,
            Substitute.For<IEncryptor>(),
            agent,
            new FixedClock(Now));

        await sut.ScanAndPersistAsync(fx.TenantId, "2026-W30");

        captured.Should().NotBeNull();
        captured!.Overrides.Should().NotBeNull();
        captured.Overrides!.GoogleTrends.Should().BeNull("Google must remain controlled by its normal default/configuration");
        captured.Overrides.YouTube.Should().BeEquivalentTo(new CoreResearch.TrendSourceOverride(Enabled: false));
        captured.Overrides.TikTok.Should().BeEquivalentTo(new CoreResearch.TrendSourceOverride(Enabled: false));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
