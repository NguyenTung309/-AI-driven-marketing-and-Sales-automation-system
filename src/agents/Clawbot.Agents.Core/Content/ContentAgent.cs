using System.Diagnostics;
using System.Globalization;
using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Ops;

namespace Clawbot.Agents.Core.Content;

public sealed record ContentGenerateRequest(
    Guid TenantId,
    Guid? BriefId,
    string Platform,
    string Brief,
    string? KbModuleCode);

public sealed record ContentDraftResult(
    Guid? BriefId,
    string Platform,
    string Body,
    IReadOnlyList<RagChunk> Citations,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs);

public sealed class ContentAgent(
    IRagRetriever rag,
    IPromptTemplateProvider templates,
    IClaudeChatClient claude,
    ILlmCallScope llmScope,
    IClaudeCostTracker? costTracker = null)
{
    private const string AgentCode = "content-agent";

    private readonly IRagRetriever _rag = rag;
    private readonly IPromptTemplateProvider _templates = templates;
    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly IClaudeCostTracker? _costTracker = costTracker;

    public async Task<ContentDraftResult> GenerateAsync(ContentGenerateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Brief))
            throw new ArgumentException("brief required", nameof(request));

        // Resolve this agent's bound provider config (D8) — same per-tenant path as chat, no env drift.
        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var sw = Stopwatch.StartNew();
        var template = _templates.GetTemplate(request.Platform);
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, request.Brief, TopK: 4),
            ct).ConfigureAwait(false);

        // The rendered template carries all instructions; send it as the user message (no system),
        // mirroring the prior single-user-message content call.
        var prompt = RenderTemplate(template, request.Brief, BuildKnowledgeContext(chunks));
        var reply = await _claude.CompleteAsync(string.Empty, Array.Empty<ChatTurn>(), prompt, ct).ConfigureAwait(false);
        await RecordCostAsync(request.TenantId, reply, ct).ConfigureAwait(false);

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            reply.Text.Trim(),
            chunks,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            sw.ElapsedMilliseconds);
    }

    private async Task RecordCostAsync(Guid tenantId, ClaudeReply reply, CancellationToken ct)
    {
        if (_costTracker is null || reply.UsdCost <= 0m)
            return;

        await _costTracker.RecordAsync(new CostEntry(
            tenantId,
            AgentCode,
            reply.Model,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            _llmScope.Current?.CostAt ?? DateTimeOffset.UtcNow,
            _llmScope.Current?.ReservationId), ct).ConfigureAwait(false);
    }

    private static string RenderTemplate(string template, string brief, string knowledge) =>
        template
            .Replace("{{brief}}", brief, StringComparison.Ordinal)
            .Replace("{{knowledge}}", knowledge, StringComparison.Ordinal);

    private static string BuildKnowledgeContext(IReadOnlyList<RagChunk> chunks)
    {
        if (chunks.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"[{i + 1}] (module={chunk.KbModuleCode}, score={chunk.Score:0.00}) {chunk.Snippet}");
        }

        return sb.ToString().TrimEnd();
    }
}
