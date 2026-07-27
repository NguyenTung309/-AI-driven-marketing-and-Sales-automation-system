using System.Diagnostics;
using System.Globalization;
using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content.Chain;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Ops;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Content;

public sealed record ContentGenerateRequest(
    Guid TenantId,
    Guid? BriefId,
    string Platform,
    string Brief,
    string? KbModuleCode);

// Repurpose tái dùng L1/L2 (P4, §4.5): PlanJson/OutlineJson là ảnh chụp đã lưu ở item gốc; Platform là nền tảng đích.
public sealed record ContentRepurposeFromChainRequest(
    Guid TenantId,
    Guid? BriefId,
    string Platform,
    string? PlanJson,
    string? OutlineJson);

// Đổi hook (P5, §4.5): tái dùng L1/L2 của CHÍNH item, chạy lại L3+L4 với hook marketer chọn (HookIndex ghi đè
// SelectedHookIndex đã chọn tự động). Cùng nền tảng bài gốc. HookIndex ngoài [0, số hook) => coi như hỏng, trả null.
public sealed record ContentRegenerateHookRequest(
    Guid TenantId,
    Guid? BriefId,
    string Platform,
    string? PlanJson,
    string? OutlineJson,
    int HookIndex);

// Refine (P6, §4.7): reviewer reject => tái dùng L1/L2 của CHÍNH item, chạy lại L3+L4 kèm góp ý reviewer bơm vào L3.
// Cùng nền tảng bài gốc; giữ nguyên hook đã chọn (không đổi hook, chỉ sửa bài theo góp ý). null khi tắt/hỏng/fallback.
public sealed record ContentRefineFromChainRequest(
    Guid TenantId,
    Guid? BriefId,
    string Platform,
    string? PlanJson,
    string? OutlineJson,
    string RejectionReason);

public sealed record ContentDraftResult(
    Guid? BriefId,
    string Platform,
    string Body,
    IReadOnlyList<RagChunk> Citations,
    int InputTokens,
    int OutputTokens,
    decimal UsdCost,
    long LatencyMs,
    // Ảnh chụp L1/L2 JSON — CHỈ có khi chuỗi chạy đủ thành công (P4, §4.5); null với single-shot/fallback.
    // gRPC service lưu vào content_items để repurpose/đổi hook tái dùng, khỏi chạy lại L1/L2.
    string? ChainPlanJson = null,
    string? ChainOutlineJson = null);

