using System.Text.Json;
using System.Threading.RateLimiting;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Channels.Meta;

public interface ICommentChannelAdapterResolver
{
    Task<ICommentChannelAdapter?> ResolveAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        CancellationToken ct = default);
}

public sealed class TenantCommentChannelAdapterResolver(
    ICommentChannelAdapter pancake,
    MetaCommentChannelAdapter meta,
    Clawbot.Infrastructure.Channels.Pancake.IPancakeConfigResolver pancakeConfig,
    Clawbot.Infrastructure.Channels.Pancake.IPancakePageTokenResolver pageTokenResolver) : ICommentChannelAdapterResolver
{
    public async Task<ICommentChannelAdapter?> ResolveAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        CancellationToken ct = default)
    {
        var config = await pancakeConfig.ResolveAsync(tenantId, ct).ConfigureAwait(false);
        var separator = externalThreadId.IndexOf(':', StringComparison.Ordinal);
        var pageId = separator > 0 ? externalThreadId[..separator] : externalThreadId;
        var pancakeToken = config is not null
            && (string.IsNullOrWhiteSpace(config.PageId)
                || string.Equals(config.PageId, pageId, StringComparison.Ordinal))
            ? await pageTokenResolver.ResolveAsync(
                tenantId,
                platform,
                pageId,
                ct).ConfigureAwait(false)
            : null;
        if (pancakeToken is not null)
            return pancake;

        if (string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase)
            || string.Equals(platform, "instagram", StringComparison.OrdinalIgnoreCase))
        {
            return meta;
        }

        return null;
    }
}

