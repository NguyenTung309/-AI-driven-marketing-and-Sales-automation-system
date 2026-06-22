using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Agents;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class SemanticKernelOrchestratorTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset At = new(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

    private const string PlanJson = """
    {
      "version": 1,
      "tasks": [
        { "id": "t1", "agent": "content", "description": "contact 0912345678", "input": { "brief": "HSK4", "phone": "0912345678" }, "dependsOn": [], "status": "pending" }
      ]
    }
    """;

    [Fact]
    public async Task PlanAsync_auto_run_returns_running_session_with_redacted_goal()
    {
        var orchestrator = BuildOrchestrator(monthToDate: 10m);

        var result = await orchestrator.PlanAsync(
            TenantId, "call 0912345678 about HSK4", requireApproval: false, At, CancellationToken.None);

        result.CostBlocked.Should().BeFalse();
        result.Session.Status.Should().Be(AgentSessionStatuses.Running);
        result.Session.Goal.Should().Be("call [PHONE] about HSK4");
        result.Plan.Tasks.Should().ContainSingle().Which.Description.Should().Be("contact [PHONE]");
        result.Session.PlanJson.Should().Contain("[PHONE]").And.NotContain("0912345678");
    }

    [Fact]
    public async Task PlanAsync_require_approval_returns_pending_session()
    {
        var orchestrator = BuildOrchestrator(monthToDate: 10m);

        var result = await orchestrator.PlanAsync(
            TenantId, "launch HSK4", requireApproval: true, At, CancellationToken.None);

        result.CostBlocked.Should().BeFalse();
        result.Session.Status.Should().Be(AgentSessionStatuses.PendingApproval);
        result.Session.RequiresApproval.Should().BeTrue();
    }

    [Fact]
    public async Task PlanAsync_auto_run_blocked_by_cost_falls_back_to_pending_approval()
    {
        var orchestrator = BuildOrchestrator(monthToDate: 199.99m);

        var result = await orchestrator.PlanAsync(
            TenantId, "launch HSK4", requireApproval: false, At, CancellationToken.None);

        result.CostBlocked.Should().BeTrue();
        result.CostReason.Should().Be("cost_cap_preflight");
        result.Session.Status.Should().Be(AgentSessionStatuses.PendingApproval);
        result.Session.RequiresApproval.Should().BeTrue();
    }

    private static SemanticKernelOrchestrator BuildOrchestrator(decimal monthToDate)
    {
        var generator = new SemanticKernelPlanGenerator(
            new ClawbotChatCompletionService(new FixedChatClient(PlanJson)));
        var costGuard = new OrchestratorCostGuard(
            new FixedSummaryTracker(new CostSummary(TenantId, monthToDate, 200m, (float)(monthToDate / 200m))));
        return new SemanticKernelOrchestrator(
            new FixedCatalog(), generator, new RegexPiiRedactor(), costGuard);
    }

    private sealed class FixedCatalog : IAgentCatalog
    {
        private static readonly AgentCatalogEntry Content = new(
            "content-agent", "content", "Content", "content", "Run content", "{}", Orchestratable: true);

        public Task<IReadOnlyList<AgentCatalogEntry>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AgentCatalogEntry>>([Content]);

        public Task<AgentCatalogEntry> ResolveAsync(string name, CancellationToken ct = default) =>
            Task.FromResult(Content);
    }

    private sealed class FixedSummaryTracker(CostSummary summary) : IClaudeCostTracker
    {
        public string Name => "cost";

        public Task RecordAsync(CostEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct) =>
            Task.FromResult(summary);
    }

    private sealed class FixedChatClient(string response) : IClaudeChatClient
    {
        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default) =>
            Task.FromResult(new ClaudeReply(response, 1, 1, 0.01m, "test"));

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new ClaudeStreamChunk(response, Final: true, 1, 1, 0.01m, "test");
        }
    }
}
