using Clawbot.Domain.Content;

namespace Clawbot.AgentService.Services;

// Phase 2.4: shared generation write path — stamp generator identity and queue one
// immediately-due durable review task per created revision in the same transaction.
internal static class ContentGenerationPersistence
{
    public const string GeneratorAgentCode = "content-agent";
    public const string GeneratorAgentNotConfigured = "content_agent_not_configured";
    public const string GeneratorAgentRequired = "content_generator_agent_required";

    public static ContentReviewTask CreateImmediateReviewTask(
        Guid tenantId,
        Guid contentItemId,
        int contentRevision,
        DateTimeOffset at) =>
        ContentReviewTask.CreatePending(
            tenantId,
            contentItemId,
            contentRevision,
            nextAttemptAt: at,
            createdAt: at);
}
