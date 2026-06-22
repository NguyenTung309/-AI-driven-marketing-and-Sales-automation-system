using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Microsoft.SemanticKernel.ChatCompletion;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

public sealed class ClawbotChatCompletionServiceTests
{
    [Fact]
    public async Task GetChatMessageContentsAsync_maps_system_history_and_last_user_message()
    {
        var client = new RecordingClaudeChatClient();
        var service = new ClawbotChatCompletionService(client);
        var history = new ChatHistory("system prompt");
        history.AddUserMessage("first user");
        history.AddAssistantMessage("first assistant");
        history.AddUserMessage("final user");

        var result = await service.GetChatMessageContentsAsync(history, cancellationToken: CancellationToken.None);

        result.Should().ContainSingle().Which.Content.Should().Be("ok");
        client.SystemPrompt.Should().Be("system prompt");
        client.UserMessage.Should().Be("final user");
        client.History.Should().Equal(
            new ChatTurn("user", "first user"),
            new ChatTurn("assistant", "first assistant"));
    }

    [Fact]
    public async Task GetChatMessageContentsAsync_handles_missing_system_prompt()
    {
        var client = new RecordingClaudeChatClient();
        var service = new ClawbotChatCompletionService(client);
        var history = new ChatHistory();
        history.AddUserMessage("hello");

        var result = await service.GetChatMessageContentsAsync(history, cancellationToken: CancellationToken.None);

        result.Should().ContainSingle().Which.Content.Should().Be("ok");
        client.SystemPrompt.Should().BeEmpty();
        client.UserMessage.Should().Be("hello");
        client.History.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStreamingChatMessageContentsAsync_buffers_non_streaming_reply()
    {
        var client = new RecordingClaudeChatClient();
        var service = new ClawbotChatCompletionService(client);
        var history = new ChatHistory();
        history.AddUserMessage("hello");
        var chunks = new List<string?>();

        await foreach (var chunk in service.GetStreamingChatMessageContentsAsync(history, cancellationToken: CancellationToken.None))
            chunks.Add(chunk.Content);

        chunks.Should().Equal("ok");
    }

    [Fact]
    public async Task GetChatMessageContentsAsync_records_cost_when_llm_scope_is_available()
    {
        var client = new RecordingClaudeChatClient();
        var tracker = new RecordingCostTracker();
        var scope = new LlmCallScope();
        var service = new ClawbotChatCompletionService(client, tracker, scope);
        var tenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var history = new ChatHistory();
        history.AddUserMessage("hello");

        using (scope.Begin(tenantId, "orchestrator"))
        {
            await service.GetChatMessageContentsAsync(history, cancellationToken: CancellationToken.None);
        }

        tracker.Entries.Should().ContainSingle().Which.Should().Match<CostEntry>(entry =>
            entry.TenantId == tenantId &&
            entry.AgentCode == "orchestrator" &&
            entry.Model == "test-model" &&
            entry.InputTokens == 1 &&
            entry.OutputTokens == 1 &&
            entry.UsdCost == 0.01m);
    }

    private sealed class RecordingCostTracker : IClaudeCostTracker
    {
        public string Name => "cost";
        public List<CostEntry> Entries { get; } = [];

        public Task RecordAsync(CostEntry entry, CancellationToken ct)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct) =>
            Task.FromResult(new CostSummary(tenantId, 0m, 200m, 0f));
    }

    private sealed class RecordingClaudeChatClient : IClaudeChatClient
    {
        public string? SystemPrompt { get; private set; }
        public IReadOnlyList<ChatTurn> History { get; private set; } = [];
        public string? UserMessage { get; private set; }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            SystemPrompt = systemPrompt;
            History = history;
            UserMessage = userMessage;
            return Task.FromResult(new ClaudeReply("ok", 1, 1, 0.01m, "test-model"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ClaudeStreamChunk("ok", Final: false, 0, 0, 0m);
            yield return new ClaudeStreamChunk(string.Empty, Final: true, 1, 1, 0.01m, "test-model");
        }
    }
}
