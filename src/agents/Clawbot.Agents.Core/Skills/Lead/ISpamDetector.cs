namespace Clawbot.Agents.Core.Skills.Lead;

public sealed record SpamSignal(bool IsSpam, float Confidence, string? Reason);

public interface ISpamDetector : ISkill
{
    Task<SpamSignal> EvaluateAsync(string text, string? senderHandle, string? sourcePlatform, CancellationToken ct);
}

// Source: https://akismet.com/developers/  (REST API).
// Heuristic fallback when Akismet unavailable: regex repeated emoji / URL flood / known scam keywords.
internal sealed class AkismetSpamDetector : ISpamDetector
{
    public string Name => "spam-detection";

    public Task<SpamSignal> EvaluateAsync(string text, string? senderHandle, string? sourcePlatform, CancellationToken ct) =>
        throw new NotImplementedException("TODO: POST https://<key>.rest.akismet.com/1.1/comment-check via HttpClient.");
}
