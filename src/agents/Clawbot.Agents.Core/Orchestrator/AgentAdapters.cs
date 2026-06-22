using System.Text.Json;
using Clawbot.Agents.Core.Ads;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Docs;
using Clawbot.Agents.Core.Research;
using Clawbot.Agents.Core.SaleAssist;
using Clawbot.Domain.Ads;

namespace Clawbot.Agents.Core.Orchestrator;

public abstract class AgentAdapterBase(string name) : IAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

    protected static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
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
        var result = await _agent.GenerateAsync(new ContentGenerateRequest(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.OptionalGuid(input, "brief_id"),
            AgentTaskInput.RequiredString(input, "platform"),
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
        var result = await _agent.ScanAsync(new ResearchScanRequest(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.RequiredString(input, "geo"),
            AgentTaskInput.StringList(input, "keywords")), ct).ConfigureAwait(false);
        return Json(result);
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

public sealed class AdsAgentAdapter(AdsAgent agent) : AgentAdapterBase("ads-agent")
{
    private readonly AdsAgent _agent = agent;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = AgentTaskInput.OptionalString(input, "operation") ?? "apply";
        if (string.Equals(operation, "lookalike", StringComparison.OrdinalIgnoreCase))
        {
            var audienceId = await _agent.BuildLookalikeAsync(
                AgentTaskInput.RequiredString(input, "platform"),
                AgentTaskInput.StringList(input, "seed_contact_keys"), ct).ConfigureAwait(false);
            return Json(new { audienceId });
        }

        if (string.Equals(operation, "remarketing", StringComparison.OrdinalIgnoreCase))
        {
            var ok = await _agent.BuildRemarketingAsync(
                AgentTaskInput.RequiredString(input, "platform"),
                AgentTaskInput.RequiredString(input, "audience_name"),
                AgentTaskInput.StringList(input, "contact_keys"), ct).ConfigureAwait(false);
            return Json(new { ok });
        }

        var applied = await _agent.ApplyActionAsync(
            AgentTaskInput.RequiredString(input, "platform"),
            AgentTaskInput.RequiredString(input, "campaign_id"),
            AgentTaskInput.RequiredString(input, "action"),
            AgentTaskInput.OptionalDecimal(input, "new_budget"), ct).ConfigureAwait(false);
        return Json(new { applied });
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
