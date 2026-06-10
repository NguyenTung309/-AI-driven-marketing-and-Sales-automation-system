using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Clawbot.Agents.Core.Chat;

namespace Clawbot.Agents.Core.Skills.Content;

public sealed record ImagePromptRequest(string Brief, string Platform, string Style, IReadOnlyList<string> BrandTokens);

public sealed record ImagePromptResult(string Prompt, string NegativePrompt, IReadOnlyDictionary<string, string> Hints);

public interface IImagePromptGenerator : ISkill
{
    Task<ImagePromptResult> GenerateAsync(ImagePromptRequest request, CancellationToken ct);
}

internal sealed partial class ClaudeImagePromptGenerator(IClaudeChatClient claude) : IImagePromptGenerator
{
    public string Name => "image-prompt-generation";

    public async Task<ImagePromptResult> GenerateAsync(ImagePromptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var brandCtx = request.BrandTokens.Count > 0
            ? $"Brand tokens: {string.Join(", ", request.BrandTokens)}"
            : "No brand tokens provided.";

        var userMsg = string.Format(CultureInfo.InvariantCulture,
            "Brief: {0}\nPlatform: {1}\nStyle: {2}\n{3}\n\n" +
            "Return JSON: {{\"prompt\":\"...\",\"negative_prompt\":\"...\",\"hints\":{{\"composition\":\"...\",\"lighting\":\"...\",\"mood\":\"...\"}}}}",
            request.Brief, request.Platform, request.Style, brandCtx);

        var reply = await claude.CompleteAsync(
            "You are an expert visual prompt engineer for AI image generation (FLUX, Midjourney, DALL-E). " +
            "Create detailed, specific prompts. Return only valid JSON.",
            Array.Empty<ChatTurn>(),
            userMsg,
            ct).ConfigureAwait(false);

        return ParseResult(reply.Text);
    }

    internal static ImagePromptResult ParseResult(string json)
    {
        var prompt = ExtractJsonString(json, "prompt") ?? json.Trim();
        var neg = ExtractJsonString(json, "negative_prompt") ?? string.Empty;

        var hints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hintsMatch = HintsBlockRegex().Match(json);
        if (hintsMatch.Success)
        {
            foreach (Match m in HintItemRegex().Matches(hintsMatch.Groups[1].Value))
                hints[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        return new ImagePromptResult(prompt, neg, hints);
    }

    private static string? ExtractJsonString(string json, string key)
    {
        var idx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (idx < 0) return null;
        var afterKey = json[(idx + key.Length + 2)..];
        var m = JsonStringValueRegex().Match(afterKey);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@":\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonStringValueRegex();

    [GeneratedRegex(@"""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonStringRegex();

    [GeneratedRegex(@"""hints""\s*:\s*\{(.*?)\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex HintsBlockRegex();

    [GeneratedRegex(@"""(\w+)""\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex HintItemRegex();
}
