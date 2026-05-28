namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record ConversationTurn(string Role, string Content, DateTimeOffset At);

public sealed record SummaryResult(string Summary, IReadOnlyList<string> KeyPoints);

public interface IConversationSummarizer : ISkill
{
    Task<SummaryResult> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, int maxWords, CancellationToken ct);
}

// Source: Anthropic Claude Sonnet 4.6 via Semantic Kernel chat completion.
internal sealed class ClaudeConversationSummarizer : IConversationSummarizer
{
    public string Name => "conversation-summarization";

    public Task<SummaryResult> SummarizeAsync(IReadOnlyList<ConversationTurn> turns, int maxWords, CancellationToken ct) =>
        throw new NotImplementedException("TODO: build Claude prompt with turn list + max-word constraint via SK.");
}
