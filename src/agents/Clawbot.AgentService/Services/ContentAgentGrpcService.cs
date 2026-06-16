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
    AppDbContext db,
    IClock clock,
    ILogger<ContentAgentGrpcService> logger) : ContentAgent.ContentAgentBase
{
    private readonly CoreContent.ContentAgent _agent = agent;
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

        var draft = await _agent.GenerateAsync(
            new CoreContent.ContentGenerateRequest(
                tenantId, briefId, request.Channel, request.Brief, KbModuleCode: null),
            context.CancellationToken).ConfigureAwait(false);

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
