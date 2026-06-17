using Clawbot.Domain.Analytics;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class DailyReportJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_publishes_tenant_daily_report_from_aggregate_kpi_row()
    {
        using var fx = new TestAppDb();
        var metricDate = DateOnly.FromDateTime(Now.ToOffset(TimeSpan.FromHours(7)).DateTime).AddDays(-1);
        var kpi = KpiDaily.Create(fx.TenantId, metricDate, "all", Now);
        kpi.Record(leads: 12, dms: 8, replies: 20, conversions: 3, avgRespSec: 42.5m, adSpend: 1500000m);
        fx.Db.KpiDailies.Add(kpi);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingNotificationPublisher();
        var sut = new DailyReportJob(
            fx.Db,
            publisher,
            new FixedClock(Now),
            NullLogger<DailyReportJob>.Instance);

        await sut.RunAsync(CancellationToken.None);

        var request = publisher.Requests.Should().ContainSingle().Subject;
        request.TenantId.Should().Be(fx.TenantId);
        request.UserId.Should().BeNull();
        request.Type.Should().Be("daily_report");
        request.Title.Should().Be("Báo cáo ngày 14/06/2026");
        request.Severity.Should().Be("info");
        request.Link.Should().Be("/analytics");
        request.Body.Should().Contain("12 lead");
        request.Body.Should().Contain("8 hội thoại");
        request.Body.Should().Contain("20 phản hồi");
        request.Body.Should().Contain("3 chuyển đổi");
        request.Body.Should().Contain("42.5s");
        request.Body.Should().Contain("1,500,000");
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
