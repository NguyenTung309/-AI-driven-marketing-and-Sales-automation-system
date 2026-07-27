namespace Clawbot.Agents.Core.Chat;

public sealed record ChatTurn(string Role, string Content);

// IsEstimated = token/cost do LlmTokenEstimator đếm cục bộ vì provider không trả usage.
// Số ước lượng luôn thấp hơn thực tế (không thấy reasoning token) -> phải gắn nhãn trên UI.
public sealed record ClaudeReply(string Text, int InputTokens, int OutputTokens, decimal UsdCost, string Model = "", bool IsEstimated = false);

public sealed record ClaudeStreamChunk(string Text, bool Final, int InputTokens, int OutputTokens, decimal UsdCost, string Model = "", bool IsEstimated = false);

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
