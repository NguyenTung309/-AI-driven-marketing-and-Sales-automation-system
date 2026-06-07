using System.Diagnostics;
using System.Globalization;
using System.Text;
using Clawbot.Agents.Core.Rag;

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
    long LatencyMs);

public sealed class ContentAgent(
    IRagRetriever rag,
    IPromptTemplateProvider templates,
    IContentLlmClient llm)
{
    private readonly IRagRetriever _rag = rag;
    private readonly IPromptTemplateProvider _templates = templates;
    private readonly IContentLlmClient _llm = llm;

    public async Task<ContentDraftResult> GenerateAsync(ContentGenerateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Brief))
            throw new ArgumentException("brief required", nameof(request));

        var sw = Stopwatch.StartNew();
        var template = _templates.GetTemplate(request.Platform);
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, request.Brief, TopK: 4),
            ct).ConfigureAwait(false);

        var prompt = RenderTemplate(template, request.Brief, BuildKnowledgeContext(chunks));
        var completion = await _llm.CompleteAsync(
            new ContentLlmRequest(request.TenantId, request.Platform, prompt),
            ct).ConfigureAwait(false);

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            completion.Text.Trim(),
            chunks,
            completion.InputTokens,
            completion.OutputTokens,
            sw.ElapsedMilliseconds);
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
