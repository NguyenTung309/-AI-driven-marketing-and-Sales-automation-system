using System.Text.Json;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// Fetches like/comment counts for published Facebook posts via Graph API and stores them on the
// schedule. Pancake exposes no engagement metric, so this is the only source for those numbers.
public sealed partial class MetaEngagementSyncJob(
    AppDbContext db,
    IMetaIntegrationService meta,
    IMetaGraphClient graph,
    IClock clock,
    ILogger<MetaEngagementSyncJob> logger)
{
    private const int BatchSize = 100;

    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // NULL engagement_synced_at sorts first on SQL Server ascending, so never-synced posts refresh first.
        var schedules = await db.ContentSchedules.IgnoreQueryFilters()
            .Where(s => s.Status == "posted" && s.Platform == "facebook" && s.PostUrl != null)
            .OrderBy(s => s.EngagementSyncedAt)
            .Take(BatchSize)
            .ToListAsync(ct).ConfigureAwait(false);
        if (schedules.Count == 0)
            return;

        // Resolve the page token once per (tenant, asset) group — same page serves many posts.
        var credCache = new Dictionary<(Guid TenantId, Guid? AssetId), MetaPageCredential?>();
        foreach (var schedule in schedules)
        {
            var postId = ExtractPostId(schedule.PostUrl);
            if (postId is null)
                continue;

            var key = (schedule.TenantId, schedule.MetaAssetId);
            if (!credCache.TryGetValue(key, out var cred))
            {
                cred = await meta.ResolvePageAsync(schedule.TenantId, schedule.MetaAssetId, ct).ConfigureAwait(false);
                credCache[key] = cred;
            }
            if (cred is null)
                continue;

            try
            {
                using var doc = await graph.GetAsync(
                    schedule.TenantId,
                    postId,
                    new Dictionary<string, string?> { ["fields"] = "likes.summary(true),comments.summary(true)" },
                    cred.PageAccessToken,
                    ct).ConfigureAwait(false);
                var (likes, comments) = ReadCounts(doc.RootElement);
                schedule.SetEngagement(likes, comments, now);
                LogSynced(logger, schedule.Id, likes ?? -1, comments ?? -1);
            }
            catch (MetaGraphException ex)
            {
                // Best-effort: a bad token / deleted post must not abort the whole batch.
                LogSyncFailed(logger, schedule.Id, ex.Message);
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // FB permalink is https://www.facebook.com/{post_id}; post_id (pageid_storyid) is the tail segment.
    // ponytail: parse the permalink tail; thread PublishResult.ExternalPostId if the URL format ever diverges.
    internal static string? ExtractPostId(string? postUrl)
    {
        if (string.IsNullOrWhiteSpace(postUrl))
            return null;
        var trimmed = postUrl.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var tail = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return tail.Contains('_', StringComparison.Ordinal) ? tail : null;
    }

    internal static (int? Likes, int? Comments) ReadCounts(JsonElement root) =>
        (ReadSummaryTotal(root, "likes"), ReadSummaryTotal(root, "comments"));

    private static int? ReadSummaryTotal(JsonElement root, string edge) =>
        root.TryGetProperty(edge, out var edgeEl)
            && edgeEl.TryGetProperty("summary", out var summary)
            && summary.TryGetProperty("total_count", out var total)
            && total.TryGetInt32(out var count)
            ? count
            : null;

    [LoggerMessage(EventId = 5260, Level = LogLevel.Information,
        Message = "Synced engagement for schedule {ScheduleId}: {Likes} likes, {Comments} comments")]
    private static partial void LogSynced(ILogger logger, Guid scheduleId, int likes, int comments);

    [LoggerMessage(EventId = 5261, Level = LogLevel.Warning,
        Message = "Engagement sync failed for schedule {ScheduleId}: {Reason}")]
    private static partial void LogSyncFailed(ILogger logger, Guid scheduleId, string reason);
}
