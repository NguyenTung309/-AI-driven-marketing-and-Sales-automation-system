using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Content;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class KbAccuracyTestJobTests
{
    [Fact]
    public async Task RunAsync_alerts_deployed_kb_versions_with_accuracy_below_85_percent()
    {
        using var fx = new TestAppDb();
        var low = AddDeployedVersion(fx, "HSK", 84m);
        AddDeployedVersion(fx, "SALE", 85m);
        var notifier = new RecordingContentNotifier();
        var sut = new KbAccuracyTestJob(fx.Db, notifier, NullLogger<KbAccuracyTestJob>.Instance);

        await sut.RunAsync(CancellationToken.None);

        var alert = notifier.Alerts.Should().ContainSingle().Subject;
        alert.tenantId.Should().Be(fx.TenantId);
        alert.evt.AlertType.Should().Be("kb_accuracy");
        alert.evt.Platform.Should().Be("kb");
        alert.evt.Metric.Should().Be("HSK");
        alert.evt.Severity.Should().Be("warning");
        alert.evt.Message.Should().Contain("84%");

        low.AccuracyScore.Should().Be(84m);
    }

    private static KbVersion AddDeployedVersion(TestAppDb fx, string code, decimal accuracy)
    {
        var module = KbModule.Create(fx.TenantId, code, $"{code} module", DateTimeOffset.UtcNow);
        var version = KbVersion.Create(module.Id, 1, $"# {code}", DateTimeOffset.UtcNow);
        version.Deploy(DateTimeOffset.UtcNow);
        version.RecordAccuracy(accuracy);

        fx.Db.AddRange(module, version);
        fx.Db.SaveChanges();
        return version;
    }

    private sealed class RecordingContentNotifier : IContentNotifier
    {
        public List<(Guid tenantId, AnalyticsAlertEvent evt)> Alerts { get; } = new();

        public Task NotifyTrendScanAsync(Guid tenantId, ContentTrendScanEvent evt, CancellationToken ct = default)
        {
            _ = tenantId;
            _ = evt;
            _ = ct;
            return Task.CompletedTask;
        }

        public Task NotifyPublishFailedAsync(Guid tenantId, ContentPublishFailedEvent evt, CancellationToken ct = default)
        {
            _ = tenantId;
            _ = evt;
            _ = ct;
            return Task.CompletedTask;
        }

        public Task NotifyAnalyticsAlertAsync(Guid tenantId, AnalyticsAlertEvent evt, CancellationToken ct = default)
        {
            _ = ct;
            Alerts.Add((tenantId, evt));
            return Task.CompletedTask;
        }
    }
}
