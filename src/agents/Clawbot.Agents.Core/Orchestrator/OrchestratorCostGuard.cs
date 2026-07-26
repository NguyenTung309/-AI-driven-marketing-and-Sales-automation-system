using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Ops;

namespace Clawbot.Agents.Core.Orchestrator;

public sealed record CostGuardResult(bool Allowed, string? Reason, Guid? ReservationId = null)
{
    public static CostGuardResult Allow(Guid? reservationId = null) => new(true, null, reservationId);
    public static CostGuardResult Deny(string reason) => new(false, reason);
}

public sealed class OrchestratorCostGuard(ILlmCostTracker tracker)
{
    private readonly ILlmCostTracker _tracker = tracker;
    private readonly ILlmCostReservationStore? _reservations = tracker as ILlmCostReservationStore;

    public async Task<CostGuardResult> CanStartAsync(
        Guid tenantId,
        decimal estimatedUsd,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        var summary = await _tracker.SummaryAsync(tenantId, at, ct).ConfigureAwait(false);
        return summary.MonthToDateUsd + Math.Max(0m, estimatedUsd) > summary.CapUsd
            ? CostGuardResult.Deny("cost_cap_preflight")
            : CostGuardResult.Allow();
    }

    public async Task<CostGuardResult> TryReserveAsync(
        Guid tenantId,
        decimal estimatedUsd,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        if (_reservations is null)
        {
            var summary = await _tracker.SummaryAsync(tenantId, at, ct).ConfigureAwait(false);
            return summary.MonthToDateUsd + Math.Max(0m, estimatedUsd) > summary.CapUsd
                ? CostGuardResult.Deny("cost_cap_midrun")
                : CostGuardResult.Allow();
        }

        var result = await _reservations.TryReserveAsync(tenantId, estimatedUsd, at, ct).ConfigureAwait(false);
        return result.Allowed
            ? CostGuardResult.Allow(result.ReservationId)
            : CostGuardResult.Deny(result.Reason ?? "cost_cap_midrun");
    }

    public Task RecordAsync(
        Guid tenantId,
        string agentCode,
        ClaudeReply reply,
        DateTimeOffset at,
        Guid? reservationId,
        Guid? sessionId = null,
        CancellationToken ct = default) =>
        // Bỏ qua chỉ khi không có gì để ghi; lượt có token nhưng cost 0 (provider không trả usage,
        // rate chưa cấu hình) vẫn phải vào ledger, nếu không cap tháng mất luôn dữ liệu.
        reply.UsdCost <= 0m && reply.InputTokens <= 0 && reply.OutputTokens <= 0
            ? Task.CompletedTask
            : _tracker.RecordAsync(new CostEntry(tenantId, agentCode, reply.Model, reply.InputTokens, reply.OutputTokens, reply.UsdCost, at, reservationId, sessionId, reply.IsEstimated), ct);

    public Task AdjustReservationAsync(
        Guid tenantId,
        Guid? reservationId,
        CancellationToken ct = default) =>
        ReleaseReservationAsync(tenantId, reservationId, ct);

    public Task ReleaseReservationAsync(
        Guid tenantId,
        Guid? reservationId,
        CancellationToken ct = default) =>
        _reservations is not null && reservationId.HasValue
            ? _reservations.ReleaseReservationAsync(tenantId, reservationId.Value, ct)
            : Task.CompletedTask;
}
