using System.Globalization;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public sealed partial class DailyReportJob(
    AppDbContext db,
    INotificationPublisher publisher,
    IClock clock,
    ILogger<DailyReportJob> logger)
{
    private static readonly TimeSpan AnalyticsOffset = TimeSpan.FromHours(7);

    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [1800, 1800, 1800])]
    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var metricDate = DateOnly.FromDateTime(clock.UtcNow.ToOffset(AnalyticsOffset).DateTime).AddDays(-1);
        var rows = await db.KpiDailies.IgnoreQueryFilters()
            .Where(k => k.Date == metricDate && k.Platform == "all")
            .Select(k => new
            {
                k.TenantId,
                k.Leads,
                k.Dms,
                k.Replies,
                k.Conversions,
                k.AvgResponseTimeSec,
                k.AdSpend,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var row in rows)
        {
            await publisher.PublishAsync(new NotificationRequest(
                row.TenantId,
                UserId: null,
                Type: "daily_report",
                Title: $"Báo cáo ngày {metricDate:dd/MM/yyyy}",
                Severity: "info",
                Body: BuildBody(row.Leads, row.Dms, row.Replies, row.Conversions, row.AvgResponseTimeSec, row.AdSpend),
                Link: "/analytics"), ct).ConfigureAwait(false);
        }

        LogDailyReportPushed(logger, rows.Count, metricDate);
    }

    private static string BuildBody(
        int leads,
        int dms,
        int replies,
        int conversions,
        decimal? avgResponseTimeSec,
        decimal? adSpend)
    {
        var avg = avgResponseTimeSec is null
            ? "n/a"
            : $"{avgResponseTimeSec.Value.ToString("0.#", CultureInfo.InvariantCulture)}s";
        var spend = adSpend is null
            ? "n/a"
            : adSpend.Value.ToString("#,0", CultureInfo.InvariantCulture);

        return $"{leads} lead, {dms} hội thoại, {replies} phản hồi, {conversions} chuyển đổi. " +
            $"Thời gian phản hồi TB {avg}. Chi tiêu quảng cáo {spend}.";
    }

    [LoggerMessage(EventId = 5004, Level = LogLevel.Information,
        Message = "Daily report notifications pushed ({Count}) for {Day}")]
    private static partial void LogDailyReportPushed(ILogger logger, int count, DateOnly day);
}
