using Clawbot.Agents.Core.Ads;
using Clawbot.Domain.Ads;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class AdsLookalikeRefreshJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_notifies_when_connector_returns_no_lookalike_audience()
    {
        using var fx = new TestAppDb();
        var campaign = AdsCampaign.Create(fx.TenantId, "meta", "campaign-1", Now);
        campaign.Resume(Now);
        fx.Db.AdsCampaigns.Add(campaign);
        for (var i = 0; i < 100; i++)
        {
            var contact = Contact.Create(fx.TenantId, $"Lead {i}", Now);
            fx.Db.Contacts.Add(contact);
            fx.Db.Entry(contact).Property(nameof(Contact.Phone)).CurrentValue = $"090{i:0000000}";
            var lead = Lead.Create(fx.TenantId, contact.Id, "facebook", Now);
            lead.AdjustScore(80, "qualified seed", Now);
            fx.Db.Leads.Add(lead);
        }

        await fx.Db.SaveChangesAsync();
        var connector = new FixedAdsConnector("meta", audienceId: null);
        var publisher = new RecordingNotificationPublisher();
        var sut = new AdsLookalikeRefreshJob(
            fx.Db,
            new FixedAdsConnectorResolver(connector),
            publisher,
            NullLogger<AdsLookalikeRefreshJob>.Instance);

        await sut.RunAsync();

        connector.SeedCalls.Should().Equal(100);
        var request = publisher.Requests.Should().ContainSingle().Which;
        request.TenantId.Should().Be(fx.TenantId);
        request.Type.Should().Be("ads_lookalike_failed");
        request.Severity.Should().Be("warning");
        request.Title.Should().Contain("meta");
        request.Body.Should().Contain("100");
        request.Link.Should().Be("/ads");
    }

    [Fact]
    public async Task RunAsync_counts_seed_contacts_per_tenant()
    {
        using var fx = new TestAppDb();
        var otherTenantId = Guid.NewGuid();
        var campaign = AdsCampaign.Create(fx.TenantId, "meta", "campaign-1", Now);
        campaign.Resume(Now);
        fx.Db.AdsCampaigns.Add(campaign);
        SeedHotContacts(fx, fx.TenantId, count: 99, phonePrefix: "090");
        SeedHotContacts(fx, otherTenantId, count: 1, phonePrefix: "091");
        await fx.Db.SaveChangesAsync();
        var connector = new FixedAdsConnector("meta", audienceId: null);
        var publisher = new RecordingNotificationPublisher();
        var sut = new AdsLookalikeRefreshJob(
            fx.Db,
            new FixedAdsConnectorResolver(connector),
            publisher,
            NullLogger<AdsLookalikeRefreshJob>.Instance);

        await sut.RunAsync();

        connector.SeedCalls.Should().BeEmpty();
        publisher.Requests.Should().BeEmpty();
    }

    private static void SeedHotContacts(TestAppDb fx, Guid tenantId, int count, string phonePrefix)
    {
        for (var i = 0; i < count; i++)
        {
            var contact = Contact.Create(tenantId, $"{phonePrefix} Lead {i}", Now);
            fx.Db.Contacts.Add(contact);
            fx.Db.Entry(contact).Property(nameof(Contact.Phone)).CurrentValue = $"{phonePrefix}{i:0000000}";
            var lead = Lead.Create(tenantId, contact.Id, "facebook", Now);
            lead.AdjustScore(80, "qualified seed", Now);
            fx.Db.Leads.Add(lead);
        }
    }

    private sealed class FixedAdsConnectorResolver(IAdsPlatformConnector connector) : IAdsConnectorResolver
    {
        public IAdsPlatformConnector? Resolve(string platform) =>
            string.Equals(platform, connector.Platform, StringComparison.OrdinalIgnoreCase) ? connector : null;
    }

    private sealed class FixedAdsConnector(string platform, string? audienceId) : IAdsPlatformConnector
    {
        public string Platform { get; } = platform;
        public List<int> SeedCalls { get; } = [];

        public Task<AdsMetricSnapshot?> FetchMetricsAsync(Guid tenantId, string externalCampaignId, CancellationToken ct = default) =>
            Task.FromResult<AdsMetricSnapshot?>(null);

        public Task<bool> ApplyActionAsync(Guid tenantId, string externalCampaignId, string action, decimal? newBudget, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<string?> BuildLookalikeAsync(Guid tenantId, IReadOnlyList<string> seedContactKeys, CancellationToken ct = default)
        {
            SeedCalls.Add(seedContactKeys.Count);
            return Task.FromResult(audienceId);
        }

        public Task<bool> BuildRemarketingAsync(Guid tenantId, string audienceName, IReadOnlyList<string> contactKeys, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public List<NotificationRequest> Requests { get; } = [];

        public Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