public sealed class MetaCommentChannelAdapter(
    AppDbContext db,
    IMetaGraphClient graph,
    IMetaIntegrationService meta,
    IInstagramCredentialResolver standaloneInstagram) : ICommentChannelAdapter
{
    private const int MaxTextLength = 4_000;
    private static readonly PartitionedRateLimiter<string> RateLimiter =
        PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetSlidingWindowLimiter(
                key,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromSeconds(1),
                    SegmentsPerWindow = 2,
                    QueueLimit = 20,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true,
                }));

    private readonly Dictionary<(Guid TenantId, string Platform, string PageId), MetaReplyContext?> _contextCache = [];

    public async Task<string?> SendCommentReplyAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string commentMessageId,
        string text,
        CancellationToken ct = default)
    {
        var context = await ResolveContextAsync(
            tenantId,
            platform,
            externalThreadId,
            ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("meta_comment_target_unavailable");
        var commentId = ValidateIdentifier(commentMessageId, nameof(commentMessageId));
        var message = ValidateText(text);
        using var lease = await RateLimiter.AcquireAsync(
            $"tenant:{tenantId}:meta:{context.Platform}:{context.PageId}",
            1,
            ct).ConfigureAwait(false);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("meta_comment_rate_limit_exceeded");

        var path = context.Platform == "instagram"
            ? $"{commentId}/replies"
            : $"{commentId}/comments";
        using var document = await graph.PostAsync(
            tenantId,
            path,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["message"] = message,
            },
            context.AccessToken,
            ct).ConfigureAwait(false);
        return RequiredResponseId(document);
    }

    public async Task<string?> SendPrivateReplyAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        string postId,
        string commentMessageId,
        string fromId,
        string text,
        CancellationToken ct = default)
    {
        var context = await ResolveContextAsync(
            tenantId,
            platform,
            externalThreadId,
            ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("meta_comment_target_unavailable");
        var commentId = ValidateIdentifier(commentMessageId, nameof(commentMessageId));
        _ = ValidateIdentifier(postId, nameof(postId));
        _ = ValidateIdentifier(fromId, nameof(fromId));
        var message = ValidateText(text);
        using var lease = await RateLimiter.AcquireAsync(
            $"tenant:{tenantId}:meta:{context.Platform}:{context.PageId}",
            1,
            ct).ConfigureAwait(false);
        if (!lease.IsAcquired)
            throw new InvalidOperationException("meta_comment_rate_limit_exceeded");

        if (string.IsNullOrWhiteSpace(context.MessagingNodeId))
            throw new InvalidOperationException("meta_private_reply_target_unavailable");
        if (context.Platform == "facebook")
        {
            var privatePage = await meta.ResolvePageForPrivateRepliesByExternalIdAsync(
                tenantId,
                context.PageId,
                ct).ConfigureAwait(false);
            if (privatePage is null)
                throw new InvalidOperationException("meta_private_reply_capability_unavailable");
        }
        else if (context.Platform == "instagram" && context.AssetId is { } instagramAssetId)
        {
            var privateReplyResolution = await meta.ResolveInstagramForPrivateRepliesAsync(
                tenantId,
                instagramAssetId,
                ct).ConfigureAwait(false);
            if (privateReplyResolution.Credential is null)
                throw new InvalidOperationException("meta_private_reply_capability_unavailable");
        }

        using var document = await graph.PostAsync(
            tenantId,
            $"{context.MessagingNodeId}/messages",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["recipient"] = JsonSerializer.Serialize(new { comment_id = commentId }),
                ["message"] = JsonSerializer.Serialize(new { text = message }),
            },
            context.AccessToken,
            ct).ConfigureAwait(false);
        return RequiredResponseId(document);
    }

    private async Task<MetaReplyContext?> ResolveContextAsync(
        Guid tenantId,
        string platform,
        string externalThreadId,
        CancellationToken ct)
    {
        if (tenantId == Guid.Empty
            || string.IsNullOrWhiteSpace(platform)
            || string.IsNullOrWhiteSpace(externalThreadId))
        {
            return null;
        }

        var normalizedPlatform = platform.Trim().ToLowerInvariant();
        if (normalizedPlatform is not ("facebook" or "instagram"))
            return null;
        var separator = externalThreadId.IndexOf(':', StringComparison.Ordinal);
        var pageId = separator > 0 ? externalThreadId[..separator] : externalThreadId;
        if (!IsSafeIdentifier(pageId))
            return null;
        var key = (tenantId, normalizedPlatform, pageId);
        if (_contextCache.TryGetValue(key, out var cached))
            return cached;

        var inbox = await db.Inboxes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(value => value.TenantId == tenantId
                && value.Platform == normalizedPlatform
                && value.ExternalPageId == pageId
                && value.IsActive
                && value.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (inbox is null)
        {
            _contextCache[key] = null;
            return null;
        }

        MetaReplyContext? context = null;
        if (inbox.Platform == "facebook")
        {
            var credential = await meta.ResolvePageForCommentsByExternalIdAsync(
                tenantId,
                pageId,
                ct).ConfigureAwait(false);
            if (credential is not null)
                context = new MetaReplyContext("facebook", pageId, credential.PageAccessToken, credential.AssetId, pageId);
        }
        else
        {
            var standalone = await standaloneInstagram.ResolveAsync(tenantId, ct).ConfigureAwait(false);
            if (standalone.Status == InstagramCredentialResolutionStatus.Resolved
                && standalone.Credential is not null
                && string.Equals(standalone.Credential.InstagramUserId, pageId, StringComparison.Ordinal))
            {
                context = new MetaReplyContext("instagram", pageId, standalone.Credential.AccessToken, null, pageId);
            }
            else
            {
                var assetIds = await db.MetaAssets
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .Where(asset => asset.TenantId == tenantId
                        && asset.AssetType == "page"
                        && asset.IsActive)
                    .Select(asset => asset.Id)
                    .ToListAsync(ct).ConfigureAwait(false);
                foreach (var assetId in assetIds)
                {
                    var resolution = await meta.ResolveInstagramForCommentsAsync(tenantId, assetId, ct)
                        .ConfigureAwait(false);
                    if (resolution.Credential is not null
                        && string.Equals(resolution.Credential.InstagramUserId, pageId, StringComparison.Ordinal))
                    {
                        context = new MetaReplyContext(
                            "instagram",
                            pageId,
                            resolution.Credential.PageAccessToken,
                            resolution.Credential.PageAssetId,
                            await ResolveFacebookPageIdAsync(tenantId, resolution.Credential.PageAssetId, ct).ConfigureAwait(false));
                        break;
                    }
                }
            }
        }

        _contextCache[key] = context;
        return context;
    }

    private Task<string?> ResolveFacebookPageIdAsync(
        Guid tenantId,
        Guid assetId,
        CancellationToken ct) =>
        db.MetaAssets
            .IgnoreQueryFilters()
            .Where(asset => asset.TenantId == tenantId
                && asset.Id == assetId
                && asset.AssetType == "page"
                && asset.IsActive)
            .Select(asset => asset.ExternalId)
            .FirstOrDefaultAsync(ct);

    private static string RequiredResponseId(JsonDocument document)
    {
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in new[] { "id", "message_id" })
            {
                if (document.RootElement.TryGetProperty(propertyName, out var id)
                    && id.ValueKind == JsonValueKind.String
                    && IsSafeIdentifier(id.GetString()))
                {
                    return id.GetString()!;
                }
            }
        }
        throw new ChannelDeliveryAmbiguousException("meta_comment_response_missing_id");
    }

    private static string ValidateIdentifier(string? value, string parameterName) =>
        IsSafeIdentifier(value)
            ? value!.Trim()
            : throw new ArgumentException("Meta identifier invalid", parameterName);

    private static string ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("reply text required", nameof(text));
        var normalized = text.Trim();
        return normalized.Length > MaxTextLength ? normalized[..MaxTextLength] : normalized;
    }

    private static bool IsSafeIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 256
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private sealed record MetaReplyContext(
        string Platform,
        string PageId,
        string AccessToken,
        Guid? AssetId,
        string? MessagingNodeId);
}
