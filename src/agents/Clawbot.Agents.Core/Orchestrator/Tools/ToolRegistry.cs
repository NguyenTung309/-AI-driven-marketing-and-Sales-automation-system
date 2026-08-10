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
// InputSchemaJson is rendered verbatim into the ReAct system prompt (GenericLlmAgentWorker.BuildReActSystemPrompt),
// so an empty "{}" left the model guessing arg names and enums — each wrong guess burned one of the 5 tool steps
// (e.g. report-agent looping on "platform required." then "metric is not supported."). Schemas below are the
// contract the adapters actually enforce via AgentTaskInput; keep them in sync when an adapter's args change.
internal sealed class AdapterTool(IAgent agent, string description, string permission, ToolRiskLevel riskLevel) : IAgentTool
{
    public string Name => agent.Name;
    public string Description { get; } = description;
    public string InputSchemaJson { get; } = ToolRegistryFactory.InputSchemaFor(agent.Name);
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
    // (RbacSeeder) and are enforced against the initiating user. research-agent performs an external market scan with no
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
            // list/find_cold reads CRM; score/create/batch_score write. Use operation=list when goal needs lead IDs.
            ["lead-agent"] = ("List/query leads (operation=list|find_cold, stage, topN), score one lead (lead_id), create from contact, or batch_score. Returns lead_ids + items.", "leads:write", ToolRiskLevel.Low),
            // Chỉ có 5 metric kinh doanh (không có KPI hạ tầng). Nói rõ trong description để planner không giao
            // việc kiểu "forecast uptime" — task đó chắc chắn cụt ở bước validate.
            // reportUrl phải nói rõ trong description: model không tự đoán được là kết quả có sẵn link,
            // và một câu trả lời không kèm link thì người đọc mất đường vào bảng/biểu đồ/Excel.
            ["report-agent"] = ("Pull a daily KPI snapshot, detect anomalies, or forecast a metric series. operation=snapshot|anomaly|forecast; metric is one of leads/dms/replies/conversions/avg_response_time_sec only (no infrastructure KPIs); platform defaults to all; date defaults to today. The result carries reportUrl — always quote that link in your final answer so the user can open the full table/chart and download Excel/PDF.", "analytics:read", ToolRiskLevel.Low),
            ["docs-agent"] = ("Render a document (pdf/docx) from a template and variables. Args: template_code, template_body, vars_json.", "docs:write", ToolRiskLevel.Low),
            ["sale-assist"] = ("Summarize a conversation, draft a reply, or suggest an upsell. REQUIRES conversation_id + turns_json. Args: operation=summarize|draft|upsell.", "sale-assist:use", ToolRiskLevel.Low),
            ["chat-agent"] = ("Reply to a customer chat message using RAG + conversation memory. Args: user_text, conversation_id?, history.", "conversations:write", ToolRiskLevel.High),
            ["research-agent"] = ("Quét trend thị trường theo geo + keywords (nguồn trend công khai). Không lọc theo ngày và không lấy tin mới nhất. Args: geo (mặc định VN), keywords (có thể bỏ trống).", "", ToolRiskLevel.Low),
            // SPEC-16 P4-3: explicit AgentService-layer tools (ContentTools.cs) declared here so the admin upsert
            // can validate their names + permissions. Build() skips them (no adapter with these names is registered).
            // Phase 4.9: content.review is canonical (queues durable agent review only — never publishes).
            // content.approve remains a non-publishing legacy alias during migration.
            // content.list là bước đầu bắt buộc của reviewer-agent: content.review cần content_id cụ thể và
            // trước đây không tool nào liệt kê được bài đang chờ, nên reviewer luôn kết luận "thiếu content_id".
            ["content.list"] = ("List this tenant's content items with their workflow state. Use it first to find content_id values. Args: workflow_state (e.g. awaiting_human_approval), platform, limit.", "content:read", ToolRiskLevel.Low),
            ["content.review"] = ("Record the reviewer verdict for a content revision (never grants publishing approval). REQUIRES content_id — call content.list first to get one. Args: content_id, decision=approve|reject, reason.", "content:write", ToolRiskLevel.Low),
            ["content.approve"] = ("Legacy alias for content.review — reviewer action only; never publishes.", "content:write", ToolRiskLevel.Low),
            ["content.schedule"] = ("Queue an approved content revision for publishing.", "content:publish", ToolRiskLevel.High),
            ["content.publish"] = ("Queue an existing approved schedule for the durable publisher.", "content:publish", ToolRiskLevel.High),
            // Read-only external search (SearXNG self-host), no tenant-data write -> no permission gate.
            ["web.search"] = ("Tìm web công khai qua SearXNG: tin mới, bài đăng, giá và đối thủ. Dùng tool này khi cần nội dung mới theo ngày. Args: query, max_results.", "", ToolRiskLevel.Low),
        };

    // JSON Schema per adapter tool, rendered into the ReAct prompt. Enums matter more than types here: the model
    // cannot discover a closed value set (report metrics, operations) from a free-form "string" and each wrong
    // guess costs a tool step. Adapters without an entry fall back to "{}" (args validated at call time).
    private static readonly Dictionary<string, string> InputSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["report-agent"] = """
            {"type":"object","properties":{"operation":{"type":"string","enum":["snapshot","anomaly","forecast"],"default":"snapshot","description":"snapshot = KPI theo ngày; anomaly = phát hiện bất thường; forecast = dự báo"},"metric":{"type":"string","enum":["leads","dms","replies","conversions","avg_response_time_sec"],"description":"Bắt buộc cho anomaly/forecast. Chỉ nhận đúng các giá trị này — không có metric hạ tầng (uptime, error_rate, throughput)"},"platform":{"type":"string","description":"facebook|zalo|tiktok|... Bỏ trống = 'all' (tổng hợp mọi nền tảng)"},"date":{"type":"string","pattern":"^\\d{4}-\\d{2}-\\d{2}$","description":"Chỉ dùng cho snapshot. Bỏ trống = hôm nay"},"lookback_days":{"type":"integer","description":"anomaly: số ngày lấy mẫu, mặc định 30"},"z_threshold":{"type":"number","description":"anomaly: ngưỡng z-score, mặc định 2.0"},"horizon_days":{"type":"integer","description":"forecast: số ngày dự báo, mặc định 7"}},"additionalProperties":false}
            """,
        ["content-agent"] = """
            {"type":"object","properties":{"platform":{"type":"string","enum":["facebook","instagram","zalo","website"],"description":"Chỉ 4 kênh này đăng được; KHÔNG có tiktok/youtube"},"brief":{"type":"string","description":"Yêu cầu nội dung: chủ đề, thông điệp, CTA"},"tone":{"type":"string","description":"Giọng văn, ví dụ friendly|professional"},"language":{"type":"string","description":"Mã ngôn ngữ, mặc định vi"}},"required":["platform","brief"],"additionalProperties":false}
            """,
        ["lead-agent"] = """
            {"type":"object","properties":{"operation":{"type":"string","enum":["list","find_cold","score","create","batch_score"],"description":"Dùng list khi cần lấy lead_ids trước"},"stage":{"type":"string","description":"list: lọc theo stage"},"top_n":{"type":"integer","description":"list/find_cold: số bản ghi, mặc định 20"},"lead_id":{"type":"string","description":"Bắt buộc cho score"},"contact_id":{"type":"string","description":"Bắt buộc cho create"},"lead_ids":{"type":"array","items":{"type":"string"},"description":"Bắt buộc cho batch_score"}},"required":["operation"],"additionalProperties":false}
            """,
        ["docs-agent"] = """
            {"type":"object","properties":{"template_code":{"type":"string","description":"Mã mẫu có sẵn; bỏ trống nếu truyền template_body"},"template_body":{"type":"string","description":"Nội dung mẫu dạng {{key}} khi không dùng template_code"},"vars_json":{"type":"string","description":"JSON object phẳng ánh xạ tên biến -> giá trị"},"contact_id":{"type":"string"}},"additionalProperties":false}
            """,
        ["sale-assist"] = """
            {"type":"object","properties":{"operation":{"type":"string","enum":["summarize","draft","upsell"]},"conversation_id":{"type":"string"},"turns_json":{"type":"string","description":"JSON array [{\"role\":\"customer|agent\",\"text\":\"...\"}] — bắt buộc, tool không tự đọc hội thoại"}},"required":["operation","conversation_id","turns_json"],"additionalProperties":false}
            """,
        ["chat-agent"] = """
            {"type":"object","properties":{"user_text":{"type":"string","description":"Tin nhắn của khách cần trả lời"},"conversation_id":{"type":"string"},"history":{"type":"string","description":"Lịch sử hội thoại rút gọn, có thể bỏ trống"}},"required":["user_text"],"additionalProperties":false}
            """,
        ["research-agent"] = """
            {"type":"object","properties":{"geo":{"type":"string","description":"Mã khu vực, mặc định VN"},"keywords":{"type":"array","items":{"type":"string"},"description":"Từ khóa trend, có thể bỏ trống để dùng mặc định"}},"additionalProperties":false}
            """,
    };

    internal static string InputSchemaFor(string name) =>
        InputSchemas.TryGetValue(name, out var schema) ? schema.Trim() : "{}";

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
