using Clawbot.Agents.Core.Chat;

namespace Clawbot.Agents.Core.Orchestrator;

// Resolves tools by name and filters to an agent's allow-list. Scoped because it wraps the scoped IAgent adapters.
public sealed class ToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _byName;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _byName = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
            _byName[tool.Name] = tool;
    }

    public IReadOnlyList<IAgentTool> All => _byName.Values.ToArray();

    public IAgentTool? Resolve(string name) =>
        _byName.TryGetValue(name, out var tool) ? tool : null;

    // EARS[WHEN a data-defined agent declares an allowed-tools list THE SYSTEM SHALL expose only those tools
    // to its ReAct loop, dropping unknown names so a stale allow-list never broadens capability]
    public IReadOnlyList<IAgentTool> AllowedFor(IReadOnlyList<string> allowedToolNames)
    {
        if (allowedToolNames.Count == 0)
            return [];
        var result = new List<IAgentTool>();
        foreach (var name in allowedToolNames)
        {
            if (_byName.TryGetValue(name, out var tool))
                result.Add(tool);
        }
        return result;
    }
}

// Wraps an existing IAgent adapter as a tool: forwards args (+ tenant_id from context) and maps AgentResult -> ToolResult.
// ponytail: InputSchemaJson is "{}" for now; adapters validate required keys via AgentTaskInput and return a clear
// ArgumentException, which surfaces to the ReAct loop as a tool error. Schemas get refined when the ReAct prompt
// needs stricter arg guidance.
internal sealed class AdapterTool(IAgent agent, string description, string permission, ToolRiskLevel riskLevel) : IAgentTool
{
    public string Name => agent.Name;
    public string Description { get; } = description;
    public string InputSchemaJson { get; } = "{}";
    public string RequiredPermission { get; } = permission;
    public ToolRiskLevel RiskLevel { get; } = riskLevel;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        // EARS[WHEN dry-run is on THE SYSTEM SHALL return the intended action as a preview without executing it]
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would call {Name} with args {System.Text.Json.JsonSerializer.Serialize(args)}");

        var input = new Dictionary<string, string>(args, StringComparer.OrdinalIgnoreCase)
        {
            ["tenant_id"] = ctx.TenantId.ToString("D")
        };
        var task = new AgentTask(ctx.TaskId, agent.Name, Description, input);
        var result = await agent.ExecuteAsync(task, ct).ConfigureAwait(false);
        return result.Success
            ? ToolResult.Ok(result.Output)
            : ToolResult.Fail(result.Error ?? result.Output);
    }
}

public static class ToolRegistryFactory
{
    // adapter name -> (description, required permission, risk level). Permissions reuse seeded role_permissions
    // (RbacSeeder); enforcement is Phase 4 (P4-3). research-agent performs an external market scan with no
    // tenant-data write, so it carries no permission (empty = no gate).
    // SPEC-16 P4-4: High-risk = irreversible / outward-facing (ad spend, customer messages). content publish is
    // high-risk (via ContentPublishTool, not this adapter); the content-agent adapter here only generates drafts (Low).
    // SPEC-16 P4-3: KnownTools exposes the catalog so the admin upsert (Api) can validate an agent's allowedTools
    // list against known tool names + the admin's permission to grant each tool's RequiredPermission.
    public static IReadOnlyDictionary<string, (string Description, string Permission, ToolRiskLevel Risk)> KnownTools => Metadata;

    private static readonly Dictionary<string, (string Description, string Permission, ToolRiskLevel Risk)> Metadata =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["content-agent"] = ("Generate content (posts/captions/copy) for a platform from a brief.", "content:write", ToolRiskLevel.Low),
            ["ads-agent"] = ("Apply ad campaign actions (budget/pause/resume) or build lookalike/remarketing audiences.", "ads:write", ToolRiskLevel.High),
            ["lead-agent"] = ("Score an existing lead or create a new lead from a contact.", "leads:write", ToolRiskLevel.Low),
            ["report-agent"] = ("Pull a daily KPI snapshot, detect anomalies, or forecast a metric series.", "analytics:read", ToolRiskLevel.Low),
            ["docs-agent"] = ("Render a document (pdf/docx) from a template and variables.", "docs:write", ToolRiskLevel.Low),
            ["sale-assist"] = ("Summarize a conversation, draft a reply, or suggest an upsell for a sales rep.", "sale-assist:use", ToolRiskLevel.Low),
            ["chat-agent"] = ("Reply to a customer chat message using RAG + conversation memory.", "conversations:write", ToolRiskLevel.High),
            ["research-agent"] = ("Scan market research by geo and keywords.", "", ToolRiskLevel.Low),
            // SPEC-16 P4-3: explicit AgentService-layer tools (ContentTools.cs) declared here so the admin upsert
            // can validate their names + permissions. Build() skips them (no adapter with these names is registered).
            ["content.approve"] = ("Approve/reject a draft content item (reviewer action).", "content:write", ToolRiskLevel.Low),
            ["content.schedule"] = ("Schedule an approved content item for publishing.", "content:write", ToolRiskLevel.Low),
            ["content.publish"] = ("Publish an approved content item via the social publisher.", "content:write", ToolRiskLevel.High),
        };

    // Builds the registry from adapter-wrapped tools PLUS any explicit IAgentTool registrations (AgentService-layer
    // tools that need AppDbContext, e.g. content persist/approve). Explicit tools win on name collision so a
    // persisting content tool overrides the text-only adapter-wrapped one of the same name.
    public static ToolRegistry Build(IEnumerable<IAgent> adapters, IEnumerable<IAgentTool>? explicitTools = null)
    {
        var byName = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in adapters)
        {
            // ponytail: skip adapters without declared tool metadata rather than guessing a permission.
            if (!Metadata.TryGetValue(adapter.Name, out var meta))
                continue;
            byName[adapter.Name] = new AdapterTool(adapter, meta.Description, meta.Permission, meta.Risk);
        }
        if (explicitTools is not null)
        {
            foreach (var tool in explicitTools)
                byName[tool.Name] = tool; // explicit overrides adapter-wrapped on name collision
        }
        return new ToolRegistry(byName.Values);
    }
}
