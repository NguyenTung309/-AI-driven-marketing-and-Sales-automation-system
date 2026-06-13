using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoreSale = Clawbot.Agents.Core.SaleAssist;

namespace Clawbot.AgentService.Services;

public sealed partial class SaleAssistAgentGrpcService(
    CoreSale.SaleAssistAgent agent,
    IPiiRedactor pii,
    AppDbContext db,
    IClock clock,
    ILogger<SaleAssistAgentGrpcService> logger) : SaleAssistAgent.SaleAssistAgentBase
{
    private const int RecentTurnsLimit = 12;

    private readonly CoreSale.SaleAssistAgent _agent = agent;
    private readonly IPiiRedactor _pii = pii;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly ILogger<SaleAssistAgentGrpcService> _logger = logger;

    public override async Task<DraftResponse> Draft(DraftRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ctx = await LoadContextAsync(request.TenantId, request.ConversationId, context.CancellationToken).ConfigureAwait(false);
        var result = await _agent.DraftAsync(ctx, context.CancellationToken).ConfigureAwait(false);

        return new DraftResponse
        {
            DraftText = result.DraftText,
            SuggestedAction = result.SuggestedAction,
            LeadScore = result.LeadScoreHint,
        };
    }

    public override async Task<SummarizeResponse> Summarize(SummarizeRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ctx = await LoadContextAsync(request.TenantId, request.ConversationId, context.CancellationToken).ConfigureAwait(false);
        var result = await _agent.SummarizeAsync(ctx, context.CancellationToken).ConfigureAwait(false);

        return new SummarizeResponse { Summary = result.Summary };
    }

    public override async Task<AutoSummaryResponse> AutoSummaryOnResolve(AutoSummaryRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var ctx = await LoadContextAsync(request.TenantId, request.ConversationId, context.CancellationToken).ConfigureAwait(false);
        var result = await _agent.AutoSummaryAsync(ctx, context.CancellationToken).ConfigureAwait(false);

        // Persist auto-summary to agent_sessions trace (no schema change)
        // Redact PII from summary before persisting — derived from raw customer messages
        if (Guid.TryParse(request.TenantId, out var tenantId) && Guid.TryParse(request.ConversationId, out var convId))
        {
            var redactedSummary = await _pii.RedactAsync(result.Summary, context.CancellationToken).ConfigureAwait(false);
            var redactedKeyPoints = new List<string>();
            foreach (var kp in result.KeyPoints)
            {
                var redacted = await _pii.RedactAsync(kp, context.CancellationToken).ConfigureAwait(false);
                redactedKeyPoints.Add(redacted.RedactedText);
            }

            var traceContent = $"summary={redactedSummary.RedactedText} key_points={redactedKeyPoints.Count}";
            var session = AgentSession.Start(tenantId, agentId: null, conversationId: convId,
                goal: "auto-summary-on-resolve", startedAt: _clock.UtcNow);
            session.AppendTrace("sale-assist", "auto-summary", "completed", traceContent, _clock.UtcNow);
            session.Finish(_clock.UtcNow);
            _db.AgentSessions.Add(session);
            await _db.SaveChangesAsync(context.CancellationToken).ConfigureAwait(false);
            LogAutoSummaryPersisted(_logger, convId, redactedKeyPoints.Count);

            // Return redacted summary to caller
            var redactedResponse = new AutoSummaryResponse
            {
                Summary = redactedSummary.RedactedText,
                Persisted = true
            };
            redactedResponse.KeyPoints.AddRange(redactedKeyPoints);
            return redactedResponse;
        }

        // Fallback: GUID parse failed, return unredacted (shouldn't happen in practice)
        var response = new AutoSummaryResponse
        {
            Summary = result.Summary,
            Persisted = false
        };
        response.KeyPoints.AddRange(result.KeyPoints);
        return response;
    }

    public override async Task<UpsellResponse> Upsell(UpsellRequest request, ServerCallContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        if (!Guid.TryParse(request.TenantId, out var tid) || tid == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(request.ConversationId, out var cid) || cid == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "conversation_id required"));

        var conv = await _db.Conversations.IgnoreQueryFilters()
            .Where(c => c.Id == cid && c.TenantId == tid)
            .Select(c => new { c.ContactId })
            .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "conversation not found"));

        // Hybrid gate: only spend LLM tokens when the lead is near closing (stage 'hot').
        var lead = conv.ContactId is null ? null : await _db.Leads.IgnoreQueryFilters()
            .Where(l => l.TenantId == tid && l.ContactId == conv.ContactId && l.DeletedAt == null)
            .OrderByDescending(l => l.Score)
            .Select(l => new { l.Stage, l.Score })
            .FirstOrDefaultAsync(context.CancellationToken).ConfigureAwait(false);

        if (lead is null || lead.Stage != "hot")
        {
            return new UpsellResponse
            {
                Eligible = false,
                Suggestion = string.Empty,
                Reason = lead is null ? "no lead for conversation" : $"lead stage '{lead.Stage}' not hot yet",
                LeadScore = lead?.Score ?? 0,
            };
        }

        var ctx = await LoadContextAsync(request.TenantId, request.ConversationId, context.CancellationToken).ConfigureAwait(false);
        var result = await _agent.SuggestUpsellAsync(ctx, context.CancellationToken).ConfigureAwait(false);

        var hasSignal = result.Suggestion.Length > 0
            && !string.Equals(result.Suggestion, "NONE", StringComparison.OrdinalIgnoreCase);
        return new UpsellResponse
        {
            Eligible = hasSignal,
            Suggestion = hasSignal ? result.Suggestion : string.Empty,
            Reason = hasSignal ? "hot lead with closing signal" : "no closing signal detected",
            LeadScore = lead.Score,
        };
    }

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information, Message = "Auto-summary persisted for conversation {ConversationId} with {KeyPointCount} key points")]
    private static partial void LogAutoSummaryPersisted(ILogger logger, Guid conversationId, int keyPointCount);

    private async Task<CoreSale.ConversationContext> LoadContextAsync(string tenantId, string conversationId, CancellationToken ct)
    {
        if (!Guid.TryParse(tenantId, out var tid) || tid == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "tenant_id required"));
        if (!Guid.TryParse(conversationId, out var cid) || cid == Guid.Empty)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "conversation_id required"));

        var conv = await _db.Conversations
            .IgnoreQueryFilters()
            .Where(c => c.Id == cid && c.TenantId == tid)
            .Select(c => new { c.Id, c.Platform, c.ContactId })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? throw new RpcException(new Status(StatusCode.NotFound, "conversation not found"));

        var contactName = conv.ContactId is null
            ? null
            : await _db.Contacts.IgnoreQueryFilters()
                .Where(c => c.Id == conv.ContactId)
                .Select(c => c.DisplayName)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var turns = await _db.Messages
            .IgnoreQueryFilters()
            .Where(m => m.ConversationId == cid && m.TenantId == tid)
            .OrderByDescending(m => m.SentAt)
            .Take(RecentTurnsLimit)
            .Select(m => new CoreSale.TurnSnapshot(m.Direction, m.Content, m.SentAt))
            .ToListAsync(ct).ConfigureAwait(false);
        turns.Reverse();

        return new CoreSale.ConversationContext(tid, cid, contactName, conv.Platform, turns);
    }
}