public sealed class ContentAgent(
    IRagRetriever rag,
    IPromptTemplateProvider templates,
    IClaudeChatClient claude,
    ILlmCallScope llmScope,
    ILlmCostTracker? costTracker = null,
    IContentChain? chain = null,
    IOptions<ContentChainOptions>? chainOptions = null,
    IContentChainTraceSink? traceSink = null)
{
    private const string AgentCode = "content-agent";

    private readonly IRagRetriever _rag = rag;
    private readonly IPromptTemplateProvider _templates = templates;
    private readonly IClaudeChatClient _claude = claude;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly ILlmCostTracker? _costTracker = costTracker;
    private readonly IContentChain? _chain = chain;
    private readonly ContentChainOptions? _chainOptions = chainOptions?.Value;
    private readonly IContentChainTraceSink? _traceSink = traceSink;

    public async Task<ContentDraftResult> GenerateAsync(ContentGenerateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Brief))
            throw new ArgumentException("brief required", nameof(request));

        // Resolve this agent's bound provider config (D8) — same per-tenant path as chat, no env drift.
        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var template = _templates.GetTemplate(request.Platform);
        var chunks = await _rag.RetrieveAsync(
            new RagRequest(request.TenantId, request.KbModuleCode, request.Brief, TopK: 4),
            ct).ConfigureAwait(false);
        var knowledge = BuildKnowledgeContext(chunks);

        // Prompt chaining (P1): bật theo cờ + allow-list. Lỗi/timeout ở chuỗi => rơi xuống single-shot (§7).
        if (_chain is not null && _chainOptions is not null && _chainOptions.IsEnabledFor(request.TenantId))
        {
            var chained = await RunChainAsync(request, template, knowledge, chunks, _chainOptions, ct)
                .ConfigureAwait(false);
            if (chained is not null)
                return chained;
        }

        return await RunSingleShotAsync(request, template, knowledge, chunks, ct).ConfigureAwait(false);
    }

    // Repurpose (§4.5, P4): tái dùng L1/L2 đã lưu, chạy lại CHỈ L3+L4 cho nền tảng đích — khỏi gọi lại LLM/RAG cho
    // L1/L2. Trả null khi: chuỗi tắt, JSON L1/L2 hỏng, hoặc resume fallback => caller chạy full chuỗi từ body (như cũ).
    public async Task<ContentDraftResult?> RepurposeFromChainAsync(
        ContentRepurposeFromChainRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform required", nameof(request));

        if (_chain is null || _chainOptions is null || !_chainOptions.IsEnabledFor(request.TenantId))
            return null;

        var snapshot = ContentChainSnapshot.TryDeserialize(request.PlanJson, request.OutlineJson);
        if (snapshot is null)
            return null;

        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var template = _templates.GetTemplate(request.Platform);

        var sw = Stopwatch.StartNew();
        // KHÔNG gọi RAG: proofPoint đã qua G2 nằm sẵn trong outline lưu trước; giới hạn ký tự lấy theo nền tảng đích.
        var context = new ContentChainContext(
            request.TenantId,
            request.Platform,
            Brief: string.Empty,       // brief thô đã được chưng cất ở L1 lưu trước (§4.6)
            Knowledge: string.Empty,
            template,
            _chainOptions.LimitsFor(request.Platform),
            ChunkCount: 0,             // G2 không chạy lại; L3 chỉ dùng proofPoint đã gate trong outline
            Plan: snapshot.Plan,
            Outline: snapshot.Outline);

        var outcome = await _chain.ResumeFromWriteAsync(context, ct).ConfigureAwait(false);

        await RecordCostAsync(request.TenantId, outcome.Model, outcome.InputTokens, outcome.OutputTokens,
            outcome.UsdCost, outcome.IsEstimated, ct).ConfigureAwait(false);
        await WriteTracesAsync(
            new ContentGenerateRequest(request.TenantId, request.BriefId, request.Platform, string.Empty, null),
            outcome, ct).ConfigureAwait(false);

        if (!outcome.Succeeded)
            return null;

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            outcome.Body.Trim(),
            Array.Empty<RagChunk>(),
            outcome.InputTokens,
            outcome.OutputTokens,
            outcome.UsdCost,
            sw.ElapsedMilliseconds,
            // Nền tảng đích tái dùng đúng L1/L2 đó => lưu lại để repurpose tiếp/đổi hook vẫn tái dùng được.
            ChainPlanJson: ContentChainSnapshot.SerializePlan(outcome.Plan),
            ChainOutlineJson: ContentChainSnapshot.SerializeOutline(outcome.Outline));
    }

    // Đổi hook (§4.5, P5): tái dùng L1/L2 CỦA CHÍNH item, chạy lại L3+L4 với hook marketer chọn thay vì hook tự
    // động. Cùng nền tảng bài gốc. Trả null (=> caller giữ nguyên bài, báo lỗi) khi: chuỗi tắt, JSON hỏng,
    // hookIndex ngoài [0, số hook), hoặc resume fallback.
    public async Task<ContentDraftResult?> RegenerateHookAsync(
        ContentRegenerateHookRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform required", nameof(request));

        if (_chain is null || _chainOptions is null || !_chainOptions.IsEnabledFor(request.TenantId))
            return null;

        var snapshot = ContentChainSnapshot.TryDeserialize(request.PlanJson, request.OutlineJson);
        if (snapshot is null)
            return null;
        if (request.HookIndex < 0 || request.HookIndex >= snapshot.Outline.Hooks.Count)
            return null;

        // Ghi đè hook đã chọn tự động bằng hook marketer chọn — L3 dùng hook này làm câu mở bài.
        var outline = snapshot.Outline with { SelectedHookIndex = request.HookIndex };

        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var template = _templates.GetTemplate(request.Platform);

        var sw = Stopwatch.StartNew();
        var context = new ContentChainContext(
            request.TenantId,
            request.Platform,
            Brief: string.Empty,
            Knowledge: string.Empty,
            template,
            _chainOptions.LimitsFor(request.Platform),
            ChunkCount: 0,
            Plan: snapshot.Plan,
            Outline: outline);

        var outcome = await _chain.ResumeFromWriteAsync(context, ct).ConfigureAwait(false);

        await RecordCostAsync(request.TenantId, outcome.Model, outcome.InputTokens, outcome.OutputTokens,
            outcome.UsdCost, outcome.IsEstimated, ct).ConfigureAwait(false);
        await WriteTracesAsync(
            new ContentGenerateRequest(request.TenantId, request.BriefId, request.Platform, string.Empty, null),
            outcome, ct).ConfigureAwait(false);

        if (!outcome.Succeeded)
            return null;

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            outcome.Body.Trim(),
            Array.Empty<RagChunk>(),
            outcome.InputTokens,
            outcome.OutputTokens,
            outcome.UsdCost,
            sw.ElapsedMilliseconds,
            // Lưu lại L1/L2 (outline đã kèm hook mới) để lần đổi hook sau vẫn tái dùng được.
            ChainPlanJson: ContentChainSnapshot.SerializePlan(outcome.Plan),
            ChainOutlineJson: ContentChainSnapshot.SerializeOutline(outcome.Outline));
    }

    // Refine (§4.7, P6): reviewer reject kèm lý do => tái dùng L1/L2 CỦA CHÍNH item, chạy lại L3+L4 bơm lý do reject
    // vào L3 làm góp ý cần khắc phục. Giữ nguyên hook đã chọn (SelectedHookIndex trong outline lưu). Trả null (=>
    // caller giữ nguyên bài, về hàng chờ người) khi: chuỗi tắt, JSON L1/L2 hỏng, hoặc resume fallback.
    public async Task<ContentDraftResult?> RefineFromChainAsync(
        ContentRefineFromChainRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw new ArgumentException("platform required", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RejectionReason))
            throw new ArgumentException("rejection reason required", nameof(request));

        if (_chain is null || _chainOptions is null || !_chainOptions.IsEnabledFor(request.TenantId))
            return null;

        var snapshot = ContentChainSnapshot.TryDeserialize(request.PlanJson, request.OutlineJson);
        if (snapshot is null)
            return null;

        using var _llm = _llmScope.Begin(request.TenantId, AgentCode);
        var template = _templates.GetTemplate(request.Platform);

        var sw = Stopwatch.StartNew();
        var context = new ContentChainContext(
            request.TenantId,
            request.Platform,
            Brief: string.Empty,
            Knowledge: string.Empty,
            template,
            _chainOptions.LimitsFor(request.Platform),
            ChunkCount: 0,
            Plan: snapshot.Plan,
            Outline: snapshot.Outline,
            Body: null,
            // Lý do reviewer reject — WriteStep bơm vào L3 làm góp ý cần khắc phục (không phải chỉ dẫn hệ thống).
            RefineFeedback: request.RejectionReason);

        var outcome = await _chain.ResumeFromWriteAsync(context, ct).ConfigureAwait(false);

        await RecordCostAsync(request.TenantId, outcome.Model, outcome.InputTokens, outcome.OutputTokens,
            outcome.UsdCost, outcome.IsEstimated, ct).ConfigureAwait(false);
        await WriteTracesAsync(
            new ContentGenerateRequest(request.TenantId, request.BriefId, request.Platform, string.Empty, null),
            outcome, ct).ConfigureAwait(false);

        if (!outcome.Succeeded)
            return null;

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            outcome.Body.Trim(),
            Array.Empty<RagChunk>(),
            outcome.InputTokens,
            outcome.OutputTokens,
            outcome.UsdCost,
            sw.ElapsedMilliseconds,
            // Giữ nguyên L1/L2 (không đổi khi chỉ sửa L3+L4) để lần refine/repurpose sau vẫn tái dùng.
            ChainPlanJson: ContentChainSnapshot.SerializePlan(outcome.Plan),
            ChainOutlineJson: ContentChainSnapshot.SerializeOutline(outcome.Outline));
    }

    // Đường cũ — một lần gọi LLM. Template mang toàn bộ chỉ dẫn, gửi làm user message (system rỗng).
    private async Task<ContentDraftResult> RunSingleShotAsync(
        ContentGenerateRequest request,
        string template,
        string knowledge,
        IReadOnlyList<RagChunk> chunks,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var prompt = RenderTemplate(template, request.Brief, knowledge);
        var reply = await _claude.CompleteAsync(string.Empty, Array.Empty<ChatTurn>(), prompt, ct).ConfigureAwait(false);
        await RecordCostAsync(request.TenantId, reply, ct).ConfigureAwait(false);

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            reply.Text.Trim(),
            chunks,
            reply.InputTokens,
            reply.OutputTokens,
            reply.UsdCost,
            sw.ElapsedMilliseconds);
    }

    // Đường chuỗi — trả draft khi thành công; null khi fallback để GenerateAsync chạy single-shot.
    private async Task<ContentDraftResult?> RunChainAsync(
        ContentGenerateRequest request,
        string template,
        string knowledge,
        IReadOnlyList<RagChunk> chunks,
        ContentChainOptions options,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var context = new ContentChainContext(
            request.TenantId,
            request.Platform,
            request.Brief,
            knowledge,
            template,
            options.LimitsFor(request.Platform),
            chunks.Count);   // tập citationId hợp lệ [1..k] cho cổng G2 (§4.2)

        var outcome = await _chain!.RunAsync(context, ct).ConfigureAwait(false);

        // Chi phí thực chuỗi đã tiêu (đầy đủ khi thành công, một phần khi fallback) — ghi ledger như content-agent.
        await RecordCostAsync(request.TenantId, outcome.Model, outcome.InputTokens, outcome.OutputTokens,
            outcome.UsdCost, outcome.IsEstimated, ct).ConfigureAwait(false);
        await WriteTracesAsync(request, outcome, ct).ConfigureAwait(false);

        if (!outcome.Succeeded)
            return null;

        sw.Stop();
        return new ContentDraftResult(
            request.BriefId,
            request.Platform,
            outcome.Body.Trim(),
            chunks,
            outcome.InputTokens,
            outcome.OutputTokens,
            outcome.UsdCost,
            sw.ElapsedMilliseconds,
            ContentChainSnapshot.SerializePlan(outcome.Plan),
            ContentChainSnapshot.SerializeOutline(outcome.Outline));
    }

    private async Task WriteTracesAsync(ContentGenerateRequest request, ContentChainOutcome outcome, CancellationToken ct)
    {
        if (_traceSink is null)
            return;
        try
        {
            await _traceSink.WriteAsync(request.TenantId, request.BriefId, outcome, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _ = ex; // trace là phụ trợ, không được làm hỏng job sinh bài
        }
    }

    private Task RecordCostAsync(Guid tenantId, ClaudeReply reply, CancellationToken ct) =>
        RecordCostAsync(
            tenantId, reply.Model, reply.InputTokens, reply.OutputTokens, reply.UsdCost, reply.IsEstimated, ct);

    private async Task RecordCostAsync(
        Guid tenantId,
        string model,
        int inputTokens,
        int outputTokens,
        decimal usdCost,
        bool isEstimated,
        CancellationToken ct)
    {
        if (_costTracker is null || (usdCost <= 0m && inputTokens <= 0 && outputTokens <= 0))
            return;

        await _costTracker.RecordAsync(new CostEntry(
            tenantId,
            AgentCode,
            model,
            inputTokens,
            outputTokens,
            usdCost,
            _llmScope.Current?.CostAt ?? DateTimeOffset.UtcNow,
            _llmScope.Current?.ReservationId,
            SessionId: null,
            IsEstimated: isEstimated), ct).ConfigureAwait(false);
    }

    private static string RenderTemplate(string template, string brief, string knowledge) =>
        template
            .Replace("{{brief}}", brief, StringComparison.Ordinal)
            .Replace("{{knowledge}}", knowledge, StringComparison.Ordinal);

    private static string BuildKnowledgeContext(IReadOnlyList<RagChunk> chunks)
    {
        if (chunks.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"[{i + 1}] (module={chunk.KbModuleCode}, score={chunk.Score:0.00}) {chunk.Snippet}");
        }

        return sb.ToString().TrimEnd();
    }
}
