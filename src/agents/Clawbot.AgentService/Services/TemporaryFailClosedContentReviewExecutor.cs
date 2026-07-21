using Clawbot.Domain.Content;

namespace Clawbot.AgentService.Services;

// Phase 2.3 temporary adapter: keeps DI resolvable and fail-closed until the strict
// provider completion contract lands in 2.5–2.12. Never approves content.
public sealed class TemporaryFailClosedContentReviewExecutor : IContentReviewExecutor
{
    public string AgentCode => "reviewer-agent";

    public Task<ContentReviewExecutionResult> ReviewAsync(
        ContentReviewExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ContentReviewExecutionResult(
            ContentItem.ReviewStatusFailed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reasonCode: "reviewer_error"));
    }
}
