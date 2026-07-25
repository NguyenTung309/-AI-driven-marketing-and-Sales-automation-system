using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace Clawbot.Agents.Core.Chat;

public sealed class ClawbotChatCompletionService(
    IClaudeChatClient client,
    ILlmCostTracker? costTracker = null,
    ILlmCallScope? llmScope = null) : IChatCompletionService
{
    private readonly IClaudeChatClient _client = client;
    private readonly ILlmCostTracker? _costTracker = costTracker;
    private readonly ILlmCallScope? _llmScope = llmScope;

    public IReadOnlyDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>();

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var (systemPrompt, turns, userMessage) = Map(chatHistory);
        var reply = await _client.CompleteAsync(systemPrompt, turns, userMessage, cancellationToken).ConfigureAwait(false);
        await RecordCostAsync(reply, cancellationToken).ConfigureAwait(false);
        return [new ChatMessageContent(AuthorRole.Assistant, reply.Text)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (systemPrompt, turns, userMessage) = Map(chatHistory);
        var reply = await _client.CompleteAsync(systemPrompt, turns, userMessage, cancellationToken).ConfigureAwait(false);
        await RecordCostAsync(reply, cancellationToken).ConfigureAwait(false);
        yield return new StreamingChatMessageContent(AuthorRole.Assistant, reply.Text);
    }

    private async Task RecordCostAsync(ClaudeReply reply, CancellationToken ct)
    {
        var current = _llmScope?.Current;
        if (_costTracker is null
            || current is null
            || (reply.UsdCost <= 0m && reply.InputTokens <= 0 && reply.OutputTokens <= 0))
            return;

        await _costTracker.RecordAsync(new CostEntry(
            current.Value.TenantId,
            current.Value.AgentCode,
            reply.Model,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            current.Value.CostAt ?? DateTimeOffset.UtcNow,
            current.Value.ReservationId,
            SessionId: null,
            IsEstimated: reply.IsEstimated), ct).ConfigureAwait(false);
    }

    private static (string SystemPrompt, IReadOnlyList<ChatTurn> History, string UserMessage) Map(ChatHistory chatHistory)
    {
        var messages = chatHistory.ToArray();
        var systemPrompt = string.Empty;
        var start = 0;
        if (messages.Length > 0 && messages[0].Role == AuthorRole.System)
        {
            systemPrompt = messages[0].Content ?? string.Empty;
            start = 1;
        }

        var userIndex = Array.FindLastIndex(messages, message => message.Role == AuthorRole.User);
        if (userIndex < start)
            throw new InvalidOperationException("Chat history must contain a user message.");

        var history = messages
            .Skip(start)
            .Take(userIndex - start)
            .Where(message => message.Role == AuthorRole.User || message.Role == AuthorRole.Assistant)
            .Select(message => new ChatTurn(message.Role.Label, message.Content ?? string.Empty))
            .ToArray();

        return (systemPrompt, history, messages[userIndex].Content ?? string.Empty);
    }
}
