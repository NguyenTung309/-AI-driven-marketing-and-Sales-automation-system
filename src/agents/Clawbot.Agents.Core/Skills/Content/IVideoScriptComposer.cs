namespace Clawbot.Agents.Core.Skills.Content;

public sealed record VideoScriptRequest(string Topic, string Platform, int LengthSeconds, string TargetAudience);

public sealed record VideoScript(string Hook, string Value, string Cta, IReadOnlyList<string> ShotList);

public interface IVideoScriptComposer : ISkill
{
    Task<VideoScript> ComposeAsync(VideoScriptRequest request, CancellationToken ct);
}

// Source: Internal Hook+Value+CTA formula (see .sdd/skills/content-copywriting.md).
// Wraps Claude with structured output JSON schema.
internal sealed class HvcVideoScriptComposer : IVideoScriptComposer
{
    public string Name => "short-video-script-formula";

    public Task<VideoScript> ComposeAsync(VideoScriptRequest request, CancellationToken ct) =>
        throw new NotImplementedException("TODO: SK with response_format=json_object and HVC schema.");
}
