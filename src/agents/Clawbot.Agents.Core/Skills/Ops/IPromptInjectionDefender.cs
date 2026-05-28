namespace Clawbot.Agents.Core.Skills.Ops;

public sealed record InjectionVerdict(bool IsMalicious, float Confidence, IReadOnlyList<string> Reasons);

public interface IPromptInjectionDefender : ISkill
{
    Task<InjectionVerdict> InspectAsync(string userInput, CancellationToken ct);
}

// Source: https://www.lakera.ai/lakera-guard (REST) OR https://github.com/protectai/llm-guard.
// Wrap inbound text before passing to Agent-Chat / Agent-SaleAssist.
internal sealed class LakeraPromptInjectionDefender : IPromptInjectionDefender
{
    public string Name => "prompt-injection-defender";

    public Task<InjectionVerdict> InspectAsync(string userInput, CancellationToken ct) =>
        throw new NotImplementedException("TODO: POST https://api.lakera.ai/v1/guard with X-Lakera-Key header.");
}
