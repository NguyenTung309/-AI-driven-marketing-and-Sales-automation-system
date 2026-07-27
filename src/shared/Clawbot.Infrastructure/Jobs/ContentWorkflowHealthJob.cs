using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Jobs;

public sealed class ContentWorkflowHealthOptions
{
    public const string SectionName = "Content:WorkflowHealth";

    /// <summary>Alert when any tenant has a pending/leased review task older than this many minutes.</summary>
    public int ReviewTaskAgeWarnMinutes { get; set; } = 30;

    /// <summary>Alert when held schedule intents exceed this count globally.</summary>
    public int HeldScheduleWarnCount { get; set; } = 25;

    /// <summary>Alert when outcome_unknown attempts exceed this count globally.</summary>
    public int OutcomeUnknownWarnCount { get; set; } = 5;

    /// <summary>Cooldown-dupe key window for Warning logs (minutes).</summary>
    public int AlertCooldownMinutes { get; set; } = 30;
}

/// <summary>
/// Phase 6.7 durable health: every 5 minutes scan tenant-scoped workflow debt and emit Warning+ logs
/// (SystemLogSink persists Warning+ into system_logs for admin UI).
/// </summary>
public sealed partial class ContentWorkflowHealthJob(
    AppDbContext db,
    IContentWorkflowRuntimeGate runtimeGate,
    IClock clock,
    IMemoryCache cache,
    IOptions<ContentWorkflowHealthOptions> options,
    ILogger<ContentWorkflowHealthJob> logger)
{
    public const string AlertCooldownCacheKey = "content.workflow.health.alert_cooldown";

    [DisableConcurrentExecution(timeoutInSeconds: 240)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var opts = options.Value;
        var gate = await runtimeGate.GetAsync(ct).ConfigureAwait(false);

        var reviewAgeCutoff = now.AddMinutes(-Math.Max(1, opts.ReviewTaskAgeWarnMinutes));
        var oldReviewTasks = await db.ContentReviewTasks.IgnoreQueryFilters()
            .CountAsync(
                t => (t.Status == ContentReviewTask.StatusPending || t.Status == ContentReviewTask.StatusLeased)
                    && t.CreatedAt <= reviewAgeCutoff,
                ct)
            .ConfigureAwait(false);

        var heldSchedules = await db.ContentSchedules.IgnoreQueryFilters()
            .CountAsync(s => s.Status == ContentSchedule.StatusHeld, ct)
            .ConfigureAwait(false);

        var outcomeUnknown = await db.ContentSchedules.IgnoreQueryFilters()
            .CountAsync(s => s.Status == ContentSchedule.StatusOutcomeUnknown, ct)
            .ConfigureAwait(false);

        LogSnapshot(
            logger,
            gate.PublicationPaused,
            gate.MinimumWriterVersion,
            oldReviewTasks,
            heldSchedules,
            outcomeUnknown);

        var shouldAlert =
            oldReviewTasks > 0
            || heldSchedules >= Math.Max(1, opts.HeldScheduleWarnCount)
            || outcomeUnknown >= Math.Max(1, opts.OutcomeUnknownWarnCount)
            || gate.PublicationPaused;

        if (!shouldAlert)
            return;

        var cooldown = TimeSpan.FromMinutes(Math.Max(1, opts.AlertCooldownMinutes));
        if (cache.TryGetValue(AlertCooldownCacheKey, out DateTimeOffset lastAlertAt)
            && now - lastAlertAt < cooldown)
        {
            return;
        }

        cache.Set(AlertCooldownCacheKey, now, cooldown);
        LogAlert(
            logger,
            gate.PublicationPaused,
            gate.MinimumWriterVersion,
            oldReviewTasks,
            heldSchedules,
            outcomeUnknown,
            opts.ReviewTaskAgeWarnMinutes,
            opts.HeldScheduleWarnCount,
            opts.OutcomeUnknownWarnCount);
    }

    [LoggerMessage(
        EventId = 5610,
        Level = LogLevel.Information,
        Message = "Content workflow health: paused={Paused} minWriter={MinWriter} oldReviewTasks={OldReviewTasks} held={Held} outcomeUnknown={OutcomeUnknown}")]
    private static partial void LogSnapshot(
        ILogger logger,
        bool paused,
        int minWriter,
        int oldReviewTasks,
        int held,
        int outcomeUnknown);

    [LoggerMessage(
        EventId = 5611,
        Level = LogLevel.Warning,
        Message = "Content workflow debt: paused={Paused} minWriter={MinWriter} oldReviewTasks={OldReviewTasks} (>{AgeMinutes}m) held={Held} (warn>={HeldWarn}) outcomeUnknown={OutcomeUnknown} (warn>={UnknownWarn})")]
    private static partial void LogAlert(
        ILogger logger,
        bool paused,
        int minWriter,
        int oldReviewTasks,
        int held,
        int outcomeUnknown,
        int ageMinutes,
        int heldWarn,
        int unknownWarn);
}
