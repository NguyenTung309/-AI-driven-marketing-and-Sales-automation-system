namespace Clawbot.Agents.Core.Orchestrator;

// Đọc chính sách xử lý task lỗi của tenant (pause | replan | fail). Cài đặt nằm ở Infrastructure trên
// Tenant.OrchestratorFailurePolicy; interface để lại Agents.Core cho orchestrator khỏi dính Domain/EF,
// giống hệt IOrchestrationApprovalResolver. Không cấu hình => Pause (rẻ nhất, có người gác).
public interface IOrchestrationFailurePolicyResolver
{
    Task<string> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
