using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Clawbot.Api.Services;

public sealed class SaleAssistDraftFeedbackService(
    AppDbContext db,
    IPiiRedactor pii,
    IClock clock)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sent",
        "edited",
        "discarded",
    };

    private readonly AppDbContext _db = db;
    private readonly IPiiRedactor _pii = pii;
    private readonly IClock _clock = clock;

    public async Task<SaleAssistDraftFeedbackResponse> RecordAsync(
        Guid tenantId,
        SaleAssistDraftFeedbackRequest request,
        CancellationToken ct = default)
    {
        if (request.ConversationId == Guid.Empty)
            throw new ArgumentException("conversationId required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DraftText))
            throw new ArgumentException("draftText required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Outcome) || !AllowedOutcomes.Contains(request.Outcome.Trim()))
            throw new ArgumentException("outcome must be sent, edited, or discarded", nameof(request));

        var conversationExists = await _db.Conversations.IgnoreQueryFilters()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == request.ConversationId && c.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (!conversationExists)
            throw new KeyNotFoundException("conversation not found");

        var finalText = request.FinalText ?? string.Empty;
        var redactedDraft = await _pii.RedactAsync(request.DraftText, ct).ConfigureAwait(false);
        var redactedFinal = await _pii.RedactAsync(finalText, ct).ConfigureAwait(false);
        var edited = !string.Equals(request.DraftText.Trim(), finalText.Trim(), StringComparison.Ordinal);
        var now = _clock.UtcNow;
        var outcome = request.Outcome.Trim().ToLowerInvariant();
        var payload = JsonSerializer.Serialize(new
        {
            outcome,
            edited,
            draftText = redactedDraft.RedactedText,
            finalText = redactedFinal.RedactedText,
            draftPiiSpanCount = redactedDraft.Spans.Count,
            finalPiiSpanCount = redactedFinal.Spans.Count,
        }, JsonOptions);

        var session = AgentSession.Start(
            tenantId,
            agentId: null,
            conversationId: request.ConversationId,
            goal: "sale-assist-draft-feedback",
            startedAt: now);
        session.AppendTrace("sale-assist", "draft-feedback", "recorded", payload, now);
        session.Finish(now);
        _db.AgentSessions.Add(session);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return new SaleAssistDraftFeedbackResponse(session.Id, edited, now);
    }
}
