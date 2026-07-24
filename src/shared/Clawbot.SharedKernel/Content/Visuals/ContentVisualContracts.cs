using System.Collections.Frozen;

namespace Clawbot.SharedKernel.Content.Visuals;

public sealed record ContentVisualPreset
{
    private static readonly ContentVisualPreset LandscapePreset =
        new("1200x630", 1200, 630);

    private static readonly ContentVisualPreset SquarePreset =
        new("1080x1080", 1080, 1080);

    private static readonly IReadOnlyList<ContentVisualPreset> SupportedPresets =
        Array.AsReadOnly([LandscapePreset, SquarePreset]);

    private static readonly FrozenDictionary<string, ContentVisualPreset> PresetsByToken =
        SupportedPresets.ToFrozenDictionary(preset => preset.Token, StringComparer.Ordinal);

    private ContentVisualPreset(string token, int width, int height)
    {
        Token = token;
        Width = width;
        Height = height;
    }

    public static ContentVisualPreset Landscape => LandscapePreset;
    public static ContentVisualPreset Square => SquarePreset;
    public static IReadOnlyList<ContentVisualPreset> Supported => SupportedPresets;

    public string Token { get; }
    public int Width { get; }
    public int Height { get; }

    public static ContentVisualPreset Parse(string? token, string path = "$.preset")
    {
        if (!TryParse(token, out var preset))
            throw ContentVisualValidation.Error("preset_not_allowed", path);

        return preset!;
    }

    public static bool TryParse(string? token, out ContentVisualPreset? preset)
    {
        preset = null;
        return token is not null && PresetsByToken.TryGetValue(token, out preset);
    }

    public override string ToString() => Token;
}

public static class TrustedThemeTokenCatalog
{
    private static readonly IReadOnlyList<string> AllowedTokens =
        Array.AsReadOnly(["light", "dark", "brand"]);

    private static readonly FrozenSet<string> AllowedSet =
        AllowedTokens.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<string> Allowed => AllowedTokens;

    public static bool IsAllowed(string? token) =>
        token is not null && AllowedSet.Contains(token);
}

public sealed record TrustedTemplateReference
{
    private TrustedTemplateReference(string templateId, int version, string sha256)
    {
        TemplateId = templateId;
        Version = version;
        Sha256 = sha256;
    }

    public string TemplateId { get; }
    public int Version { get; }
    public string Sha256 { get; }

    public static TrustedTemplateReference Create(
        string templateId,
        int version,
        string sha256,
        string path = "$.template")
    {
        var validatedId = ContentVisualValidation.ValidateIdentifier(templateId, $"{path}.id");
        if (version <= 0)
            throw ContentVisualValidation.Error("template_version_invalid", $"{path}.version");
        var validatedHash = ContentVisualValidation.ValidateSha256(sha256, $"{path}.sha256");
        return new TrustedTemplateReference(validatedId, version, validatedHash);
    }
}

public sealed class ContentVisualSlot
{
    private ContentVisualSlot(string name, IReadOnlyList<string> lines)
    {
        Name = name;
        Lines = lines;
    }

    public string Name { get; }
    public IReadOnlyList<string> Lines { get; }

    public static ContentVisualSlot Create(
        string name,
        IEnumerable<string> lines,
        string path = "$.slots")
    {
        var validatedName = ContentVisualValidation.ValidateIdentifier(name, $"{path}.name");
        var suppliedLines = ContentVisualValidation.CopyBounded(
            lines,
            ContentVisualLimits.MaximumLinesPerSlot,
            "slot_line_limit_exceeded",
            $"{path}.lines");
        if (suppliedLines.Length == 0)
        {
            throw ContentVisualValidation.Error("slot_line_limit_exceeded", $"{path}.lines");
        }

        var normalizedLines = new string[suppliedLines.Length];
        for (var index = 0; index < suppliedLines.Length; index++)
        {
            normalizedLines[index] = ContentVisualValidation.NormalizeLine(
                suppliedLines[index],
                ContentVisualLimits.MaximumGraphemesPerLine,
                $"{path}.lines[{index}]");
        }

        return new ContentVisualSlot(validatedName, Array.AsReadOnly(normalizedLines));
    }
}

public sealed record ContentThemeTokenBinding
{
    private ContentThemeTokenBinding(string name, string token)
    {
        Name = name;
        Token = token;
    }

    public string Name { get; }
    public string Token { get; }

    public static ContentThemeTokenBinding Create(
        string name,
        string token,
        string path = "$.themeTokens")
    {
        var validatedName = ContentVisualValidation.ValidateIdentifier(name, $"{path}.name");
        if (!TrustedThemeTokenCatalog.IsAllowed(token))
            throw ContentVisualValidation.Error("theme_token_not_allowed", $"{path}.token");

        return new ContentThemeTokenBinding(validatedName, token);
    }
}
