namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record InjectionVerdict(bool IsMalicious, float Confidence, IReadOnlyList<string> Reasons);

public interface IPromptInjectionDefender : ISkill
{
    Task<InjectionVerdict> InspectAsync(string userInput, CancellationToken ct);
}

// Baseline phrase heuristic. Vendor swap target: lakera.ai/guard or protectai/llm-guard.
internal sealed class HeuristicPromptInjectionDefender : IPromptInjectionDefender
{
    private static readonly string[] SuspiciousPhrases =
    {
        "ignore previous instructions",
        "ignore all prior",
        "system prompt",
        "you are now",
        "act as",
        "developer mode",
        "jailbreak",
        "bỏ qua hướng dẫn",
        "phớt lờ chỉ dẫn",
        "đóng vai",
    };

    public string Name => "prompt-injection-defender";

    public Task<InjectionVerdict> InspectAsync(string userInput, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return Task.FromResult(new InjectionVerdict(false, 0f, Array.Empty<string>()));

        var lower = userInput.ToUpperInvariant();
        var hits = SuspiciousPhrases
            .Where(p => lower.Contains(p.ToUpperInvariant(), StringComparison.Ordinal))
            .ToList();

        if (hits.Count == 0)
            return Task.FromResult(new InjectionVerdict(false, 0.10f, Array.Empty<string>()));

        var confidence = Math.Min(0.50f + 0.15f * hits.Count, 0.95f);
        return Task.FromResult(new InjectionVerdict(true, confidence, hits));
    }
}
