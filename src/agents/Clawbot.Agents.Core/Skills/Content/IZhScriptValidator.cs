namespace Clawbot.Agents.Core.Skills.Content;

public sealed record ZhScriptCheck(bool IsConsistent, string DetectedScript, string? ConvertedText);  // Simplified|Traditional|Mixed

public interface IZhScriptValidator : ISkill
{
    Task<ZhScriptCheck> ValidateAsync(string chineseText, string targetScript, CancellationToken ct);  // targetScript = "s" | "t"
}

// Source: https://github.com/BYVoid/OpenCC (s2t.json / t2s.json conversion rules).
// .NET shells out to OpenCC binary OR uses OpenCC.NET NuGet (if available).
internal sealed class OpenCcZhScriptValidator : IZhScriptValidator
{
    public string Name => "zh-script-validation";

    public Task<ZhScriptCheck> ValidateAsync(string chineseText, string targetScript, CancellationToken ct) =>
        throw new NotImplementedException("TODO: call OpenCC binary via Process.Start OR OpenCC.NET converter.");
}
