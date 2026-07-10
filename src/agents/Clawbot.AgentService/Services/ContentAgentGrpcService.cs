using Clawbot.Agents.Contracts.Content;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreContent = Clawbot.Agents.Core.Content;

namespace Clawbot.AgentService.Services;

public sealed partial class ContentAgentGrpcService(
    CoreContent.ContentAgent agent,
    CoreContent.ContentReviewer reviewer,
    AppDbContext db,
    IClock clock,
    ILogger<ContentAgentGrpcService> logger) : ContentAgent.ContentAgentBase
{
    // Server-side cap cho một lần review — fail-closed về needs_human khi vượt (QĐ3).
    private static readonly TimeSpan ReviewTimeout = TimeSpan.FromSeconds(20);

    private readonly CoreContent.ContentAgent _agent = agent;
    private readonly CoreContent.ContentReviewer _reviewer = reviewer;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<ContentAgentGrpcService> _logger = logger;

    public override async Task<ContentResponse> Generate(ContentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = ParseTenantId(request.TenantId);
        if (string.IsNullOrWhiteSpace(request.Brief))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "brief required"));
        if (string.IsNullOrWhiteSpace(request.Channel))
            throw new RpcException(new Status(StatusCode.InvalidArgument, "channel required"));
        var briefId = ParseOptionalGuid(request.BriefId, "brief_id");

        CoreContent.ContentDraftResult draft;
        try
        {
            draft = await _agent.GenerateAsync(
                new CoreContent.ContentGenerateRequest(
                    tenantId, briefId, request.Channel, request.Brief, KbModuleCode: null),
                context.CancellationToken).ConfigureAwait(false);
        }
        catch (CoreContent.ContentPromptTemplateException ex)
        {
            // Surface "no template for platform X" as a client error instead of an opaque handler fault
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var item = ContentItem.Create(
            tenantId, draft.Platform, draft.Body, createdBy: null, _clock.UtcNow, briefId: draft.BriefId);
        _db.ContentItems.Add(item);
        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        LogDraftGenerated(
            _logger,
            tenantId,
            item.Id,
            item.Platform,
            draft.InputTokens,
            draft.OutputTokens,
            draft.LatencyMs);

        return new ContentResponse
        {
            ContentId = item.Id.ToString(),
            Title = item.Platform,
            Body = item.Body,
            Variants = { ToVariant(item) },
        };
    }

    public override async Task<ContentResponse> Repurpose(RepurposeRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = ParseTenantId(request.TenantId);
        if (!Guid.TryParse(request.ContentId, out var contentId) || contentId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "content_id required"));
        IReadOnlyList<string> targets;
        try
        {
            targets = CoreContent.ContentRepurposeMapper.NormalizeTargets(request.TargetChannels);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var source = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Id == contentId && i.DeletedAt == null)
            .Select(i => new { i.Id, i.BriefId, i.Body })
            .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "content item not found"));

        var sourceBody = string.IsNullOrWhiteSpace(request.SourceBody) ? source.Body : request.SourceBody;
        var created = new List<(ContentItem Item, CoreContent.ContentDraftResult Draft)>(targets.Count);
        foreach (var target in targets)
        {
            var draft = await _agent.GenerateAsync(
                new CoreContent.ContentGenerateRequest(
                    tenantId, source.BriefId, target, sourceBody, KbModuleCode: null),
                context.CancellationToken).ConfigureAwait(false);

            var item = ContentItem.Create(
                tenantId, draft.Platform, draft.Body, createdBy: null, _clock.UtcNow, briefId: draft.BriefId);
            created.Add((item, draft));
        }

        _db.ContentItems.AddRange(created.Select(c => c.Item));
        await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        foreach (var (item, draft) in created)
        {
            LogDraftGenerated(
                _logger,
                tenantId,
                item.Id,
                item.Platform,
                draft.InputTokens,
                draft.OutputTokens,
                draft.LatencyMs);
        }

        var first = created[0].Item;
        var response = new ContentResponse
        {
            ContentId = first.Id.ToString(),
            Title = first.Platform,
            Body = first.Body,
        };
        response.Variants.AddRange(created.Select(c => ToVariant(c.Item)));
        return response;
    }

    // Review-gate P1: chấm một content item bằng reviewer-agent. Verdict approve => stamp ApprovedByAgentId
    // (id của agent_definitions row reviewer-agent); reject => demote + lý do; needs_human => giữ nguyên.
    // Mọi đường lỗi (reviewer chưa cấu hình, LLM down/timeout) trả needs_human — fail-closed, không throw.
    public override async Task<ReviewContentResponse> Review(ReviewContentRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var tenantId = ParseTenantId(request.TenantId);
        if (!Guid.TryParse(request.ContentId, out var contentId) || contentId == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "content_id required"));

        var item = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId && i.Id == contentId && i.DeletedAt == null)
            .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "content item not found"));

        if (!string.Equals(item.Status, "draft", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase))
            throw new RpcException(new Status(StatusCode.FailedPrecondition, $"content item is '{item.Status}', only draft or approved items can be reviewed"));

        var reviewerDefId = await _db.AgentDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Code == "reviewer-agent" && d.DeletedAt == null)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false);
        if (reviewerDefId is null)
            return new ReviewContentResponse { Verdict = CoreContent.ContentReviewResult.NeedsHuman, Reason = "reviewer_not_configured" };

        // Separation of duties: agent sinh content không được tự duyệt.
        if (item.CreatedByAgentId is not null && item.CreatedByAgentId == reviewerDefId)
            return new ReviewContentResponse { Verdict = CoreContent.ContentReviewResult.NeedsHuman, Reason = "reviewer_independence" };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        timeout.CancelAfter(ReviewTimeout);
        var result = await _reviewer.ReviewAsync(tenantId, item.Platform, item.Body, timeout.Token).ConfigureAwait(false);

        var now = _clock.UtcNow;
        if (result.Verdict == CoreContent.ContentReviewResult.Approve)
        {
            item.ApproveByAgent(reviewerDefId.Value, now);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        }
        else if (result.Verdict == CoreContent.ContentReviewResult.RejectVerdict)
        {
            item.Reject(now, result.Reason);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
        }

        LogReviewed(_logger, tenantId, item.Id, result.Verdict, result.Reason);
        return new ReviewContentResponse
        {
            Verdict = result.Verdict,
            Reason = result.Reason,
            ReviewedByAgentDefinitionId = result.Verdict == CoreContent.ContentReviewResult.Approve ? reviewerDefId.Value.ToString() : string.Empty,
        };
    }

    private static Guid ParseTenantId(string tenantId)
    {
        if (!Guid.TryParse(tenantId, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        return parsed;
    }

    private static Guid? ParseOptionalGuid(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"{fieldName} invalid"));

        return parsed;
    }

    private static ContentVariant ToVariant(ContentItem item) =>
        new()
        {
            Platform = item.Platform,
            Title = item.Platform,
            Body = item.Body,
            ContentId = item.Id.ToString(),
        };

    [LoggerMessage(
        EventId = 5202,
        Level = LogLevel.Information,
        Message = "Reviewed content item {ContentItemId} tenant {TenantId}: verdict={Verdict} reason={Reason}")]
    private static partial void LogReviewed(ILogger logger, Guid tenantId, Guid contentItemId, string verdict, string reason);

    [LoggerMessage(
        EventId = 5201,
        Level = LogLevel.Information,
        Message = "Generated content draft {ContentItemId} for tenant {TenantId} platform {Platform} (inputTokens={InputTokens}, outputTokens={OutputTokens}, latencyMs={LatencyMs})")]
    private static partial void LogDraftGenerated(
        ILogger logger,
        Guid tenantId,
        Guid contentItemId,
        string platform,
        int inputTokens,
        int outputTokens,
        long latencyMs);
}
