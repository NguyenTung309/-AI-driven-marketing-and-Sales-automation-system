using Clawbot.Domain.Content;

namespace Clawbot.AgentService.Services;

public sealed record ContentReviewWorkerOptions
{
    public const string SectionName = "ContentReviewWorker";

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan LeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public int BatchSize { get; init; } = 10;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxAttempts { get; init; } = ContentItem.MaxAgentReviewAttempts;
}
