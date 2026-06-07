using Clawbot.Agents.Contracts.Research;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

public interface IWeeklyTrendScanner
{
    Task<IReadOnlyList<ContentTrendBrief>> ScanAsync(Guid tenantId, string weekOf, CancellationToken ct = default);
}

public sealed class GrpcWeeklyTrendScanner(ResearchAgent.ResearchAgentClient client) : IWeeklyTrendScanner
{
    private readonly ResearchAgent.ResearchAgentClient _client = client;

    public async Task<IReadOnlyList<ContentTrendBrief>> ScanAsync(
        Guid tenantId,
        string weekOf,
        CancellationToken ct = default)
    {
        var response = await _client.WeeklyTrendsAsync(
            new TrendRequest
            {
                TenantId = tenantId.ToString(),
                WeekOf = weekOf,
            },
            cancellationToken: ct).ResponseAsync.ConfigureAwait(false);

        return response.Trends
            .Select(t => new ContentTrendBrief(
                weekOf,
                t.Topic,
                t.Source,
                t.Metric,
                t.RelevanceScore,
                t.ContentIdeas.ToList()))
            .ToList();
    }
}

public sealed partial class WeeklyTrendScanJob(
    AppDbContext db,
    IWeeklyTrendScanner scanner,
    IContentNotifier notifier,
    IClock clock,
    ILogger<WeeklyTrendScanJob> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IWeeklyTrendScanner _scanner = scanner;
    private readonly IContentNotifier _notifier = notifier;
    private readonly IClock _clock = clock;
    private readonly ILogger<WeeklyTrendScanJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 3600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var weekOf = ContentTrendBriefFormatter.CurrentWeekOf(_clock.UtcNow);
        var tenantIds = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.IsActive)
            .Select(t => t.Id)
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var tenantId in tenantIds)
        {
            try
            {
                var trends = await _scanner.ScanAsync(tenantId, weekOf, ct).ConfigureAwait(false);
                await _notifier.NotifyTrendScanAsync(
                    tenantId,
                    new ContentTrendScanEvent(tenantId, trends.Count, _clock.UtcNow),
                    ct).ConfigureAwait(false);
                LogTenantScanned(_logger, tenantId, weekOf, trends.Count);
            }
            catch (Exception ex)
            {
                LogTenantScanFailed(_logger, tenantId, weekOf, ex.Message, ex);
            }
        }
    }

    [LoggerMessage(
        EventId = 5101,
        Level = LogLevel.Information,
        Message = "Weekly content trend scan completed for tenant {TenantId} week {WeekOf} (trends={TrendCount})")]
    private static partial void LogTenantScanned(ILogger logger, Guid tenantId, string weekOf, int trendCount);

    [LoggerMessage(
        EventId = 5102,
        Level = LogLevel.Warning,
        Message = "Weekly content trend scan failed for tenant {TenantId} week {WeekOf} ({Reason})")]
    private static partial void LogTenantScanFailed(ILogger logger, Guid tenantId, string weekOf, string reason, Exception exception);
}
