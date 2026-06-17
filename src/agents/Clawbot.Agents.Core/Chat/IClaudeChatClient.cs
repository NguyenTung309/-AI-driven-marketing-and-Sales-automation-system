namespace Clawbot.Agents.Core.Chat;

public sealed record ChatTurn(string Role, string Content);

public sealed record ClaudeReply(string Text, int InputTokens, int OutputTokens, decimal UsdCost);

public sealed record ClaudeStreamChunk(string Text, bool Final, int InputTokens, int OutputTokens, decimal UsdCost);

public interface IClaudeChatClient
{
    Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default);

    IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default);
}
