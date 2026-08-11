namespace Clawbot.Agents.Core.Orchestrator;

// Risk classification for the autonomy approval gate (SPEC-16 P4-4). High-risk tools have irreversible or
// outward-facing side effects (publishing, ad spend, customer messages) and pause for human approval when the
// tenant toggle is on; Low-risk tools (generate, score, report, schedule) auto-execute.
public enum ToolRiskLevel
{
    Low = 0,
    High = 1,
}

// Ambient context for a tool invocation: the tenant the run belongs to, the orchestrator task id the tool is
// servicing, and the calling data-defined agent's identity (for audit attribution). Adapter-wrapped tools (no
// definition) leave the agent identity null; AgentService-layer tools (content persist/approve) use it to attribute
// state changes to the agent actor rather than a human user.
// RequireHighRiskApproval: when true (Tenant.RequireOrchestrationApproval), the worker refuses High-risk tools.
// DryRun: when true (P4-2), the tool returns its intended action/args as a preview without executing side effects.
public sealed record ToolContext(
    Guid TenantId,
    string TaskId,
    Guid? AgentDefinitionId = null,
    string? AgentCode = null,
    bool RequireHighRiskApproval = false,
    bool DryRun = false,
    bool CanPublishContent = false,
    Guid? SessionId = null,
    int? OrchestrationPlanGeneration = null);

public sealed record ToolResult(bool Success, string Output, string? Error)
{
    public static ToolResult Ok(string output) => new(true, output, null);
    public static ToolResult Fail(string error) => new(false, string.Empty, error);
}

// A capability a data-defined agent may invoke inside its ReAct loop. Wraps the existing agent adapters so the
// orchestrator's "hands" (content/ads/lead/report/...) become callable tools without re-implementing them.
// RequiredPermission is enforced by the worker against the initiating user's current role permissions.
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    string InputSchemaJson { get; }
    string RequiredPermission { get; }
    ToolRiskLevel RiskLevel { get; }
    Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct);
}
