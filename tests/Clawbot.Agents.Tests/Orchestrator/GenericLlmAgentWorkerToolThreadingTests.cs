using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

// Regression guard for the cross-agent threading fix: the ReAct loop must fold successful tool-result JSON
// (content_id, etc.) into AgentResult.Output so the orchestrator's upstream_results carries it to the next
// agent. Before the fix only the LLM's free-text final answer was returned and the id was dropped.
public sealed class GenericLlmAgentWorkerToolThreadingTests
{
    private static GenericLlmAgentWorker BuildWorker(IClaudeChatClient chat)
    {
        var definition = new AgentDefinitionCatalogEntry(
            Id: Guid.NewGuid(),
            Code: "content-agent",
            ShortName: "content",
            DisplayName: "Content Agent",
            AgentType: "content",
            Description: "Create a draft.",
            InputSchemaJson: "{}",
            Orchestratable: true,
            KbModuleCode: null,
            AllowedToolsJson: """["content-agent"]""");

        var registry = new ToolRegistry(new IAgentTool[] { new FakeContentTool() });
        return new GenericLlmAgentWorker(
            definition,
            new EmptyRag(),
            chat,
            new OrchestratorCostGuard(new InMemoryLlmCostTracker()),
            new LlmCallScope(),
            registry);
    }

    private static AgentTask TaskWithTenant() =>
        new("task-1", "content-agent", "Draft a post.", new Dictionary<string, string>
        {
            ["tenant_id"] = Guid.NewGuid().ToString("D"),
        });

    [Fact]
    public async Task ExecuteAsync_FoldsToolResultIntoOutput_WhenModelFinishesWithText()
    {
        // Arrange: model calls the tool, then replies with a plain-text final answer.
        var chat = new ScriptedChatClient(
            """{"tool":"content-agent","args":{"platform":"facebook","brief":"x"}}""",
            "Done — draft created.");
        var worker = BuildWorker(chat);

        // Act
        var result = await worker.ExecuteAsync(TaskWithTenant(), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("[tool_results]");
        result.Output.Should().Contain("content_id");
        result.Output.Should().Contain("c-123");
        result.Output.Should().Contain("Done — draft created.");
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsToolResultsAsSuccess_WhenLoopHitsIterationCap()
    {
        // Arrange: model only ever emits tool actions (never a final answer), so the loop exhausts its cap.
        var chat = new AlwaysToolChatClient("""{"tool":"content-agent","args":{"platform":"facebook"}}""");
        var worker = BuildWorker(chat);

        // Act
        var result = await worker.ExecuteAsync(TaskWithTenant(), CancellationToken.None);

        // Assert: the persisted draft isn't orphaned — its id still threads downstream.
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("content_id");
        result.Output.Should().Contain("c-123");
    }

    [Fact]
    public async Task ExecuteAsync_Fails_WhenTextOnlyAdmitsMissingLeadList()
    {
        var definition = new AgentDefinitionCatalogEntry(
            Id: Guid.NewGuid(),
            Code: "lead-agent",
            ShortName: "lead",
            DisplayName: "Lead Agent",
            AgentType: "lead",
            Description: "List leads",
            InputSchemaJson: "{}",
            Orchestratable: true,
            KbModuleCode: null,
            AllowedToolsJson: "[]"); // text-only path
        var worker = new GenericLlmAgentWorker(
            definition,
            new EmptyRag(),
            new ScriptedChatClient("Thiếu danh sách 11 lead gốc. Input chỉ có tenant_id."),
            new OrchestratorCostGuard(new InMemoryLlmCostTracker()),
            new LlmCallScope(),
            new ToolRegistry(Array.Empty<IAgentTool>()));

        var result = await worker.ExecuteAsync(
            new AgentTask("t1", "lead-agent", "Xác định lead lạnh", new Dictionary<string, string>
            {
                ["tenant_id"] = Guid.NewGuid().ToString("D"),
            }),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("blocked_missing_input");
    }

    [Theory]
    [InlineData("Thiếu danh sách 11 lead gốc", true)]
    [InlineData("please provide the list of leads", true)]
    [InlineData("Draft created successfully", false)]
    public void LooksLikeBlockedMissingData_detects_soft_fail_phrases(string text, bool expected)
    {
        GenericLlmAgentWorker.LooksLikeBlockedMissingData(text).Should().Be(expected);
    }

    [Fact]
    public async Task ExecuteAsync_UsesDefaultTools_WhenAllowedToolsJsonEmpty_ButRegistryHasTool()
    {
        // DB grant [] nhưng AgentToolDefaults gán lead-agent → ReAct path, không text-only.
        var definition = new AgentDefinitionCatalogEntry(
            Id: Guid.NewGuid(),
            Code: "lead-agent",
            ShortName: "lead",
            DisplayName: "Lead Agent",
            AgentType: "lead",
            Description: "List leads",
            InputSchemaJson: "{}",
            Orchestratable: true,
            KbModuleCode: null,
            AllowedToolsJson: "[]");
        var registry = new ToolRegistry(new IAgentTool[] { new FakeLeadListTool() });
        var worker = new GenericLlmAgentWorker(
            definition,
            new EmptyRag(),
            new ScriptedChatClient(
                """{"tool":"lead-agent","args":{"operation":"list","stage":"cold"}}""",
                "Listed cold leads."),
            new OrchestratorCostGuard(new InMemoryLlmCostTracker()),
            new LlmCallScope(),
            registry);

        var result = await worker.ExecuteAsync(
            new AgentTask("t1", "lead-agent", "Xác định lead lạnh", new Dictionary<string, string>
            {
                ["tenant_id"] = Guid.NewGuid().ToString("D"),
            }),
            CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("lead_ids");
        result.Output.Should().Contain("lead-1");
    }

    private sealed class FakeLeadListTool : IAgentTool
    {
        public string Name => "lead-agent";
        public string Description => "List leads";
        public string InputSchemaJson => "{}";
        public string RequiredPermission => "leads:write";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

        public Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct) =>
            Task.FromResult(ToolResult.Ok("""{"operation":"list","total":1,"lead_ids":["lead-1"],"items":[{"lead_id":"lead-1","stage":"cold"}]}"""));
    }

    private sealed class FakeContentTool : IAgentTool
    {
        public string Name => "content-agent";
        public string Description => "Create and persist a draft.";
        public string InputSchemaJson => "{}";
        public string RequiredPermission => "content:write";
        public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

        public Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct) =>
            Task.FromResult(ToolResult.Ok("""{"content_id":"c-123","platform":"facebook","status":"draft"}"""));
    }

    private sealed class EmptyRag : IRagRetriever
    {
        public Task<IReadOnlyList<RagChunk>> RetrieveAsync(RagRequest request, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<RagChunk>>(Array.Empty<RagChunk>());
    }

    // Returns each scripted reply in order; repeats the last once exhausted.
    private sealed class ScriptedChatClient(params string[] replies) : IClaudeChatClient
    {
        private int _index;

        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default)
        {
            var text = replies[Math.Min(_index, replies.Length - 1)];
            _index++;
            return Task.FromResult(new ClaudeReply(text, 1, 1, 0m));
        }

        public IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class AlwaysToolChatClient(string action) : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            Task.FromResult(new ClaudeReply(action, 1, 1, 0m));

        public IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
