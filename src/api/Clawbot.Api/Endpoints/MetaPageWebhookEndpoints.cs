using System.Text;
using System.Text.Json;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Channels.Meta;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record MetaPageCommentEvent(
    string PageId,
    string CommentId,
    string PostId,
    string FromId,
    string? FromName,
    string Message,
    DateTimeOffset SentAt,
    string? ParentId);

public static class MetaPageWebhookEndpoints
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private const int MaxIdentifierLength = 256;
    private const int MaxPageIdentifierLength = 128;
    private const int MaxMessageLength = 32_000;
    private const int MaxCommentsPerPayload = 500;

    public static IEndpointRouteBuilder MapMetaPageWebhooks(this IEndpointRouteBuilder app)
    {
        const string path = "/webhooks/meta/page";
        app.MapGet(path, VerifyAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.WebhookPolicy);
        app.MapPost(path, ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.WebhookPolicy);
        return app;
    }

    private static async Task<IResult> VerifyAsync(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IMetaGraphConfigurationResolver configurations,
        CancellationToken ct)
    {
        var candidates = await configurations.GetWebhookCandidatesAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        return string.Equals(mode, "subscribe", StringComparison.Ordinal)
               && !string.IsNullOrEmpty(challenge)
               && candidates.Any(candidate => FixedTimeEquals(verifyToken, candidate.Options.WebhookVerifyToken))
            ? Results.Text(challenge, "text/plain", Encoding.UTF8)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        AppDbContext db,
        IMetaGraphConfigurationResolver configurations,
        IMetaInboxProvisioner inboxes,
        IPublishEndpoint publisher,
        IClock clock,
        CancellationToken ct)
    {
        var candidates = await configurations.GetWebhookCandidatesAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (request.ContentLength is > MaxPayloadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var payload = await ReadPayloadAsync(request.Body, MaxPayloadBytes, ct).ConfigureAwait(false);
        if (payload is null)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        var signedCandidates = candidates
            .Where(candidate => MetaBusinessIntegrationWebhookEndpoints.IsValidSignature(
                payload,
                signature,
                candidate.Options.AppSecret))
            .ToList();
        if (signedCandidates.Count == 0)
            return Results.StatusCode(StatusCodes.Status401Unauthorized);

        IReadOnlyList<MetaPageCommentEvent> comments;
        try
        {
            comments = ParseComments(payload, clock.UtcNow);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { code = "meta.webhook_payload_invalid" });
        }

        if (comments.Count == 0)
            return Results.Ok(new { received = 0 });

        var pageIds = comments.Select(comment => comment.PageId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var explicitTenantIds = signedCandidates
            .Where(candidate => candidate.TenantId.HasValue)
            .Select(candidate => candidate.TenantId!.Value)
            .Distinct()
            .ToList();
        var allowAllTenants = signedCandidates.Any(candidate => !candidate.TenantId.HasValue);
        var tenantOverrides = allowAllTenants
            ? await db.SocialCredentials
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(credential => credential.Provider == MetaGraphConfigurationStore.Provider
                    && credential.PageId == null
                    && credential.IsActive
                    && credential.DeletedAt == null)
                .Select(credential => credential.TenantId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false)
            : [];
        var assetQuery = db.MetaAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(asset => asset.AssetType == "page"
                && asset.IsActive
                && pageIds.Contains(asset.ExternalId)
                && db.MetaConnections.Any(connection =>
                    connection.Id == asset.ConnectionId
                    && connection.TenantId == asset.TenantId
                    && connection.Status == "active"));
        assetQuery = allowAllTenants
            ? assetQuery.Where(asset => explicitTenantIds.Contains(asset.TenantId)
                || !tenantOverrides.Contains(asset.TenantId))
            : assetQuery.Where(asset => explicitTenantIds.Contains(asset.TenantId));

        var pageTenants = await assetQuery
            .Select(asset => new { asset.TenantId, asset.ExternalId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var tenantsByPage = pageTenants
            .GroupBy(row => row.ExternalId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.TenantId).Distinct().ToArray(),
                StringComparer.Ordinal);

        var publishable = new List<(Guid TenantId, MetaPageCommentEvent Comment)>();
        foreach (var comment in comments)
        {
            if (!tenantsByPage.TryGetValue(comment.PageId, out var tenantIds)
                || tenantIds.Length != 1)
            {
                // A Page ID is the only tenant discriminator in a Page webhook. Refuse ambiguous
                // ownership instead of leaking a customer comment across tenants.
                continue;
            }
            var tenantId = tenantIds[0];
            await inboxes.EnsureAsync(
                tenantId,
                "facebook",
                comment.PageId,
                $"Facebook - {comment.PageId}",
                ct).ConfigureAwait(false);
            publishable.Add((tenantId, comment));
        }

        if (publishable.Count == 0)
            return Results.Ok(new { received = 0 });

        await inboxes.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var (tenantId, comment) in publishable)
        {
            var metadata = BuildMetadata(comment);
            var message = new ChannelMessage(
                Channel: "facebook",
                ExternalThreadId: $"{comment.PageId}:{comment.FromId}",
                ExternalUserId: comment.FromId,
                Text: comment.Message,
                SentAt: comment.SentAt,
                Metadata: metadata,
                MessageType: "comment",
                ParentPostId: comment.PostId,
                ParentCommentId: !string.IsNullOrWhiteSpace(comment.ParentId)
                    && !string.Equals(comment.ParentId, comment.PostId, StringComparison.Ordinal)
                    ? comment.ParentId
                    : null);
            await publisher.Publish(
                new ChannelInboundMessageReceived(tenantId, message),
                ct).ConfigureAwait(false);
        }

        // UseBusOutbox buffers the published events in AppDbContext; without this second save the
        // endpoint returns 200 while the broker message never becomes durable.
        await inboxes.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { received = publishable.Count });
    }

    private static async Task<byte[]?> ReadPayloadAsync(Stream body, int maxBytes, CancellationToken ct)
    {
        await using var buffer = new MemoryStream(Math.Min(maxBytes, 64 * 1024));
        var chunk = new byte[64 * 1024];
        var total = 0;
        while (true)
        {
            var read = await body.ReadAsync(chunk.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0)
                break;
            total += read;
            if (total > maxBytes)
                return null;
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }

    internal static IReadOnlyList<MetaPageCommentEvent> ParseComments(
        byte[] payload,
        DateTimeOffset fallbackTime)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !string.Equals(ScalarString(root, "object"), "page", StringComparison.Ordinal)
            || !root.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var comments = new List<MetaPageCommentEvent>();
        var seen = new HashSet<(string PageId, string CommentId)>();
        foreach (var entry in entries.EnumerateArray())
        {
            var pageId = ScalarString(entry, "id");
            if (!IsValidPageIdentifier(pageId)
                || !entry.TryGetProperty("changes", out var changes)
                || changes.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var change in changes.EnumerateArray())
            {
                if (!string.Equals(ScalarString(change, "field"), "feed", StringComparison.Ordinal)
                    || !change.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.Object
                    || !string.Equals(ScalarString(value, "item"), "comment", StringComparison.Ordinal)
                    || !string.Equals(ScalarString(value, "verb"), "add", StringComparison.Ordinal))
                {
                    continue;
                }

                var commentId = ScalarString(value, "comment_id") ?? ScalarString(value, "id");
                var postId = ScalarString(value, "post_id");
                var message = ScalarString(value, "message") ?? string.Empty;
                var from = value.TryGetProperty("from", out var fromElement)
                    && fromElement.ValueKind == JsonValueKind.Object
                    ? fromElement
                    : default;
                var fromId = ScalarString(from, "id");
                if (!IsValidIdentifier(commentId)
                    || !IsValidIdentifier(postId)
                    || !IsValidIdentifier(fromId)
                    || !seen.Add((pageId!, commentId!)))
                {
                    continue;
                }

                var parentId = ScalarString(value, "parent_id");
                if (parentId?.Length > MaxIdentifierLength)
                    parentId = null;
                comments.Add(new MetaPageCommentEvent(
                    pageId!,
                    commentId!,
                    postId!,
                    fromId!,
                    ScalarString(from, "name") is { } name && name.Length > MaxIdentifierLength
                        ? name[..MaxIdentifierLength]
                        : ScalarString(from, "name"),
                    message.Length > MaxMessageLength ? message[..MaxMessageLength] : message,
                    ParseSentAt(value, fallbackTime),
                    parentId));
                if (comments.Count >= MaxCommentsPerPayload)
                    return comments.DistinctBy(comment => (comment.PageId, comment.CommentId)).ToArray();
            }
        }

        return comments
            .DistinctBy(comment => (comment.PageId, comment.CommentId))
            .ToArray();
    }

    private static Dictionary<string, string> BuildMetadata(MetaPageCommentEvent comment)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["page_id"] = comment.PageId,
            ["sender_id"] = comment.FromId,
            ["external_message_id"] = comment.CommentId,
        };
        if (!string.IsNullOrWhiteSpace(comment.FromName))
            metadata["sender_name"] = comment.FromName.Trim();
        if (string.Equals(comment.PageId, comment.FromId, StringComparison.Ordinal))
            metadata["is_owner"] = "true";
        if (!string.IsNullOrWhiteSpace(comment.ParentId))
            metadata["comment_parent_id"] = comment.ParentId.Trim();
        return metadata;
    }

    private static DateTimeOffset ParseSentAt(JsonElement value, DateTimeOffset fallbackTime)
    {
        if (value.TryGetProperty("created_time", out var created))
        {
            if (created.ValueKind == JsonValueKind.Number && created.TryGetInt64(out var unixSeconds))
            {
                try { return DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
                catch (ArgumentOutOfRangeException) { }
            }

            if (created.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(created.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return fallbackTime;
    }

    private static bool IsValidPageIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= MaxPageIdentifierLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsValidIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= MaxIdentifierLength
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static string? ScalarString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (string.IsNullOrEmpty(left))
            return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
