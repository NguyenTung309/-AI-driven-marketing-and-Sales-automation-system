using System.Diagnostics;
using Clawbot.Agents.Core.Chat;
using Microsoft.Extensions.Options;

namespace Clawbot.Agents.Core.Content.Chain;

public interface IContentChain
{
    Task<ContentChainOutcome> RunAsync(ContentChainContext context, CancellationToken ct = default);

    // Chạy lại chỉ L3+L4 với Plan+Outline đã lưu (repurpose/đổi hook, §4.5, P4). context phải mang sẵn Plan+Outline.
    Task<ContentChainOutcome> ResumeFromWriteAsync(ContentChainContext context, CancellationToken ct = default);
}

// Điều phối tuần tự các mắt xích. Mỗi step: 1 lần gọi LLM + cổng kiểm; lỗi cổng => repair đúng 1 lần;
// vẫn lỗi / timeout / LLM down => fallback (Succeeded=false) để ContentAgent chạy single-shot (§7).
// Cộng dồn token/chi phí để ghi ledger dưới agentCode content-agent (§6).
public sealed class ContentChain(
    IEnumerable<IContentChainStep> steps,
    IClaudeChatClient claude,
    IOptions<ContentChainOptions> options) : IContentChain
{
    private const int MaxAttemptsPerStep = 2; // 1 lần thường + 1 lần repair

    private readonly IReadOnlyList<IContentChainStep> _steps = steps.OrderBy(s => s.Order).ToArray();
    private readonly IClaudeChatClient _claude = claude;
    private readonly ContentChainOptions _options = options.Value;

    // Order tối thiểu của mắt xích "write" — mốc để repurpose/đổi hook chạy lại chỉ L3+L4 (§4.5, P4).
    private const int WriteOrder = 3;

    public Task<ContentChainOutcome> RunAsync(ContentChainContext context, CancellationToken ct = default) =>
        RunStepsAsync(context, minOrder: 1, ct);

    // Repurpose/đổi hook: bỏ L1/L2, chạy lại từ L3 với Plan+Outline đã lưu trong context (§4.5).
    // Caller phải bơm sẵn context.Plan + context.Outline (đã kèm SelectedHookIndex) từ L1/L2 lưu trước.
    public Task<ContentChainOutcome> ResumeFromWriteAsync(ContentChainContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Plan is null || context.Outline is null)
            throw new ArgumentException("resume_requires_plan_and_outline", nameof(context));
        return RunStepsAsync(context, minOrder: WriteOrder, ct);
    }

    private async Task<ContentChainOutcome> RunStepsAsync(
        ContentChainContext context, int minOrder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);

        var traces = new List<ContentChainStepTrace>();
        var totals = new RunningTotals();
        var current = context;

        using var chainCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        chainCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.ChainTimeoutSeconds)));

        foreach (var step in _steps)
        {
            if (step.Order < minOrder)
                continue;

            var advanced = false;
            var lastError = ContentChainErrorCodes.StepError;

            for (var attempt = 0; attempt < MaxAttemptsPerStep && !advanced; attempt++)
            {
                var prompt = attempt == 0
                    ? step.BuildPrompt(current)
                    : step.BuildRepairPrompt(current, lastError);

                var stopwatch = Stopwatch.StartNew();
                ClaudeReply reply;
                try
                {
                    reply = await CompleteStepAsync(prompt, chainCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // caller hủy — không nuốt
                }
                catch (OperationCanceledException)
                {
                    stopwatch.Stop();
                    traces.Add(TimingOnlyTrace(step.StepId, totals.Model, stopwatch, ContentChainErrorCodes.StepTimeout));
                    return Fallback(step.StepId, ContentChainErrorCodes.StepTimeout, traces, totals);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stopwatch.Stop();
                    traces.Add(TimingOnlyTrace(step.StepId, totals.Model, stopwatch, ContentChainErrorCodes.StepError));
                    return Fallback(step.StepId, ContentChainErrorCodes.StepError, traces, totals);
                }

                stopwatch.Stop();
                totals.Add(reply);

                var gate = step.ApplyGate(current, reply.Text);
                traces.Add(new ContentChainStepTrace(
                    step.StepId,
                    _options.Version,
                    reply.Model,
                    reply.InputTokens,
                    reply.OutputTokens,
                    reply.UsdCost,
                    stopwatch.ElapsedMilliseconds,
                    gate.Succeeded ? ContentChainErrorCodes.GatePassed : gate.ErrorCode,
                    gate.PayloadJson));

                if (gate.Succeeded)
                {
                    current = gate.Context;
                    advanced = true;
                }
                else
                {
                    lastError = gate.ErrorCode;
                }
            }

            if (!advanced)
                return Fallback(step.StepId, lastError, traces, totals);
        }

        var body = current.Body ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
            return Fallback("chain", ContentChainErrorCodes.WriteEmptyOutput, traces, totals);

        return new ContentChainOutcome(
            Succeeded: true,
            Body: body,
            FallbackReason: null,
            Traces: traces,
            InputTokens: totals.InputTokens,
            OutputTokens: totals.OutputTokens,
            UsdCost: totals.UsdCost,
            Model: totals.Model,
            Plan: current.Plan,       // L1 để lưu lại phục vụ repurpose/đổi hook (P4/P5)
            Outline: current.Outline, // L2 (đã kèm SelectedHookIndex)
            IsEstimated: totals.IsEstimated);
    }

    private async Task<ClaudeReply> CompleteStepAsync(ChainStepPrompt prompt, CancellationToken chainToken)
    {
        using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(chainToken);
        stepCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.StepTimeoutSeconds)));
        return await _claude.CompleteAsync(prompt.System, Array.Empty<ChatTurn>(), prompt.User, stepCts.Token)
            .ConfigureAwait(false);
    }

    private ContentChainStepTrace TimingOnlyTrace(string stepId, string model, Stopwatch stopwatch, string gateResult) =>
        new(stepId, _options.Version, model, 0, 0, 0m, stopwatch.ElapsedMilliseconds, gateResult, null);

    private static ContentChainOutcome Fallback(
        string stepId, string reason, IReadOnlyList<ContentChainStepTrace> traces, RunningTotals totals) =>
        new(
            Succeeded: false,
            Body: string.Empty,
            FallbackReason: $"{stepId}:{reason}",
            Traces: traces,
            InputTokens: totals.InputTokens,
            OutputTokens: totals.OutputTokens,
            UsdCost: totals.UsdCost,
            Model: totals.Model,
            IsEstimated: totals.IsEstimated);

    private sealed class RunningTotals
    {
        public int InputTokens { get; private set; }
        public int OutputTokens { get; private set; }
        public decimal UsdCost { get; private set; }
        public string Model { get; private set; } = string.Empty;

        // Cả chuỗi coi là ước lượng nếu bất kỳ bước nào phải ước lượng — cộng dồn số ước lượng
        // với số thật cho ra tổng không còn chính xác, phải gắn nhãn.
        public bool IsEstimated { get; private set; }

        public void Add(ClaudeReply reply)
        {
            InputTokens += reply.InputTokens;
            OutputTokens += reply.OutputTokens;
            UsdCost += reply.UsdCost;
            IsEstimated |= reply.IsEstimated;
            if (!string.IsNullOrEmpty(reply.Model))
                Model = reply.Model;
        }
    }
}
