using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class WeeklyTrendScanJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 4, 18, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_scans_active_tenants_for_current_gmt7_week_and_notifies()
    {
        using var fx = new TestAppDb();
        var active = Tenant.Create("active", "Active", "free", Now);
        var inactive = Tenant.Create("inactive", "Inactive", "free", Now);
        fx.Db.Tenants.AddRange(active, inactive);
        fx.Db.Entry(inactive).Property(nameof(Tenant.IsActive)).CurrentValue = false;
        await fx.Db.SaveChangesAsync();

        var scanner = new RecordingWeeklyTrendScanner();
        var notifier = Substitute.For<IContentNotifier>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var job = new WeeklyTrendScanJob(
            fx.Db,
            scanner,
            notifier,
            clock,
            NullLogger<WeeklyTrendScanJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        scanner.Calls.Should().ContainSingle()
            .Which.Should().Be((active.Id, "2026-W02"));
        await notifier.Received(1).NotifyTrendScanAsync(
            active.Id,
            Arg.Is<ContentTrendScanEvent>(e =>
                e.TenantId == active.Id
                && e.TrendCount == 1
                && e.OccurredAt == Now),
            Arg.Any<CancellationToken>());
    }

    private sealed class RecordingWeeklyTrendScanner : IWeeklyTrendScanner
    {
        public List<(Guid TenantId, string WeekOf)> Calls { get; } = [];

        public Task<IReadOnlyList<ContentTrendBrief>> ScanAsync(
            Guid tenantId,
            string weekOf,
            CancellationToken ct = default)
        {
            _ = ct;
            Calls.Add((tenantId, weekOf));
            IReadOnlyList<ContentTrendBrief> trends =
            [
                new("2026-W02", "HSK listening", "youtube", "100 views", 11d, []),
            ];
            return Task.FromResult(trends);
        }
    }
}
