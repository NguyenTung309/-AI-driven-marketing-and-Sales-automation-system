using Clawbot.Agents.Core.Content;
using Clawbot.Domain.Content;

namespace Clawbot.AgentService.Services;

// Phase 2.12: wires ContentReviewer into the durable coordinator path.
// Replaces TemporaryFailClosedContentReviewExecutor once review completion + vision are live.
public sealed class ContentReviewExecutor(ContentReviewer reviewer) : IContentReviewExecutor
{
    public string AgentCode => ContentReviewer.AgentCode;

    public async Task<ContentReviewExecutionResult> ReviewAsync(
        ContentReviewExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var outcome = await reviewer.ReviewContentItemAsync(
            request.TenantId,
            request.ContentItemId,
            request.Platform,
            request.Body,
            cancellationToken).ConfigureAwait(false);

        return new ContentReviewExecutionResult(
            outcome.ReviewStatus,
            outcome.ImageReviewStatus,
            outcome.ReviewedImageCount,
            outcome.ReasonCode);
    }
}
