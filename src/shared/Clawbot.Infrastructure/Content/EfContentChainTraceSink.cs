using Clawbot.Agents.Core.Content.Chain;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Content;

// Ghi trace chuỗi vào content_generation_traces trong scope RIÊNG (như DbLlmCostTracker) để không đụng
// unit-of-work của caller — đăng ký singleton, an toàn cho mọi consumer. content_item_id để NULL ở P1.
// Khi chuỗi fallback thì ghi thêm 1 dòng step_id="fallback" + lý do (§7) để soi được tại sao rơi single-shot.
public sealed class EfContentChainTraceSink(IServiceScopeFactory scopeFactory) : IContentChainTraceSink
{
    private const string FallbackStepId = "fallback";

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;

    public async Task WriteAsync(Guid tenantId, Guid? briefId, ContentChainOutcome outcome, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.Succeeded && outcome.Traces.Count == 0)
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Cùng một chain_run_id cho mọi mắt xích của lượt chạy này => nhóm/soi được cả chuỗi.
        var chainRunId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        foreach (var trace in outcome.Traces)
        {
            db.ContentGenerationTraces.Add(ContentGenerationTrace.Create(
                tenantId,
                chainRunId,
                trace.StepId,
                trace.PromptVersion,
                trace.Model,
                trace.InputTokens,
                trace.OutputTokens,
                trace.UsdCost,
                trace.LatencyMs,
                trace.GateResult,
                trace.PayloadJson,
                now,
                contentItemId: null,
                briefId: briefId));
        }

        if (!outcome.Succeeded)
        {
            var version = outcome.Traces.Count > 0 ? outcome.Traces[0].PromptVersion : string.Empty;
            db.ContentGenerationTraces.Add(ContentGenerationTrace.Create(
                tenantId,
                chainRunId,
                FallbackStepId,
                version,
                outcome.Model,
                inputTokens: 0,
                outputTokens: 0,
                usdCost: 0m,
                latencyMs: 0,
                ContentChainErrorCodes.ChainFallback,
                outcome.FallbackReason,
                now,
                contentItemId: null,
                briefId: briefId));
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
