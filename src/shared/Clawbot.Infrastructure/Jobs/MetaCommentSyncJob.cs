using System.Text.Json;
using Clawbot.Infrastructure.Channels.Meta;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using Hangfire;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Jobs;

// Reconciles recent Meta comments so a missed webhook does not permanently hide a customer message.
// Inbound delivery is at-least-once; ChannelMessageIngestor deduplicates by external_message_id.
public sealed partial class MetaCommentSyncJob(
    AppDbContext db,
    IMetaIntegrationService meta,
    IMetaGraphClient graph,
    IInstagramCredentialResolver instagramCredentials,
    IMetaInboxProvisioner inboxes,
    IPublishEndpoint publisher,
    IClock clock,
    ILogger<MetaCommentSyncJob> logger)
{
    private static readonly TimeSpan ReconciliationWindow = TimeSpan.FromDays(7);
    private const int BatchSize = 100;
    private const int PageSize = 50;
    private const int MaxMessageLength = 32_000;
    private const int MaxCommentsPerSchedule = 500;
    private const int MaxGraphPages = 10;

    [DisableConcurrentExecution(timeoutInSeconds: 600)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var since = now.Subtract(ReconciliationWindow);
        var schedules = await db.ContentSchedules.IgnoreQueryFilters()
            .Where(schedule => schedule.Status == "posted"
                && schedule.PostedAt >= since
                && (schedule.Platform == "facebook" || schedule.Platform == "instagram")
                && (schedule.ExternalPostId != null || schedule.PostUrl != null))
            .OrderBy(schedule => schedule.MetaCommentsSyncedAt)
            .ThenBy(schedule => schedule.PostedAt)
            .Take(BatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var fetched = 0;
        var published = 0;
        var failed = 0;
        foreach (var schedule in schedules)
        {
            schedule.MarkMetaCommentsAttempt(now);
            var objectId = schedule.ExternalPostId
                ?? (string.Equals(schedule.Platform, "facebook", StringComparison.OrdinalIgnoreCase)
                    ? MetaEngagementSyncJob.ExtractPostId(schedule.PostUrl)
                    : null);
            if (!MetaEngagementSyncJob.IsSafeGraphObjectId(objectId))
            {
                LogSkipped(logger, schedule.Id, schedule.TenantId, schedule.Platform, "post_id_unavailable");
                continue;
            }

            try
            {
                var legacyPageId = !schedule.MetaAssetId.HasValue
                    && string.Equals(schedule.Platform, "facebook", StringComparison.OrdinalIgnoreCase)
                    ? MetaEngagementSyncJob.ExtractPageId(objectId!)
                    : null;
                var source = await ResolveSourceAsync(
                    schedule.TenantId,
                    schedule.Platform,
                    schedule.MetaAssetId,
                    schedule.ProviderTargetId,
                    legacyPageId,
                    ct)
                    .ConfigureAwait(false);
                if (source is null)
                {
                    LogSkipped(logger, schedule.Id, schedule.TenantId, schedule.Platform, "credential_unavailable");
                    continue;
                }

                var comments = await FetchCommentsAsync(
                    schedule.TenantId,
                    objectId!,
                    source,
                    now,
                    ct).ConfigureAwait(false);
                fetched += comments.Count;
                await inboxes.EnsureAsync(
                    schedule.TenantId,
                    source.Platform,
                    source.ExternalPageId,
                    source.Name,
                    ct).ConfigureAwait(false);
                await inboxes.SaveChangesAsync(ct).ConfigureAwait(false);
                foreach (var comment in comments)
                {
                    await publisher.Publish(
                        new ChannelInboundMessageReceived(schedule.TenantId, comment),
                        ct).ConfigureAwait(false);
                    published++;
                }
                // Persist this schedule's bus-outbox messages before moving to another schedule;
                // cancellation must not discard a whole batch of already-published comments.
                await inboxes.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (MetaGraphException ex)
            {
                failed++;
                LogFailed(logger, schedule.Id, schedule.TenantId, schedule.Platform, GetGraphFailureReason(ex));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                LogUnexpectedFailure(logger, ex, schedule.Id, schedule.TenantId, schedule.Platform);
            }
        }

        if (schedules.Count > 0)
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogBatch(logger, schedules.Count, fetched, published, failed);
    }

    private async Task<IReadOnlyList<ChannelMessage>> FetchCommentsAsync(
        Guid tenantId,
        string objectId,
        MetaCommentSource source,
        DateTimeOffset fallbackTime,
        CancellationToken ct)
    {
        var comments = new List<ChannelMessage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? after = null;
        for (var page = 0; page < MaxGraphPages && comments.Count < MaxCommentsPerSchedule; page++)
        {
            var query = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["fields"] = source.IsInstagram
                    ? "id,text,from,timestamp,parent_id,replies{id,text,from,timestamp,parent_id}"
                    : "id,message,from,created_time,parent{id},can_comment",
                ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["order"] = "chronological",
            };
            if (!source.IsInstagram)
                query["filter"] = "stream";
            if (!string.IsNullOrWhiteSpace(after))
                query["after"] = after;

            using var document = await graph.GetAsync(
                tenantId,
                $"{objectId}/comments",
                query,
                source.AccessToken,
                ct).ConfigureAwait(false);
            foreach (var comment in ParseComments(document.RootElement, source, objectId, fallbackTime))
            {
                var externalId = comment.Metadata["external_message_id"];
                if (seen.Add(externalId))
                    comments.Add(comment);
                if (comments.Count >= MaxCommentsPerSchedule)
                    break;
            }
            if (source.IsInstagram && comments.Count < MaxCommentsPerSchedule)
            {
                foreach (var comment in await FetchNestedInstagramRepliesAsync(
                    tenantId,
                    document.RootElement,
                    source,
                    objectId,
                    fallbackTime,
                    ct).ConfigureAwait(false))
                {
                    var externalId = comment.Metadata["external_message_id"];
                    if (seen.Add(externalId))
                        comments.Add(comment);
                    if (comments.Count >= MaxCommentsPerSchedule)
                        break;
                }
            }

            after = ExtractAfterCursor(document.RootElement);
            if (string.IsNullOrWhiteSpace(after))
                break;
        }
        return comments;
    }

    private async Task<IReadOnlyList<ChannelMessage>> FetchNestedInstagramRepliesAsync(
        Guid tenantId,
        JsonElement root,
        MetaCommentSource source,
        string parentPostId,
        DateTimeOffset fallbackTime,
        CancellationToken ct)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
            return [];

        var replies = new List<ChannelMessage>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("id", out var idElement))
                continue;
            var commentId = ScalarString(item, "id");
            if (!IsValidIdentifier(commentId)
                || !item.TryGetProperty("replies", out var initialReplies)
                || initialReplies.ValueKind != JsonValueKind.Object)
                continue;

            var after = ExtractAfterCursor(initialReplies);
            for (var page = 0; page < MaxGraphPages && !string.IsNullOrWhiteSpace(after); page++)
            {
                using var document = await graph.GetAsync(
                    tenantId,
                    $"{commentId}/replies",
                    new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["fields"] = "id,text,from,timestamp,parent_id",
                        ["limit"] = PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["after"] = after,
                    },
                    source.AccessToken,
                    ct).ConfigureAwait(false);
                replies.AddRange(ParseComments(document.RootElement, source, parentPostId, fallbackTime));
                after = ExtractAfterCursor(document.RootElement);
            }
        }
        return replies;
    }

    private static string? ExtractAfterCursor(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("paging", out var paging)
            || paging.ValueKind != JsonValueKind.Object)
            return null;

        if (paging.TryGetProperty("cursors", out var cursors)
            && cursors.ValueKind == JsonValueKind.Object
            && !string.IsNullOrWhiteSpace(ScalarString(cursors, "after")))
        {
            return ScalarString(cursors, "after");
        }

        var next = ScalarString(paging, "next");
        if (string.IsNullOrWhiteSpace(next))
            return null;
        var queryStart = next.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0)
            return null;
        foreach (var pair in next[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0
                || !string.Equals(pair[..separator], "after", StringComparison.Ordinal))
                continue;
            return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
        }
        return null;
    }

    private async Task<MetaCommentSource?> ResolveSourceAsync(
        Guid tenantId,
        string platform,
        Guid? assetId,
        string? providerTargetId,
        string? legacyPageId,
        CancellationToken ct)
    {
        if (string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase))
        {
            var credential = !string.IsNullOrWhiteSpace(legacyPageId)
                ? await meta.ResolvePageForEngagementByExternalIdAsync(tenantId, legacyPageId, ct).ConfigureAwait(false)
                : await meta.ResolvePageForEngagementAsync(tenantId, assetId, ct).ConfigureAwait(false);
            return credential is null
                ? null
                : new MetaCommentSource(false, "facebook", credential.PageId, credential.PageName, credential.PageAccessToken);
        }

        if (assetId.HasValue)
        {
            var resolution = await meta.ResolveInstagramForCommentsAsync(tenantId, assetId, ct).ConfigureAwait(false);
            return resolution.Credential is null
                ? null
                : new MetaCommentSource(
                    true,
                    "instagram",
                    resolution.Credential.InstagramUserId,
                    $"Instagram - {resolution.Credential.InstagramUserId}",
                    resolution.Credential.PageAccessToken);
        }

        if (string.IsNullOrWhiteSpace(providerTargetId))
            return null;
        var standalone = await instagramCredentials.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        return standalone.Status == InstagramCredentialResolutionStatus.Resolved
            && standalone.Credential is not null
            && string.Equals(standalone.Credential.InstagramUserId, providerTargetId.Trim(), StringComparison.Ordinal)
            ? new MetaCommentSource(
                true,
                "instagram",
                standalone.Credential.InstagramUserId,
                $"Instagram - {standalone.Credential.InstagramUserId}",
                standalone.Credential.AccessToken)
            : null;
    }

    internal static IReadOnlyList<ChannelMessage> ParseComments(
        JsonElement root,
        MetaCommentSource source,
        string parentPostId,
        DateTimeOffset fallbackTime)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var comments = new List<ChannelMessage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in data.EnumerateArray())
        {
            AddComment(item);
            if (source.IsInstagram
                && item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("replies", out var replies)
                && replies.ValueKind == JsonValueKind.Object
                && replies.TryGetProperty("data", out var repliesData)
                && repliesData.ValueKind == JsonValueKind.Array)
            {
                foreach (var reply in repliesData.EnumerateArray())
                    AddComment(reply);
            }
        }

        return comments.ToArray();

        void AddComment(JsonElement item)
        {
            if (item.ValueKind != JsonValueKind.Object)
                return;
            var commentId = ScalarString(item, "id");
            var text = source.IsInstagram
                ? ScalarString(item, "text")
                : ScalarString(item, "message");
            var from = item.TryGetProperty("from", out var fromElement)
                && fromElement.ValueKind == JsonValueKind.Object
                ? fromElement
                : default;
            var fromId = ScalarString(from, "id");
            if (!IsValidIdentifier(commentId)
                || !IsValidIdentifier(fromId)
                || !seen.Add(commentId!))
            {
                return;
            }

            var messageText = text ?? string.Empty;
            if (messageText.Length > MaxMessageLength)
                messageText = messageText[..MaxMessageLength];
            var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["page_id"] = source.ExternalPageId,
                ["sender_id"] = fromId!,
                ["external_message_id"] = commentId!,
            };
            var fromName = ScalarString(from, "name") ?? ScalarString(from, "username");
            if (!string.IsNullOrWhiteSpace(fromName))
            {
                var normalizedFromName = fromName.Trim();
                metadata["sender_name"] = normalizedFromName.Length > 256
                    ? normalizedFromName[..256]
                    : normalizedFromName;
            }
            if (string.Equals(source.ExternalPageId, fromId, StringComparison.Ordinal))
                metadata["is_owner"] = "true";
            var parentId = GetParentId(item);
            if (!string.IsNullOrWhiteSpace(parentId))
                metadata["comment_parent_id"] = parentId.Length > 256 ? parentId[..256] : parentId;

            comments.Add(new ChannelMessage(
                Channel: source.Platform,
                ExternalThreadId: $"{source.ExternalPageId}:{fromId}",
                ExternalUserId: fromId!,
                Text: messageText,
                SentAt: ParseSentAt(item, source.IsInstagram, fallbackTime),
                Metadata: metadata,
                MessageType: "comment",
                ParentPostId: parentPostId,
                ParentCommentId: !string.IsNullOrWhiteSpace(parentId)
                    && !string.Equals(parentId, parentPostId, StringComparison.Ordinal)
                    ? parentId
                    : null));
        }
    }

    private static DateTimeOffset ParseSentAt(JsonElement item, bool instagram, DateTimeOffset fallbackTime)
    {
        var property = instagram ? "timestamp" : "created_time";
        if (item.TryGetProperty(property, out var timestamp))
        {
            if (timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out var unixSeconds))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
                catch (ArgumentOutOfRangeException) { }
            }
            if (timestamp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(timestamp.GetString(), out var parsed))
            {
                return parsed;
            }
        }
        return fallbackTime;
    }

    private static bool IsValidIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 256
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static string? GetParentId(JsonElement item) =>
        ScalarString(item, "parent_id")
        ?? (item.TryGetProperty("parent", out var parent)
            && parent.ValueKind == JsonValueKind.Object
                ? ScalarString(parent, "id")
                : null);

    private static string? ScalarString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static string GetGraphFailureReason(MetaGraphException exception) =>
        exception.Code is { } code
            ? $"graph_code:{code}"
            : exception.HttpStatus is { } status
                ? $"http_status:{status}"
                : "graph_error";

    internal sealed record MetaCommentSource(
        bool IsInstagram,
        string Platform,
        string ExternalPageId,
        string Name,
        string AccessToken);

    [LoggerMessage(EventId = 5270, Level = LogLevel.Information,
        Message = "Meta comment reconciliation batch completed: {Schedules} schedules, {Fetched} comments fetched, {Published} comments published, {Failed} failed")]
    private static partial void LogBatch(ILogger logger, int schedules, int fetched, int published, int failed);

    [LoggerMessage(EventId = 5271, Level = LogLevel.Warning,
        Message = "Meta comment reconciliation skipped schedule {ScheduleId}, tenant {TenantId}, platform {Platform}: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid scheduleId, Guid tenantId, string platform, string reason);

    [LoggerMessage(EventId = 5272, Level = LogLevel.Warning,
        Message = "Meta comment reconciliation failed schedule {ScheduleId}, tenant {TenantId}, platform {Platform}: {Reason}")]
    private static partial void LogFailed(ILogger logger, Guid scheduleId, Guid tenantId, string platform, string reason);

    [LoggerMessage(EventId = 5273, Level = LogLevel.Error,
        Message = "Unexpected Meta comment reconciliation failure for schedule {ScheduleId}, tenant {TenantId}, platform {Platform}")]
    private static partial void LogUnexpectedFailure(ILogger logger, Exception exception, Guid scheduleId, Guid tenantId, string platform);
}
