namespace Clawbot.Agents.Core.Skills.Content;

public sealed record ImagePromptRequest(string Brief, string Platform, string Style, IReadOnlyList<string> BrandTokens);

public sealed record ImagePromptResult(string Prompt, string NegativePrompt, IReadOnlyDictionary<string, string> Hints);

public interface IImagePromptGenerator : ISkill
{
    Task<ImagePromptResult> GenerateAsync(ImagePromptRequest request, CancellationToken ct);
}

// Source: Claude Sonnet 4.6 → output structured prompt → optionally feed to https://replicate.com/black-forest-labs/flux-schnell.
internal sealed class ClaudeImagePromptGenerator : IImagePromptGenerator
{
    public string Name => "image-prompt-generation";

    public Task<ImagePromptResult> GenerateAsync(ImagePromptRequest request, CancellationToken ct) =>
        throw new NotImplementedException("TODO: SK ChatCompletion with system prompt for visual prompt engineering.");
}
