namespace Clawbot.Agents.Core.Orchestrator;

public sealed record AutonomousRunRequest(
    Guid TenantId,
    Guid SessionId,
    string Goal,
    string Source,
    bool RequiresApproval,
    IReadOnlySet<string> ExecutionPermissions,
    bool DryRun = false);

public sealed record AutonomousRunResult(string Status, string? Reason, int RoundCount)
{
    public static AutonomousRunResult Completed(int rounds) => new("completed", null, rounds);
    public static AutonomousRunResult PendingApproval(int rounds) => new("pending_approval", null, rounds);
    public static AutonomousRunResult Failed(string reason, int rounds) => new("failed", reason, rounds);
    public static AutonomousRunResult Cancelled(int rounds) => new("cancelled", null, rounds);

    // Task lỗi + chính sách "pause": phiên dừng lại chờ người sửa output thay vì đốt LLM cho một plan mới.
    public static AutonomousRunResult AwaitingIntervention(int rounds) =>
        new("paused", "awaiting_intervention", rounds);
}

// Chính sách khi một task fail. Replan sinh plan MỚI HOÀN TOÀN (mọi task về pending, output cũ bị vứt),
// nên mỗi vòng replan nhân chi phí lên gần bằng một lần chạy đầy đủ. Mặc định vì thế là dừng chờ người.
public static class OrchestratorFailurePolicies
{
    public const string Pause = "pause";
    public const string Replan = "replan";
    public const string Fail = "fail";

    public static string Normalize(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return value is Pause or Replan or Fail ? value : Pause;
    }

    // Phiên chạy theo lịch không có ai ngồi chờ để sửa output, "pause" sẽ treo phiên tới khi có người
    // mở dashboard. Với nguồn không người trực: replan đúng một lượt (MaxRounds), hết lượt thì fail.
    // Tenant chọn "fail" là chọn chặt hơn nên giữ nguyên, không nới thành replan.
    public static string ForSource(string policy, string? source) =>
        policy is Pause && IsUnattended(source) ? Replan : policy;

    private static bool IsUnattended(string? source) =>
        string.Equals((source ?? string.Empty).Trim(), UnattendedSource, StringComparison.OrdinalIgnoreCase);

    // Khớp AgentScheduleRunner: "manual:" -> "manual", còn lại -> "schedule".
    private const string UnattendedSource = "schedule";
}

public sealed class AutonomousOrchestratorOptions
{
    // Chỉ giới hạn số lần REPLAN. Một vòng là đủ: prompt replan không nhận thêm thông tin mới ở
    // vòng 2/3, nên các vòng sau chỉ nhân chi phí chứ không tăng tỉ lệ cứu được plan.
    public int MaxRounds { get; init; } = 1;
    public int MaxConcurrency { get; init; } = 3;        // ponytail: sequential execution for now; cap reserved for parallel upgrade
    public decimal PerTaskEstimateUsd { get; init; } = 0.01m;
    public string FailurePolicy { get; init; } = OrchestratorFailurePolicies.Pause;

    // Transient (timeout / 5xx / 429) failures retry the SAME task without burning a replan round.
    // Only logical failures (non-transient exceptions or AgentResult.Success=false) count toward MaxRounds.
    public int MaxTransientRetries { get; init; } = 2;
    public int TransientBackoffBaseMs { get; init; } = 2000;
}
