using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// Fetches like/comment counts for published Facebook and Instagram posts via Graph API and stores
// them on the schedule. Pancake exposes no engagement metric, so Graph is the source for these values.
public sealed partial class MetaEngagementSyncJob(
    AppDbContext db,
    IMetaIntegrationService meta,
    IMetaGraphClient graph,
    IInstagramCredentialResolver instagramCredentials,
    IClock clock,
    ILogger<MetaEngagementSyncJob> logger)
{
    private const int BatchSize = 100;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        // NULL engagement_synced_at sorts first on SQL Server ascending, so never-synced posts refresh first.
        var schedules = await db.ContentSchedules.IgnoreQueryFilters()
            .Where(s => s.Status == "posted"
                && (s.Platform == "facebook" || s.Platform == "instagram")
                && (s.ExternalPostId != null || s.PostUrl != null))
            .OrderBy(s => s.EngagementSyncedAt)
            .Take(BatchSize)
            .ToListAsync(ct).ConfigureAwait(false);

        var synced = 0;
        var skipped = 0;
        var failed = 0;
        var pageCredentialCache = new Dictionary<(Guid TenantId, Guid? AssetId, string? LegacyPageId), MetaPageCredential?>();
        var instagramCredentialCache = new Dictionary<(Guid TenantId, Guid? AssetId), MetaInstagramCredential?>();
        var standaloneInstagramCredentialCache = new Dictionary<(Guid TenantId, string TargetId), InstagramCredential?>();

        foreach (var schedule in schedules)
        {
            // Advance the attempt watermark even when a row is invalid or temporarily unavailable;
            // otherwise a permanent bad row can occupy the first 100 slots forever.
            schedule.MarkEngagementAttempt(now);
            var platform = schedule.Platform.Trim().ToLowerInvariant();
            var rawObjectId = platform == "facebook"
                ? schedule.ExternalPostId ?? ExtractPostId(schedule.PostUrl)
                : schedule.ExternalPostId;
            var objectId = IsSafeGraphObjectId(rawObjectId) ? rawObjectId : null;
            if (objectId is null)
            {
                skipped++;
                LogSkipped(logger, schedule.Id, schedule.TenantId, platform, "no_post_id");
                continue;
            }

            try
            {
                if (platform == "facebook")
                {
                    var legacyPageId = schedule.MetaAssetId.HasValue ? null : ExtractPageId(objectId);
                    var key = (schedule.TenantId, schedule.MetaAssetId, legacyPageId);
                    if (!pageCredentialCache.TryGetValue(key, out var credential))
                    {
                        credential = schedule.MetaAssetId.HasValue
                            ? await meta.ResolvePageForEngagementAsync(
                                schedule.TenantId,
                                schedule.MetaAssetId,
                                ct).ConfigureAwait(false)
                            : await meta.ResolvePageForEngagementByExternalIdAsync(
                                schedule.TenantId,
                                ExtractPageId(objectId),
                                ct).ConfigureAwait(false);
                        pageCredentialCache[key] = credential;
                    }

                    if (credential is null)
                    {
                        skipped++;
                        LogSkipped(logger, schedule.Id, schedule.TenantId, platform, "no_page_credential");
                        continue;
                    }

                    using var doc = await graph.GetAsync(
                        schedule.TenantId,
                        objectId,
                        new Dictionary<string, string?>
                        {
                            ["fields"] = FacebookEngagementFields,
                        },
                        credential.PageAccessToken,
                        ct).ConfigureAwait(false);
                    var (likes, comments) = ReadFacebookCounts(doc.RootElement);
                    if (likes is null || comments is null)
                    {
                        failed++;
                        LogSyncFailed(logger, schedule.Id, schedule.TenantId, platform, objectId, "missing_metric_field");
                        continue;
                    }

                    var reactions = ReadFacebookReactions(doc.RootElement);
                    schedule.SetFacebookEngagement(
                        likes,
                        comments,
                        reactions.Total,
                        reactions.Love,
                        reactions.Haha,
                        reactions.Wow,
                        reactions.Sad,
                        reactions.Angry,
                        reactions.Care,
                        now);
                    synced++;
                    LogSynced(logger, schedule.Id, platform, objectId, likes.Value, comments.Value);
                    continue;
                }

                string accessToken;
                if (schedule.MetaAssetId.HasValue)
                {
                    var instagramKey = (schedule.TenantId, schedule.MetaAssetId);
                    if (!instagramCredentialCache.TryGetValue(instagramKey, out var instagramCredential))
                    {
                        var resolution = await meta.ResolveInstagramForEngagementAsync(
                            schedule.TenantId,
                            schedule.MetaAssetId,
                            ct).ConfigureAwait(false);
                        instagramCredential = resolution.Credential;
                        instagramCredentialCache[instagramKey] = instagramCredential;
                        if (instagramCredential is null)
                        {
                            skipped++;
                            LogSkipped(
                                logger,
                                schedule.Id,
                                schedule.TenantId,
                                platform,
                                $"instagram_{resolution.Status.ToString().ToLowerInvariant()}");
                            continue;
                        }
                    }

                    if (instagramCredential is null)
                    {
                        skipped++;
                        LogSkipped(logger, schedule.Id, schedule.TenantId, platform, "no_instagram_credential");
                        continue;
                    }

                    accessToken = instagramCredential.PageAccessToken;
                }
                else if (!string.IsNullOrWhiteSpace(schedule.ProviderTargetId))
                {
                    var targetId = schedule.ProviderTargetId.Trim();
                    var standaloneKey = (schedule.TenantId, targetId);
                    if (!standaloneInstagramCredentialCache.TryGetValue(standaloneKey, out var standaloneCredential))
                    {
                        var resolution = await instagramCredentials.ResolveAsync(schedule.TenantId, ct).ConfigureAwait(false);
                        standaloneCredential = resolution.Status == InstagramCredentialResolutionStatus.Resolved
                            && resolution.Credential is not null
                            && string.Equals(resolution.Credential.InstagramUserId, targetId, StringComparison.Ordinal)
                                ? resolution.Credential
                                : null;
                        standaloneInstagramCredentialCache[standaloneKey] = standaloneCredential;
                    }

                    if (standaloneCredential is null)
                    {
                        skipped++;
                        LogSkipped(logger, schedule.Id, schedule.TenantId, platform, "no_standalone_instagram_credential");
                        continue;
                    }

                    accessToken = standaloneCredential.AccessToken;
                }
                else
                {
                    skipped++;
                    LogSkipped(logger, schedule.Id, schedule.TenantId, platform, "instagram_target_missing");
                    continue;
                }

                using (var doc = await graph.GetAsync(
                    schedule.TenantId,
                    objectId,
                    new Dictionary<string, string?>
                    {
                        ["fields"] = "like_count,comments_count",
                    },
                    accessToken,
                    ct).ConfigureAwait(false))
                {
                    var (likes, comments) = ReadInstagramCounts(doc.RootElement);
                    if (likes is null || comments is null)
                    {
                        failed++;
                        LogSyncFailed(logger, schedule.Id, schedule.TenantId, platform, objectId, "missing_metric_field");
                        continue;
                    }

                    schedule.SetEngagement(likes, comments, now);
                    synced++;
                    LogSynced(logger, schedule.Id, platform, objectId, likes.Value, comments.Value);
                }
            }
            catch (MetaGraphException ex)
            {
                failed++;
                LogSyncFailed(logger, schedule.Id, schedule.TenantId, platform, objectId, GetGraphFailureReason(ex));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                LogUnexpectedFailure(logger, ex, schedule.Id, schedule.TenantId, platform, objectId);
            }
        }

        if (schedules.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogBatch(logger, schedules.Count, synced, skipped, failed);
    }

    // FB permalink is https://www.facebook.com/{post_id}; post_id (pageid_storyid) is the tail segment.
    // Legacy rows use this fallback; new rows use ContentSchedule.ExternalPostId.
    public static string? ExtractPostId(string? postUrl)
    {
        if (string.IsNullOrWhiteSpace(postUrl))
            return null;
        var trimmed = postUrl.TrimEnd('/');
        var slash = trimmed.LastIndexOf('/');
        var tail = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return IsSafeGraphObjectId(tail) && tail.Contains('_', StringComparison.Ordinal) ? tail : null;
    }

    public static bool IsSafeGraphObjectId(string? objectId) =>
        !string.IsNullOrWhiteSpace(objectId)
        && objectId.Length <= ContentSchedule.MaxExternalPostIdLength
        && objectId.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    public static string ExtractPageId(string postId)
    {
        var separator = postId.IndexOf('_', StringComparison.Ordinal);
        return separator > 0 ? postId[..separator] : postId;
    }

    // Edge "likes" chi dem reaction LIKE; tong moi loai nam o edge "reactions".
    // Moi loai duoc alias rieng (limit(0) de khong keo ve danh sach nguoi tha reaction).
    internal const string FacebookEngagementFields =
        "likes.summary(true),comments.summary(true),reactions.summary(true)"
        + ",reactions.type(LOVE).limit(0).summary(total_count).as(love)"
        + ",reactions.type(HAHA).limit(0).summary(total_count).as(haha)"
        + ",reactions.type(WOW).limit(0).summary(total_count).as(wow)"
        + ",reactions.type(SAD).limit(0).summary(total_count).as(sad)"
        + ",reactions.type(ANGRY).limit(0).summary(total_count).as(angry)"
        + ",reactions.type(CARE).limit(0).summary(total_count).as(care)";

    internal readonly record struct FacebookReactionBreakdown(
        int? Total,
        int? Love,
        int? Haha,
        int? Wow,
        int? Sad,
        int? Angry,
        int? Care);

    // Thieu edge => null chu khong phai 0: bai dong bo truoc khi co reaction khong duoc bao la "0 cam xuc".
    internal static FacebookReactionBreakdown ReadFacebookReactions(JsonElement root) =>
        new(
            ReadSummaryTotal(root, "reactions"),
            ReadSummaryTotal(root, "love"),
            ReadSummaryTotal(root, "haha"),
            ReadSummaryTotal(root, "wow"),
            ReadSummaryTotal(root, "sad"),
            ReadSummaryTotal(root, "angry"),
            ReadSummaryTotal(root, "care"));

    internal static (int? Likes, int? Comments) ReadCounts(JsonElement root) =>
        ReadFacebookCounts(root);

    internal static (int? Likes, int? Comments) ReadFacebookCounts(JsonElement root) =>
        (ReadSummaryTotal(root, "likes"), ReadSummaryTotal(root, "comments"));

    internal static (int? Likes, int? Comments) ReadInstagramCounts(JsonElement root) =>
        (ReadScalarCount(root, "like_count"), ReadScalarCount(root, "comments_count"));

    private static int? ReadSummaryTotal(JsonElement root, string edge) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(edge, out var edgeEl)
            && edgeEl.ValueKind == JsonValueKind.Object
            && edgeEl.TryGetProperty("summary", out var summary)
            && summary.ValueKind == JsonValueKind.Object
            && summary.TryGetProperty("total_count", out var total)
            && total.TryGetInt32(out var count)
            && count >= 0
            ? count
            : null;

    private static int? ReadScalarCount(JsonElement root, string propertyName) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(propertyName, out var value)
            && value.TryGetInt32(out var count)
            && count >= 0
            ? count
            : null;

    private static string GetGraphFailureReason(MetaGraphException exception) =>
        exception.Code is { } code
            ? $"graph_code:{code}"
            : exception.HttpStatus is { } status
                ? $"http_status:{status}"
                : "graph_error";

    [LoggerMessage(EventId = 5260, Level = LogLevel.Information,
        Message = "Synced engagement for schedule {ScheduleId}, platform {Platform}, object {ObjectId}: {Likes} likes, {Comments} comments")]
    private static partial void LogSynced(
        ILogger logger,
        Guid scheduleId,
        string platform,
        string objectId,
        int likes,
        int comments);

    [LoggerMessage(EventId = 5261, Level = LogLevel.Warning,
        Message = "Engagement sync failed for schedule {ScheduleId}, tenant {TenantId}, platform {Platform}, object {ObjectId}: {Reason}")]
    private static partial void LogSyncFailed(
        ILogger logger,
        Guid scheduleId,
        Guid tenantId,
        string platform,
        string objectId,
        string reason);

    [LoggerMessage(EventId = 5262, Level = LogLevel.Warning,
        Message = "Engagement sync skipped for schedule {ScheduleId}, tenant {TenantId}, platform {Platform}: {Reason}")]
    private static partial void LogSkipped(
        ILogger logger,
        Guid scheduleId,
        Guid tenantId,
        string platform,
        string reason);

    [LoggerMessage(EventId = 5263, Level = LogLevel.Information,
        Message = "Engagement sync batch completed: {Total} total, {Synced} synced, {Skipped} skipped, {Failed} failed")]
    private static partial void LogBatch(
        ILogger logger,
        int total,
        int synced,
        int skipped,
        int failed);

    [LoggerMessage(EventId = 5264, Level = LogLevel.Error,
        Message = "Unexpected engagement sync failure for schedule {ScheduleId}, tenant {TenantId}, platform {Platform}, object {ObjectId}")]
    private static partial void LogUnexpectedFailure(
        ILogger logger,
        Exception exception,
        Guid scheduleId,
        Guid tenantId,
        string platform,
        string objectId);
}
