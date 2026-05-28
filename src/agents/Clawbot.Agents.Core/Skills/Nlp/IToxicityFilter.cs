namespace Clawbot.Agents.Core.Skills.Nlp;

public sealed record ToxicityScores(
    float Toxicity,
    float Insult,
    float Threat,
    float SexualExplicit,
    float Profanity);

public interface IToxicityFilter : ISkill
{
    Task<ToxicityScores> ScoreAsync(string text, CancellationToken ct);
    Task<bool> IsBlockedAsync(string text, float threshold, CancellationToken ct);
}

// Source: https://github.com/unitaryai/detoxify (REST sidecar) OR Google Perspective API.
internal sealed class DetoxifyToxicityFilter : IToxicityFilter
{
    public string Name => "toxicity-filter";

    public Task<ToxicityScores> ScoreAsync(string text, CancellationToken ct) =>
        throw new NotImplementedException("TODO: call detoxify REST sidecar.");

    public Task<bool> IsBlockedAsync(string text, float threshold, CancellationToken ct) =>
        throw new NotImplementedException("TODO: ScoreAsync then compare against threshold.");
}
