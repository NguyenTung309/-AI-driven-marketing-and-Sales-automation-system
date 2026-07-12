using Clawbot.Agents.Core.Ads;
using Clawbot.Infrastructure.Ads;
using Clawbot.Infrastructure.Integrations.Meta;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Ads;

public sealed class AdsConnectorTests
{
    [Fact]
    public void MetaAdsConnector_ParseMetrics_parses_facebook_insights_response()
    {
        var json = """
        {
            "data": [{
                "spend": "1500.00",
                "impressions": "50000",
                "clicks": "1500",
                "actions": []
            }]
        }
        """;

        var result = MetaAdsConnector.ParseMetrics(json);

        result.Should().NotBeNull();
        result!.Spend.Should().Be(1500m);
        result.Cpl.Should().Be(1m);
        result.Ctr.Should().Be(3m);
    }

    [Fact]
    public void MetaAdsConnector_ParseMetrics_returns_null_for_empty_data()
    {
        var json = """{ "data": [] }""";

        var result = MetaAdsConnector.ParseMetrics(json);

        result.Should().BeNull();
    }

    [Fact]
    public void MetaAdsConnector_ParseMetrics_returns_null_for_malformed_json()
    {
        var result = MetaAdsConnector.ParseMetrics("not json");

        result.Should().BeNull();
    }

    [Fact]
    public void MetaAdsConnector_ParseMetrics_returns_null_for_empty_string()
    {
        MetaAdsConnector.ParseMetrics("").Should().BeNull();
        MetaAdsConnector.ParseMetrics("  ").Should().BeNull();
    }

    [Fact]
    public void TikTokAdsConnector_ParseMetrics_parses_tiktok_report_response()
    {
        var json = """
        {
            "code": 0,
            "data": {
                "list": [{
                    "metrics": {
                        "spend": 2000.50,
                        "impression": 100000,
                        "click": 5000,
                        "frequency": 2.5,
                        "ctr": 5.0,
                        "cpc": 0.40
                    }
                }]
            }
        }
        """;

        var result = TikTokAdsConnector.ParseMetrics(json);

        result.Should().NotBeNull();
        result!.Spend.Should().Be(2000.50m);
        result.Frequency.Should().Be(2.5m);
        result.Ctr.Should().Be(5.0m);
        result.Cpl.Should().Be(0.40m);
    }

    [Fact]
    public void TikTokAdsConnector_ParseMetrics_returns_null_for_missing_data()
    {
        var json = """{ "code": 0 }""";

        var result = TikTokAdsConnector.ParseMetrics(json);

        result.Should().BeNull();
    }

    [Fact]
    public void AdsConnectorResolver_returns_null_for_unknown_platform()
    {
        var resolver = new AdsConnectorResolver([]);

        resolver.Resolve("unknown").Should().BeNull();
    }

    [Fact]
    public void AdsConnectorResolver_returns_connector_for_known_platform()
    {
        var meta = new TestConnector("meta");
        var resolver = new AdsConnectorResolver([meta]);

        resolver.Resolve("meta").Should().BeSameAs(meta);
        resolver.Resolve("META").Should().BeSameAs(meta);
    }

    [Fact]
    public async Task MetaAdsConnector_uses_the_current_tenant_business_token()
    {
        var tenantId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        integrations.ResolveRootTokenAsync(tenantId, Arg.Any<CancellationToken>()).Returns("tenant-root-token");
        var graph = Substitute.For<IMetaGraphClient>();
        graph.GetAsync(
                tenantId,
                "campaign-1/insights",
                Arg.Any<IReadOnlyDictionary<string, string?>>(),
                "tenant-root-token",
                Arg.Any<CancellationToken>())
            .Returns(_ => JsonDocument.Parse("""{"data":[{"spend":"10","impressions":"100","clicks":"5"}]}"""));
        using var throttle = new AdsPlatformThrottle();
        var connector = new MetaAdsConnector(
            Options.Create(new MetaAdsOptions { Enabled = true }),
            NullLogger<MetaAdsConnector>.Instance,
            throttle,
            integrations,
            graph);

        var result = await connector.FetchMetricsAsync(tenantId, "campaign-1");

        result.Should().NotBeNull();
        result!.Spend.Should().Be(10m);
        await integrations.Received(1).ResolveRootTokenAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MetaAdsConnector_does_not_run_when_ads_automation_is_disabled()
    {
        var tenantId = Guid.NewGuid();
        var integrations = Substitute.For<IMetaIntegrationService>();
        var graph = Substitute.For<IMetaGraphClient>();
        using var throttle = new AdsPlatformThrottle();
        var connector = new MetaAdsConnector(
            Options.Create(new MetaAdsOptions { Enabled = false, AccessToken = "legacy-token" }),
            NullLogger<MetaAdsConnector>.Instance,
            throttle,
            integrations,
            graph);

        var result = await connector.FetchMetricsAsync(tenantId, "campaign-1");

        result.Should().BeNull();
        await integrations.DidNotReceiveWithAnyArgs().ResolveRootTokenAsync(default);
        await graph.DidNotReceiveWithAnyArgs().GetAsync(default, default!, default!, default!, default);
    }

    private sealed class TestConnector(string platform) : IAdsPlatformConnector
    {
        public string Platform => platform;
        public Task<AdsMetricSnapshot?> FetchMetricsAsync(Guid tenantId, string externalCampaignId, CancellationToken ct = default) => Task.FromResult<AdsMetricSnapshot?>(null);
        public Task<bool> ApplyActionAsync(Guid tenantId, string externalCampaignId, string action, decimal? newBudget, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> BuildLookalikeAsync(Guid tenantId, IReadOnlyList<string> seedContactKeys, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<bool> BuildRemarketingAsync(Guid tenantId, string audienceName, IReadOnlyList<string> contactKeys, CancellationToken ct = default) => Task.FromResult(false);
    }
}
