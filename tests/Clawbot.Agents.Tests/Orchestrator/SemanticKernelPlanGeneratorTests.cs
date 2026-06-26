using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class SemanticKernelPlanGeneratorTests
{
    private static readonly AgentCatalogEntry Content = new(
        "content-agent", "content", "Content", "content", "Run content", "{}", Orchestratable: true);

    [Fact]
    public async Task GenerateAsync_returns_validated_plan_from_llm_json()
    {
        var chat = new FixedClaudeChatClient("""
        {
          "version": 1,
          "tasks": [
            { "id": "t1", "agent": "content", "description": "write post", "input": { "brief": "HSK4" }, "dependsOn": [], "status": "pending" }
          ]
        }
        """);
        var generator = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(chat));

        var plan = await generator.GenerateAsync("launch HSK4", [Content], CancellationToken.None);

        plan.Tasks.Should().ContainSingle().Which.Agent.Should().Be("content");
        chat.UserMessage.Should().Contain("launch HSK4");
    }

    [Fact]
    public async Task GenerateAsync_includes_catalog_description_and_input_schema_in_prompt()
    {
        var content = new AgentCatalogEntry(
            "content-agent",
            "content",
            "Content",
            "content",
            "Write campaign content.",
            "{\"brief\":\"string\"}",
            Orchestratable: true);
        var chat = new FixedClaudeChatClient("""
        {
          "version": 1,
          "tasks": [
            { "id": "t1", "agent": "content", "description": "write post", "input": { "brief": "HSK4" }, "dependsOn": [], "status": "pending" }
          ]
        }
        """);
        var generator = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(chat));

        await generator.GenerateAsync("launch HSK4", [content], CancellationToken.None);

        chat.SystemPrompt.Should().Contain("Write campaign content.");
        chat.SystemPrompt.Should().Contain("{\"brief\":\"string\"}");
    }

    [Fact]
    public async Task GenerateAsync_accepts_markdown_fenced_json()
    {
        var chat = new FixedClaudeChatClient("""
        ```json
        {
          "version": 1,
          "tasks": [
            { "id": "t1", "agent": "content", "description": "write post", "input": { "brief": "HSK4" }, "dependsOn": [], "status": "pending" }
          ]
        }
        ```
        """);
        var generator = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(chat));

        var plan = await generator.GenerateAsync("launch HSK4", [Content], CancellationToken.None);

        plan.Tasks.Should().ContainSingle().Which.Id.Should().Be("t1");
    }

    [Fact]
    public async Task GenerateAsync_tolerates_string_version_and_rich_input_values()
    {
        var chat = new FixedClaudeChatClient("""
        {
          "version": "1.0",
          "tasks": [
            { "id": "t1", "agent": "content", "description": "schedule posts",
              "input": { "durationDays": 30, "platforms": ["Facebook", "Zalo"], "config": { "tz": "Asia/Ho_Chi_Minh" }, "active": true },
              "dependsOn": [], "status": "pending" }
          ]
        }
        """);
        var generator = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(chat));

        var plan = await generator.GenerateAsync("launch HSK4", [Content], CancellationToken.None);

        plan.Version.Should().Be(1);
        var input = plan.Tasks.Should().ContainSingle().Which.Input;
        input["durationDays"].Should().Be("30");
        input["platforms"].Should().Contain("Facebook").And.Contain("Zalo");
        input["config"].Should().Contain("Asia/Ho_Chi_Minh");
        input["active"].Should().Be("true");
    }

    [Fact]
    public async Task GenerateAsync_rejects_invalid_json()
    {
        var generator = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(new FixedClaudeChatClient("not-json")));

        Func<Task> act = async () => await generator.GenerateAsync("launch HSK4", [Content], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Mô hình trả về JSON không hợp lệ*");
    }

    [Fact]
    public async Task GenerateAsync_rejects_invalid_plan()
    {
        var chat = new FixedClaudeChatClient("""
        { "version": 1, "tasks": [ { "id": "t1", "agent": "missing", "description": "x", "input": {}, "dependsOn": [], "status": "pending" } ] }
        """);
        var generator = new SemanticKernelPlanGenerator(new ClawbotChatCompletionService(chat));

        Func<Task> act = async () => await generator.GenerateAsync("launch HSK4", [Content], CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Kế hoạch sai cấu trúc: unknown_agent:t1:missing*");
    }

    private sealed class FixedClaudeChatClient(string response) : IClaudeChatClient
    {
        public string? SystemPrompt { get; private set; }
        public string? UserMessage { get; private set; }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            SystemPrompt = systemPrompt;
            UserMessage = userMessage;
            return Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ClaudeStreamChunk(response, Final: false, 0, 0, 0m);
            yield return new ClaudeStreamChunk(string.Empty, Final: true, 1, 1, 0.01m, "test");
        }
    }
}
