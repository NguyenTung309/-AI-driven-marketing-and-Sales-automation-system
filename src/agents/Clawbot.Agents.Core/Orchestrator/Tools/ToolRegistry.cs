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
    public string InputSchemaJson { get; } = InputSchemaFor(agent.Name);
    public string RequiredPermission { get; } = permission;
    public ToolRiskLevel RiskLevel { get; } = riskLevel;

    private static string InputSchemaFor(string name) =>
        string.Equals(name, "research-agent", StringComparison.OrdinalIgnoreCase)
            ? """{"type":"object","properties":{"geo":{"type":"string","description":"Mã khu vực, mặc định VN"},"keywords":{"type":"array","items":{"type":"string"},"description":"Từ khóa trend, có thể bỏ trống để dùng mặc định"}},"additionalProperties":false}"""
            : "{}";

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
            ["ads-agent"] = ("Apply ad campaign actions (budget/pause/resume) or build lookalike/remarketing audiences. Args: operation, platform, campaign_id/…", "ads:write", ToolRiskLevel.High),
            // list/find_cold reads CRM; score/create/batch_score write. Use operation=list when goal needs lead IDs.
            ["lead-agent"] = ("List/query leads (operation=list|find_cold, stage, topN), score one lead (lead_id), create from contact, or batch_score. Returns lead_ids + items.", "leads:write", ToolRiskLevel.Low),
            ["report-agent"] = ("Pull a daily KPI snapshot (date), detect anomalies, or forecast a metric series. Args: operation, platform, metric, date.", "analytics:read", ToolRiskLevel.Low),
            ["docs-agent"] = ("Render a document (pdf/docx) from a template and variables. Args: template_code, template_body, vars_json.", "docs:write", ToolRiskLevel.Low),
            ["sale-assist"] = ("Summarize a conversation, draft a reply, or suggest an upsell. REQUIRES conversation_id + turns_json. Args: operation=summarize|draft|upsell.", "sale-assist:use", ToolRiskLevel.Low),
            ["chat-agent"] = ("Reply to a customer chat message using RAG + conversation memory. Args: user_text, conversation_id?, history.", "conversations:write", ToolRiskLevel.High),
            ["research-agent"] = ("Quét trend thị trường theo geo + keywords (nguồn trend công khai). Không lọc theo ngày và không lấy tin mới nhất. Args: geo (mặc định VN), keywords (có thể bỏ trống).", "", ToolRiskLevel.Low),
            // SPEC-16 P4-3: explicit AgentService-layer tools (ContentTools.cs) declared here so the admin upsert
            // can validate their names + permissions. Build() skips them (no adapter with these names is registered).
            // Phase 4.9: content.review is canonical (queues durable agent review only — never publishes).
            // content.approve remains a non-publishing legacy alias during migration.
            ["content.review"] = ("Request or retry durable Agent review for a content revision (never grants publishing approval).", "content:write", ToolRiskLevel.Low),
            ["content.approve"] = ("Legacy alias for content.review — reviewer action only; never publishes.", "content:write", ToolRiskLevel.Low),
            ["content.schedule"] = ("Queue an approved content revision for publishing.", "content:publish", ToolRiskLevel.High),
            ["content.publish"] = ("Queue an existing approved schedule for the durable publisher.", "content:publish", ToolRiskLevel.High),
            // Read-only external search (SearXNG self-host), no tenant-data write -> no permission gate.
            ["web.search"] = ("Tìm web công khai qua SearXNG: tin mới, bài đăng, giá và đối thủ. Dùng tool này khi cần nội dung mới theo ngày. Args: query, max_results.", "", ToolRiskLevel.Low),
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

        // Phase 4.9: content.approve is a non-publishing legacy alias for content.review.
        if (byName.TryGetValue("content.review", out var reviewTool)
            && !byName.ContainsKey("content.approve"))
        {
            byName["content.approve"] = reviewTool;
        }

        return new ToolRegistry(byName.Values);
    }
}
