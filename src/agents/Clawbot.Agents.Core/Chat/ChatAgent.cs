using System.Globalization;
using System.Text;
using Clawbot.Agents.Core.Rag;

namespace Clawbot.Agents.Core.Chat;

public sealed record ChatAgentRequest(
    Guid TenantId,
    Guid? ConversationId,
    string? KbModuleCode,
    string UserText,
    IReadOnlyList<ChatTurn> History);

public sealed record ChatAgentReply(
    string Text,
    IReadOnlyList<RagChunk> Citations,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs);

public sealed class ChatAgent(IRagRetriever rag, IClaudeChatClient claude)
{
    private const string DefaultSystemPrompt =
        "You are ClawBot — an omnichannel sales assistant for a Chinese-language tutoring center. " +
        "Answer concisely in the customer's language (default Vietnamese, switch to Chinese if asked). " +
        "Cite knowledge-base snippets when used. If unsure, say so and offer to escalate to a human sales rep.";

    private readonly IRagRetriever _rag = rag;
    private readonly IClaudeChatClient _claude = claude;

    public async Task<ChatAgentReply> ReplyAsync(ChatAgentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = System.Diagnostics.Stopwatch.StartNew();

        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, request.UserText, TopK: 4),
            ct).ConfigureAwait(false);

        var system = BuildSystemPrompt(chunks);
        var reply = await _claude.CompleteAsync(system, request.History, request.UserText, ct).ConfigureAwait(false);

        started.Stop();
        return new ChatAgentReply(reply.Text, chunks, reply.InputTokens, reply.OutputTokens, reply.UsdCost, started.ElapsedMilliseconds);
    }

    private static string BuildSystemPrompt(IReadOnlyList<RagChunk> chunks)
    {
        if (chunks.Count == 0) return DefaultSystemPrompt;

        var sb = new StringBuilder(DefaultSystemPrompt.Length + 256);
        sb.AppendLine(DefaultSystemPrompt);
        sb.AppendLine();
        sb.AppendLine("## Knowledge base snippets (cite by [#index] when used):");
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"[{i + 1}] (module={c.KbModuleCode}, score={c.Score:0.00}) {c.Snippet}");
        }
        return sb.ToString();
    }
}
