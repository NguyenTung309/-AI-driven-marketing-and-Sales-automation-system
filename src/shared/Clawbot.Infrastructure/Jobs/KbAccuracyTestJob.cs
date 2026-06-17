using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// M04 NFR-05: daily KB accuracy watch. Flags deployed KB versions whose recorded
// AccuracyScore is below the 85% threshold and alerts ops via SignalR.
// Scores are recorded by the on-demand KB test run (KbEndpoints, RAG+LLM wired in W5);
// this job is the monitoring/alert layer. Real scores require the real embedder + KB content.
public sealed partial class KbAccuracyTestJob(
    AppDbContext db,
    IContentNotifier notifier,
    ILogger<KbAccuracyTestJob> logger)
{
    private const decimal Threshold = 85m;

    private readonly AppDbContext _db = db;
    private readonly IContentNotifier _notifier = notifier;
    private readonly ILogger<KbAccuracyTestJob> _logger = logger;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var below = await (
            from v in _db.KbVersions.IgnoreQueryFilters()
            join m in _db.KbModules.IgnoreQueryFilters() on v.KbModuleId equals m.Id
            where v.Status == "deployed" && v.AccuracyScore != null && v.AccuracyScore < Threshold
            select new { m.TenantId, m.Code, Score = v.AccuracyScore!.Value })
            .ToListAsync(ct).ConfigureAwait(false);

        foreach (var row in below)
        {
            await _notifier.NotifyAnalyticsAlertAsync(row.TenantId, new AnalyticsAlertEvent(
                row.TenantId,
                AlertType: "kb_accuracy",
                Platform: "kb",
                Metric: row.Code,
                Severity: "warning",
                Message: $"KB module {row.Code} accuracy {row.Score:0.##}% is below the 85% threshold.",
                OccurredAt: DateTimeOffset.UtcNow), ct).ConfigureAwait(false);
            LogLowAccuracy(_logger, row.Code, row.Score);
        }

        LogChecked(_logger, below.Count);
    }

    [LoggerMessage(EventId = 5301, Level = LogLevel.Warning, Message = "KB module {Code} accuracy {Score:0.##}% below threshold")]
    private static partial void LogLowAccuracy(ILogger logger, string code, decimal score);

    [LoggerMessage(EventId = 5302, Level = LogLevel.Information, Message = "KB accuracy check flagged {Count} deployed module(s)")]
    private static partial void LogChecked(ILogger logger, int count);
}
