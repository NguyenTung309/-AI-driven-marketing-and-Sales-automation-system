using System.Text.Json;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Research;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests;

public sealed class ResearchAgentToolUseTests
{
    [Fact]
    public async Task ResearchAdapter_UsesVnWhenGeoIsMissing()
    {
        var tenantId = Guid.NewGuid();
        var researchAgent = Substitute.For<IResearchAgent>();
        ResearchScanRequest? captured = null;
        researchAgent.ScanAsync(
                Arg.Do<ResearchScanRequest>(request => captured = request),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredTrend>>([]));

        var adapter = new ResearchAgentAdapter(researchAgent);
        var result = await adapter.ExecuteAsync(new AgentTask(
            "task-1",
            "research-agent",
            "Quét xu hướng",
            new Dictionary<string, string> { ["tenant_id"] = tenantId.ToString("D") }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        captured.Should().NotBeNull();
        captured!.Geo.Should().Be("VN");
        Parse(result.Output).GetProperty("geo").GetString().Should().Be("VN");
        Parse(result.Output).GetProperty("matched").GetInt32().Should().Be(0);
        Parse(result.Output).GetProperty("hint").GetString().Should().Contain("web.search");
    }

    [Fact]
    public async Task ResearchAdapter_PreservesExplicitGeoAndReturnsEnvelopeForResults()
    {
        var researchAgent = Substitute.For<IResearchAgent>();
        researchAgent.ScanAsync(Arg.Any<ResearchScanRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredTrend>>([
                new ScoredTrend("HSK4", "google_trends", "100", 11.2, ["Soạn bài HSK4"]),
            ]));

        var adapter = new ResearchAgentAdapter(researchAgent);
        var result = await adapter.ExecuteAsync(new AgentTask(
            "task-2",
            "research-agent",
            "Quét HSK4",
            new Dictionary<string, string>
            {
                ["tenant_id"] = Guid.NewGuid().ToString("D"),
                ["geo"] = "US",
                ["keywords"] = "hsk4, mandarin",
            }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var output = Parse(result.Output);
        output.GetProperty("geo").GetString().Should().Be("US");
        output.GetProperty("matched").GetInt32().Should().Be(1);
        output.GetProperty("trends").GetArrayLength().Should().Be(1);
        output.TryGetProperty("hint", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("Không quét được. Không có quyền truy cập nguồn dữ liệu cho tenant.")]
    [InlineData("Unable to access the configured source; chuyển nhân viên hỗ trợ.")]
    public void LooksLikeBlockedMissingData_RecognizesResearchRefusal(string text)
    {
        GenericLlmAgentWorker.LooksLikeBlockedMissingData(text).Should().BeTrue();
    }

    [Fact]
    public void LooksLikeBlockedMissingData_DoesNotClassifyLongAnalysisAsRefusal()
    {
        var text = new string('a', 401) + " không thể truy cập một nguồn phụ.";

        GenericLlmAgentWorker.LooksLikeBlockedMissingData(text).Should().BeFalse();
    }

    [Fact]
    public async Task TextOnlyWorker_PreservesAgentPersonaWhenTaskHasRoleInstruction()
    {
        // Arrange
        var chatClient = Substitute.For<IClaudeChatClient>();
        string? systemPrompt = null;
        chatClient.CompleteAsync(
                Arg.Do<string>(prompt => systemPrompt = prompt),
                Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Reply("Đã hoàn thành.")));
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(),
            "content-agent",
            "content",
            "Content Agent",
            "content",
            "PERMANENT_AGENT_PERSONA",
            "{}",
            true,
            null,
            "[]");
        var worker = new GenericLlmAgentWorker(
            definition,
            Substitute.For<IRagRetriever>(),
            chatClient,
            new OrchestratorCostGuard(Substitute.For<ILlmCostTracker>()),
            Substitute.For<ILlmCallScope>());
        var task = new AgentTask(
            "task-prompt",
            "content-agent",
            "Viết nội dung",
            new Dictionary<string, string> { ["tenant_id"] = Guid.NewGuid().ToString("D") },
            RoleInstruction: "TASK_SPECIFIC_ROLE_INSTRUCTION");

        // Act
        await worker.ExecuteAsync(task, CancellationToken.None);

        // Assert
        systemPrompt.Should().Contain("PERMANENT_AGENT_PERSONA");
        systemPrompt.Should().Contain("TASK_SPECIFIC_ROLE_INSTRUCTION");
    }

    [Fact]
    public void BackOfficeGuardrail_RequiresToolUseWithoutCustomerHandoff()
    {
        AgentPromptDefaults.BackOfficeGuardrail.Should().Contain("gọi tool");
        AgentPromptDefaults.BackOfficeGuardrail.Should().NotContain("chuyển nhân viên hỗ trợ");
        AgentPromptDefaults.BaseGuardrail.Should().Contain("chuyển nhân viên hỗ trợ");
    }

    [Fact]
    public void ResearchToolMetadata_ExplainsFreshnessBoundary()
    {
        var metadata = ToolRegistryFactory.KnownTools;

        metadata["research-agent"].Description.Should().Contain("Không lọc theo ngày");
        metadata["web.search"].Description.Should().Contain("nội dung mới theo ngày");
    }

    [Fact]
    public async Task ReActWorker_PreservesAgentPersonaWhenTaskHasRoleInstruction()
    {
        // Arrange
        var chatClient = Substitute.For<IClaudeChatClient>();
        var replies = new Queue<ClaudeReply>([
            Reply("{\"tool\":\"research-agent\",\"args\":{}}"),
            Reply("Đã hoàn thành."),
        ]);
        string? systemPrompt = null;
        chatClient.CompleteAsync(
                Arg.Do<string>(prompt => systemPrompt ??= prompt),
                Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(replies.Dequeue()));
        var tool = CreateTool(ToolResult.Ok("{}"));
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(),
            "research-agent",
            "research",
            "Research",
            "research",
            "PERMANENT_AGENT_PERSONA",
            "{}",
            true,
            null,
            "[\"research-agent\"]");
        var worker = new GenericLlmAgentWorker(
            definition,
            Substitute.For<IRagRetriever>(),
            chatClient,
            new OrchestratorCostGuard(Substitute.For<ILlmCostTracker>()),
            Substitute.For<ILlmCallScope>(),
            new ToolRegistry([tool]));
        var task = new AgentTask(
            "task-react-prompt",
            "research-agent",
            "Nghiên cứu",
            new Dictionary<string, string> { ["tenant_id"] = Guid.NewGuid().ToString("D") },
            RoleInstruction: "TASK_SPECIFIC_ROLE_INSTRUCTION");

        // Act
        var result = await worker.ExecuteAsync(task, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        systemPrompt.Should().Contain("PERMANENT_AGENT_PERSONA");
        systemPrompt.Should().Contain("TASK_SPECIFIC_ROLE_INSTRUCTION");
        systemPrompt!.IndexOf("PERMANENT_AGENT_PERSONA", StringComparison.Ordinal)
            .Should().BeLessThan(systemPrompt.IndexOf("TASK_SPECIFIC_ROLE_INSTRUCTION", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReActWorker_NudgesOnceThenFails_WhenModelNeverCallsTool()
    {
        var chatClient = CreateChatClient(
            Reply("Không có quyền truy cập dữ liệu."),
            Reply("Vẫn không thể thực hiện."));

        var worker = CreateWorker(chatClient, CreateTool(ToolResult.Ok("{}")));
        var result = await worker.ExecuteAsync(BuildTask("Quét trend"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("refused_without_tool_use");
        await chatClient.Received(2).CompleteAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReActWorker_DoesNotComplete_WhenToolFailsAndModelStops()
    {
        var chatClient = CreateChatClient(
            Reply("{\"tool\":\"research-agent\",\"args\":{}}"),
            Reply("Tool không truy cập được nguồn dữ liệu."));

        var worker = CreateWorker(chatClient, CreateTool(ToolResult.Fail("source_unavailable")));
        var result = await worker.ExecuteAsync(BuildTask("Quét trend"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("source_unavailable");
    }

    [Fact]
    public async Task ReActWorker_CompletesAndThreadsToolOutput_WhenToolSucceeds()
    {
        var chatClient = CreateChatClient(
            Reply("{\"tool\":\"research-agent\",\"args\":{}}"),
            Reply("Đã quét xong."));

        var worker = CreateWorker(chatClient, CreateTool(ToolResult.Ok("{\"matched\":1}")));
        var result = await worker.ExecuteAsync(BuildTask("Quét trend"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("[tool_results]");
        result.Output.Should().Contain("matched");
    }

    [Fact]
    public async Task ReActWorker_DoesNotComplete_WhenLaterToolFailsAfterEarlierSuccess()
    {
        var chatClient = CreateChatClient(
            Reply("{\"tool\":\"research-agent\",\"args\":{\"step\":\"1\"}}"),
            Reply("{\"tool\":\"research-agent\",\"args\":{\"step\":\"2\"}}"),
            Reply("Đã quét xong, phần còn lại tôi không làm được."));

        var worker = CreateWorker(chatClient, CreateTool(
            ToolResult.Ok("{\"matched\":1}"),
            ToolResult.Fail("schedule_unavailable")));
        var result = await worker.ExecuteAsync(BuildTask("Quét trend rồi lên lịch"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("schedule_unavailable");
        // Kết quả của bước đã chạy được vẫn phải giữ lại để side effect không mồ côi.
        result.Output.Should().Contain("[tool_results]");
        result.Output.Should().Contain("matched");
    }

    [Fact]
    public async Task ReActWorker_Completes_WhenModelRetriesFailedToolSuccessfully()
    {
        var chatClient = CreateChatClient(
            Reply("{\"tool\":\"research-agent\",\"args\":{\"geo\":\"XX\"}}"),
            Reply("{\"tool\":\"research-agent\",\"args\":{\"geo\":\"VN\"}}"),
            Reply("Đã quét xong."));

        var worker = CreateWorker(chatClient, CreateTool(
            ToolResult.Fail("bad_geo"),
            ToolResult.Ok("{\"matched\":2}")));
        var result = await worker.ExecuteAsync(BuildTask("Quét trend"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Output.Should().Contain("matched");
    }

    [Fact]
    public async Task ReActWorker_DoesNotComplete_WhenModelCallsUnknownTool()
    {
        var chatClient = CreateChatClient(
            Reply("{\"tool\":\"content.schedule\",\"args\":{}}"),
            Reply("Tôi không lên lịch được."));

        var worker = CreateWorker(chatClient, CreateTool(ToolResult.Ok("{}")));
        var result = await worker.ExecuteAsync(BuildTask("Lên lịch bài"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("unknown_tool");
        // Đã là một lần thử hành động nên không nhắc lại, model chỉ được gọi đúng 2 lần.
        await chatClient.Received(2).CompleteAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReActWorker_SynthesizesFinalText_WhenMaxIterationsReachedWithToolOutputs()
    {
        // Model emits 5 tool calls, exhausting MaxReActIterations (5), then 6th call is synthesis step
        var chatClient = CreateChatClient(
            Reply("{\"tool\":\"research-agent\",\"args\":{\"query\":\"1\"}}"),
            Reply("{\"tool\":\"research-agent\",\"args\":{\"query\":\"2\"}}"),
            Reply("{\"tool\":\"research-agent\",\"args\":{\"query\":\"3\"}}"),
            Reply("{\"tool\":\"research-agent\",\"args\":{\"query\":\"4\"}}"),
            Reply("{\"tool\":\"research-agent\",\"args\":{\"query\":\"5\"}}"),
            Reply("Báo cáo tổng hợp kết quả phân tích đầy đủ và chi tiết."));

        var worker = CreateWorker(chatClient, CreateTool(ToolResult.Ok("{\"data\":1}")));
        var result = await worker.ExecuteAsync(BuildTask("Phân tích tổng hợp"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Output.Should().NotContain("(reached tool step cap");
        result.Output.Should().Contain("Báo cáo tổng hợp kết quả phân tích đầy đủ và chi tiết.");
        result.Output.Should().Contain("[tool_results]");
    }

    [Fact]
    public async Task ReActWorker_GrantsPublishCapabilityOnlyToExecutionPrincipal()
    {
        ToolContext? observedContext = null;
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("content.publish");
        tool.Description.Returns("Queue publishing.");
        tool.InputSchemaJson.Returns("{}");
        tool.RequiredPermission.Returns("content:publish");
        tool.RiskLevel.Returns(ToolRiskLevel.Low);
        tool.InvokeAsync(
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Do<ToolContext>(context => observedContext = context),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ToolResult.Ok("{}")));
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(),
            "publisher-agent",
            "content",
            "Publisher",
            "publisher",
            "Queue publishing",
            "{}",
            true,
            null,
            "[\"content.publish\"]");
        var worker = new GenericLlmAgentWorker(
            definition,
            Substitute.For<IRagRetriever>(),
            CreateChatClient(
                Reply("{\"tool\":\"content.publish\",\"args\":{}}"),
                Reply("Đã xếp hàng đăng bài.")),
            new OrchestratorCostGuard(Substitute.For<ILlmCostTracker>()),
            Substitute.For<ILlmCallScope>(),
            new ToolRegistry([tool]),
            executionPermissions: new HashSet<string>(StringComparer.Ordinal)
            {
                "content:publish",
            });

        var result = await worker.ExecuteAsync(new AgentTask(
            "task-publish",
            "publisher-agent",
            "Queue publishing",
            new Dictionary<string, string> { ["tenant_id"] = Guid.NewGuid().ToString("D") }),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        observedContext.Should().NotBeNull();
        observedContext!.CanPublishContent.Should().BeTrue();
    }

    private static IClaudeChatClient CreateChatClient(params ClaudeReply[] replies)
    {
        var chatClient = Substitute.For<IClaudeChatClient>();
        var queue = new Queue<ClaudeReply>(replies);
        chatClient.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Dequeue()));
        return chatClient;
    }

    private static GenericLlmAgentWorker CreateWorker(IClaudeChatClient chatClient, IAgentTool tool)
    {
        var tracker = Substitute.For<ILlmCostTracker>();
        var registry = new ToolRegistry([tool]);
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(),
            "research-agent",
            "research",
            "Research",
            "research",
            "Quét trend",
            "{}",
            true,
            null,
            "[\"research-agent\"]");

        return new GenericLlmAgentWorker(
            definition,
            Substitute.For<IRagRetriever>(),
            chatClient,
            new OrchestratorCostGuard(tracker),
            Substitute.For<ILlmCallScope>(),
            registry);
    }

    // Nhiều result = trả về lần lượt theo thứ tự gọi, để dựng chuỗi thành công/thất bại xen kẽ.
    private static IAgentTool CreateTool(params ToolResult[] results)
    {
        var tool = Substitute.For<IAgentTool>();
        tool.Name.Returns("research-agent");
        tool.Description.Returns("Quét trend");
        tool.InputSchemaJson.Returns("{}");
        tool.RequiredPermission.Returns(string.Empty);
        tool.RiskLevel.Returns(ToolRiskLevel.Low);
        var queue = new Queue<ToolResult>(results);
        tool.InvokeAsync(
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<ToolContext>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(queue.Count > 1 ? queue.Dequeue() : queue.Peek()));
        return tool;
    }

    [Fact]
    public async Task ResearchAgent_FallsBackToKeywordSearch_WhenDefaultTrendsEmpty()
    {
        // Arrange
        var defaultSource = Substitute.For<ITrendSource>();
        defaultSource.Source.Returns("google_trends");
        defaultSource.Enabled.Returns(true);
        defaultSource.FetchAsync(Arg.Any<string>(), Arg.Any<TrendSourceOverride?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RawTrend>>([]));

        var keywordSource = Substitute.For<IKeywordTrendSource>();
        keywordSource.Source.Returns("searxng");
        keywordSource.Enabled.Returns(true);
        keywordSource.FetchAsync(Arg.Any<string>(), Arg.Any<TrendSourceOverride?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RawTrend>>([]));
        keywordSource.FetchByKeywordsAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<TrendSourceOverride?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RawTrend>>([
                new RawTrend("Lịch thi HSK 2025 tại Việt Nam", "searxng", "news", 5.0, ["Học HSK 2025"]),
            ]));

        var scorer = new WeightedTrendScorer();
        var agent = new ResearchAgent([defaultSource, keywordSource], scorer);

        // Act
        var result = await agent.ScanWithRawAsync(new ResearchScanRequest(
            Guid.NewGuid(),
            "VN",
            ["hsk", "tiếng trung"]), CancellationToken.None);

        // Assert
        result.Trends.Should().NotBeEmpty();
        result.Trends[0].Topic.Should().Be("Lịch thi HSK 2025 tại Việt Nam");
        result.Trends[0].RelevanceScore.Should().BeGreaterThan(0d);
    }

    [Fact]
    public void AgentPromptPacks_IncludesUpstreamContentBriefingInstruction()
    {
        var contentPrompt = AgentPromptPacks.For("content-agent");
        var researchPrompt = AgentPromptPacks.For("research-agent");

        contentPrompt.Should().Contain("upstream_results");
        contentPrompt.Should().Contain("brief");
        researchPrompt.Should().Contain("5 CHỦ ĐỀ NỔI BẬT");
    }

    private static AgentTask BuildTask(string description) => new(
        "task-3",
        "research-agent",
        description,
        new Dictionary<string, string> { ["tenant_id"] = Guid.NewGuid().ToString("D") });

    private static ClaudeReply Reply(string text) => new(text, 0, 0, 0m);

    private static JsonElement Parse(string output)
    {
        using var document = JsonDocument.Parse(output);
        return document.RootElement.Clone();
    }
}
