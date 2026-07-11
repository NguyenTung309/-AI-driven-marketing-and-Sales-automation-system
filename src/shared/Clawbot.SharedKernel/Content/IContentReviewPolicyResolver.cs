namespace Clawbot.SharedKernel.Content;

// Review-gate P1: tenant flag RequireContentReview — khi bật, publish/schedule đòi chữ ký reviewer agent.
// Interface ở SharedKernel để cả API (endpoints), Infrastructure (ContentPublishJob) và AgentService (tools) dùng chung.
public interface IContentReviewPolicyResolver
{
    Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default);
}
