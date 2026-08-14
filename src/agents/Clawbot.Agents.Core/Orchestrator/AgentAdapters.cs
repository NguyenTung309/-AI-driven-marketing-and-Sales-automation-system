using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Docs;
using Clawbot.Agents.Core.Research;
using Clawbot.Agents.Core.SaleAssist;
using Clawbot.SharedKernel.Content;

namespace Clawbot.Agents.Core.Orchestrator;

public abstract class AgentAdapterBase(string name) : IAgent
{
    public string Name { get; } = name;

    public async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        try
        {
            var output = await ExecuteCoreAsync(task, ct).ConfigureAwait(false);
            return new AgentResult(task.Id, Success: true, Output: output, Error: null);
        }
        catch (Exception ex)
        {
            return new AgentResult(task.Id, Success: false, Output: string.Empty, Error: ex.Message);
        }
    }

    protected abstract Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct);

    protected static string Json<T>(T value) => JsonSerializer.Serialize(value, AgentJson.Options);
}

public sealed class ChatAgentAdapter(Chat.ChatAgent agent) : AgentAdapterBase("chat-agent")
{
    private readonly Chat.ChatAgent _agent = agent;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");
        var history = AgentTaskInput.StringList(input, "history")
            .Select((text, index) => new ChatTurn(index % 2 == 0 ? "user" : "assistant", text))
            .ToArray();
        var reply = await _agent.ReplyAsync(new ChatAgentRequest(
            tenantId,
            AgentTaskInput.OptionalGuid(input, "conversation_id"),
            AgentTaskInput.OptionalString(input, "kb_module_code"),
            AgentTaskInput.RequiredString(input, "user_text"),
            history,
            AgentTaskInput.OptionalString(input, "sender_handle"),
            AgentTaskInput.OptionalString(input, "source_platform"),
            AgentTaskInput.OptionalString(input, "matched_scenario_template")), ct).ConfigureAwait(false);
        return Json(reply);
    }
}

public sealed class ContentAgentAdapter(ContentAgent agent) : AgentAdapterBase("content-agent")
{
    private readonly ContentAgent _agent = agent;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        if (!ContentPlatformCatalog.TryNormalizeWritable(
                AgentTaskInput.OptionalString(input, "platform"),
                out var platform))
        {
            throw new ArgumentException("content.platform_unsupported");
        }

        var result = await _agent.GenerateAsync(new ContentGenerateRequest(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.OptionalGuid(input, "brief_id"),
            platform!,
            AgentTaskInput.RequiredString(input, "brief"),
            AgentTaskInput.OptionalString(input, "kb_module_code")), ct).ConfigureAwait(false);
        return Json(result);
    }
}

public sealed class ResearchAgentAdapter(IResearchAgent agent) : AgentAdapterBase("research-agent")
{
    private readonly IResearchAgent _agent = agent;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var geo = AgentTaskInput.OptionalString(input, "geo") ?? "VN";
        var trends = await _agent.ScanAsync(new ResearchScanRequest(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            geo,
            AgentTaskInput.StringList(input, "keywords")), ct).ConfigureAwait(false);

        return trends.Count > 0
            ? Json(new { trends, matched = trends.Count, geo })
            : Json(new
            {
                trends,
                matched = 0,
                geo,
                hint = "Không có chủ đề nào khớp keyword. Tool này chỉ quét trend theo keyword và không lọc theo ngày. "
                     + "Nếu cần nội dung mới theo ngày, hãy gọi web.search."
            });
    }
}

public sealed class DocsAgentAdapter(DocsAgent agent) : AgentAdapterBase("docs-agent")
{
    private readonly DocsAgent _agent = agent;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var result = await _agent.RenderAsync(new DocsRenderRequest(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.RequiredString(input, "template_code"),
            AgentTaskInput.OptionalString(input, "doc_type") ?? "pdf",
            AgentTaskInput.RequiredString(input, "template_body"),
            AgentTaskInput.StringMap(input, "vars_json"),
            DocBranding.For(AgentTaskInput.OptionalString(input, "tenant_name") ?? "ClawBot")), ct).ConfigureAwait(false);
        return Json(new { result.Sha256, result.SizeBytes, result.LatencyMs });
    }
}

public sealed class SaleAssistAgentAdapter(SaleAssistAgent agent) : AgentAdapterBase("sale-assist")
{
    private readonly SaleAssistAgent _agent = agent;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = AgentTaskInput.OptionalString(input, "operation") ?? "summarize";
        var ctx = new ConversationContext(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.RequiredGuid(input, "conversation_id"),
            AgentTaskInput.OptionalString(input, "contact_name"),
            AgentTaskInput.OptionalString(input, "platform") ?? "unknown",
            AgentTaskInput.Turns(input, "turns_json"));

        if (string.Equals(operation, "draft", StringComparison.OrdinalIgnoreCase))
            return Json(await _agent.DraftAsync(ctx, ct).ConfigureAwait(false));
        if (string.Equals(operation, "upsell", StringComparison.OrdinalIgnoreCase))
            return Json(await _agent.SuggestUpsellAsync(ctx, ct).ConfigureAwait(false));
        if (string.Equals(operation, "auto_summary", StringComparison.OrdinalIgnoreCase))
            return Json(await _agent.AutoSummaryAsync(ctx, ct).ConfigureAwait(false));

        return Json(await _agent.SummarizeAsync(ctx, ct).ConfigureAwait(false));
    }
}

// lead-agent and report-agent orchestration adapters live in the AgentService layer
// (LeadOrchestrationAdapter / ReportOrchestrationAdapter) because their logic needs
// AppDbContext + skills via LeadAgentRunner / ReportAgentRunner.
