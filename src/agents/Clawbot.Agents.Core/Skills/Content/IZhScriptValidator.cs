namespace Clawbot.Agents.Core.Skills.Content;

public sealed record ZhScriptCheck(bool IsConsistent, string DetectedScript, string? ConvertedText);

public interface IZhScriptValidator : ISkill
{
    Task<ZhScriptCheck> ValidateAsync(string chineseText, string targetScript, CancellationToken ct);
}

internal sealed class OpenCcZhScriptValidator : IZhScriptValidator
{
    public string Name => "zh-script-validation";

    private static readonly HashSet<int> TraditionalSpecificChars =
    [
        0x8449, 0x8A0A, 0x8A9E, 0x96FB, 0x9801, 0x98DB, 0x9AD8, 0x570B,
        0x5B78, 0x7FD2, 0x8F1B, 0x958B, 0x9023, 0x5E74, 0x6703, 0x5712,
        0x5340, 0x9EC3, 0x91D1, 0x83EF, 0x767C, 0x8F49, 0x5EE3, 0x5F35,
        0x570B, 0x8A00, 0x9580, 0x9996, 0x66F8, 0x9577, 0x9650, 0x5718,
    ];

    public Task<ZhScriptCheck> ValidateAsync(string chineseText, string targetScript, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chineseText);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetScript);

        var isSimplifiedTarget = targetScript.Equals("s", StringComparison.OrdinalIgnoreCase);
        var isTraditionalTarget = targetScript.Equals("t", StringComparison.OrdinalIgnoreCase);
        if (!isSimplifiedTarget && !isTraditionalTarget)
            throw new ArgumentException("targetScript must be 's' (simplified) or 't' (traditional).", nameof(targetScript));

        var hasTraditional = false;
        var hasCjk = false;

        foreach (var rune in chineseText.EnumerateRunes())
        {
            var cp = rune.Value;
            if (cp is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF)
            {
                hasCjk = true;
                if (cp is >= 0x3400 and <= 0x4DBF || TraditionalSpecificChars.Contains(cp))
                    hasTraditional = true;
            }
            if (cp is >= 0x20000 and <= 0x2A6DF or >= 0xF900 and <= 0xFAFF)
                hasTraditional = true;
        }

        var detected = hasTraditional ? "Traditional" : (hasCjk ? "Simplified" : "Unknown");

        var isConsistent = isSimplifiedTarget
            ? !hasTraditional
            : hasTraditional;

        string? converted = null;
        if (!isConsistent)
        {
            try
            {
                converted = isSimplifiedTarget
                    ? OpenCCNET.ZhConverter.HantToHans(chineseText)
                    : OpenCCNET.ZhConverter.HansToHant(chineseText);
                if (converted == chineseText) converted = null;
            }
            catch { converted = null; }
        }

        return Task.FromResult(new ZhScriptCheck(isConsistent, detected, converted));
    }
}
