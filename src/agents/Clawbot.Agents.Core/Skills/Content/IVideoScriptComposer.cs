using System.Globalization;
using System.Text.RegularExpressions;
using Clawbot.Agents.Core.Chat;

namespace Clawbot.Agents.Core.Skills.Content;

public sealed record VideoScriptRequest(string Topic, string Platform, int LengthSeconds, string TargetAudience);

public sealed record VideoScript(string Hook, string Value, string Cta, IReadOnlyList<string> ShotList);

public interface IVideoScriptComposer : ISkill
{
    Task<VideoScript> ComposeAsync(VideoScriptRequest request, CancellationToken ct);
}

internal sealed partial class HvcVideoScriptComposer(IClaudeChatClient claude) : IVideoScriptComposer
{
    public string Name => "short-video-script-formula";

    public async Task<VideoScript> ComposeAsync(VideoScriptRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var userMsg = string.Format(CultureInfo.InvariantCulture,
            "Topic: {0}\nPlatform: {1}\nLength: {2}s\nAudience: {3}\n\n" +
            "Use the Hook-Value-CTA formula. Return JSON: " +
            "{{\"hook\":\"...\",\"value\":\"...\",\"cta\":\"...\",\"shot_list\":[\"shot1\",\"shot2\",...]}}",
            request.Topic, request.Platform, request.LengthSeconds, request.TargetAudience);

        var reply = await claude.CompleteAsync(
            "You are a short-video scriptwriter specializing in the Hook-Value-CTA (HVC) formula. " +
            "Hook grabs attention in 3s, Value delivers the core message, CTA drives action. " +
            "Return only valid JSON.",
            Array.Empty<ChatTurn>(),
            userMsg,
            ct).ConfigureAwait(false);

        return ParseScript(reply.Text);
    }

    internal static VideoScript ParseScript(string json)
    {
        var hook = ExtractField(json, "hook") ?? json.Trim();
        var value = ExtractField(json, "value") ?? string.Empty;
        var cta = ExtractField(json, "cta") ?? string.Empty;

        var shots = new List<string>();
        var arrayMatch = ShotListArrayRegex().Match(json);
        if (arrayMatch.Success)
        {
            foreach (Match m in ShotItemRegex().Matches(arrayMatch.Groups[1].Value))
                shots.Add(m.Groups[1].Value.Trim());
        }

        return new VideoScript(hook, value, cta, shots);
    }

    private static string? ExtractField(string json, string key)
    {
        var idx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
        if (idx < 0) return null;
        var afterKey = json[(idx + key.Length + 2)..];
        var m = JsonKeyValueRegex().Match(afterKey);
        return m.Success ? m.Groups[1].Value : null;
    }

    [GeneratedRegex(@":\s*""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonKeyValueRegex();

    [GeneratedRegex(@"""shot_list""\s*:\s*\[(.*?)\]", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ShotListArrayRegex();

    [GeneratedRegex(@"""([^""\\]*(?:\\.[^""\\]*)*)""", RegexOptions.CultureInvariant)]
    private static partial Regex ShotItemRegex();
}
