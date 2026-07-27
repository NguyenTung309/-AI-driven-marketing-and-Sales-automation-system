using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Clawbot.SharedKernel.Content.Visuals;

public sealed record TrustedVisualSlotDefinition
{
    private TrustedVisualSlotDefinition(
        string name,
        bool isRequired,
        int maxLines,
        int maxGraphemesPerLine)
    {
        Name = name;
        IsRequired = isRequired;
        MaxLines = maxLines;
        MaxGraphemesPerLine = maxGraphemesPerLine;
    }

    public string Name { get; }
    public bool IsRequired { get; }
    public int MaxLines { get; }
    public int MaxGraphemesPerLine { get; }

    public static TrustedVisualSlotDefinition Create(
        string name,
        bool isRequired,
        int maxLines,
        int maxGraphemesPerLine)
    {
        var validatedName = ContentVisualValidation.ValidateIdentifier(name, "$.slotDefinition.name");
        if (maxLines <= 0 || maxLines > ContentVisualLimits.MaximumLinesPerSlot)
            throw ContentVisualValidation.Error("slot_line_limit_invalid", "$.slotDefinition.maxLines");
        if (maxGraphemesPerLine <= 0
            || maxGraphemesPerLine > ContentVisualLimits.MaximumGraphemesPerLine)
        {
            throw ContentVisualValidation.Error(
                "line_grapheme_limit_invalid",
                "$.slotDefinition.maxGraphemesPerLine");
        }

        return new TrustedVisualSlotDefinition(
            validatedName,
            isRequired,
            maxLines,
            maxGraphemesPerLine);
    }
}

public sealed class TrustedThemeTokenDefinition
{
    private readonly FrozenSet<string> _allowedTokenSet;

    private TrustedThemeTokenDefinition(
        string name,
        bool isRequired,
        IReadOnlyList<string> allowedTokens,
        FrozenSet<string> allowedTokenSet)
    {
        Name = name;
        IsRequired = isRequired;
        AllowedTokens = allowedTokens;
        _allowedTokenSet = allowedTokenSet;
    }

    public string Name { get; }
    public bool IsRequired { get; }
    public IReadOnlyList<string> AllowedTokens { get; }

    public static TrustedThemeTokenDefinition Create(
        string name,
        bool isRequired,
        IEnumerable<string> allowedTokens)
    {
        var validatedName = ContentVisualValidation.ValidateIdentifier(
            name,
            "$.themeDefinition.name");
        var copiedTokens = ContentVisualValidation.CopyBounded(
            allowedTokens,
            TrustedThemeTokenCatalog.Allowed.Count,
            "theme_definition_tokens_invalid",
            "$.themeDefinition.allowedTokens");
        if (copiedTokens.Length == 0)
        {
            throw ContentVisualValidation.Error(
                "theme_definition_tokens_required",
                "$.themeDefinition.allowedTokens");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in copiedTokens)
        {
            if (!TrustedThemeTokenCatalog.IsAllowed(token))
            {
                throw ContentVisualValidation.Error(
                    "theme_token_not_allowed",
                    "$.themeDefinition.allowedTokens");
            }

            if (!seen.Add(token))
            {
                throw ContentVisualValidation.Error(
                    "theme_definition_token_duplicate",
                    "$.themeDefinition.allowedTokens");
            }
        }

        Array.Sort(copiedTokens, StringComparer.Ordinal);
        var readOnlyTokens = Array.AsReadOnly(copiedTokens);
        return new TrustedThemeTokenDefinition(
            validatedName,
            isRequired,
            readOnlyTokens,
            copiedTokens.ToFrozenSet(StringComparer.Ordinal));
    }

    internal bool Allows(string token) => _allowedTokenSet.Contains(token);
}

public sealed class TrustedTemplateDefinition
{
    private readonly FrozenDictionary<string, TrustedVisualSlotDefinition> _slotsByName;
    private readonly FrozenDictionary<string, TrustedThemeTokenDefinition> _themesByName;
    private readonly FrozenSet<string> _presetTokens;

    private TrustedTemplateDefinition(
        TrustedTemplateReference identity,
        IReadOnlyList<ContentVisualPreset> presets,
        IReadOnlyList<TrustedVisualSlotDefinition> slots,
        IReadOnlyList<TrustedThemeTokenDefinition> themes)
    {
        Identity = identity;
        Presets = presets;
        Slots = slots;
        ThemeTokens = themes;
        _presetTokens = presets.Select(preset => preset.Token)
            .ToFrozenSet(StringComparer.Ordinal);
        _slotsByName = slots.ToFrozenDictionary(slot => slot.Name, StringComparer.Ordinal);
        _themesByName = themes.ToFrozenDictionary(theme => theme.Name, StringComparer.Ordinal);
    }

    public TrustedTemplateReference Identity { get; }
    public IReadOnlyList<ContentVisualPreset> Presets { get; }
    public IReadOnlyList<TrustedVisualSlotDefinition> Slots { get; }
    public IReadOnlyList<TrustedThemeTokenDefinition> ThemeTokens { get; }

    public static TrustedTemplateDefinition Create(
        TrustedTemplateReference identity,
        IEnumerable<ContentVisualPreset> presets,
        IEnumerable<TrustedVisualSlotDefinition> slots,
        IEnumerable<TrustedThemeTokenDefinition> themeTokens)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var copiedPresets = CopyUniquePresets(presets);
        var copiedSlots = CopyUniqueSlots(slots);
        var copiedThemes = CopyUniqueThemes(themeTokens);
        return new TrustedTemplateDefinition(
            identity,
            Array.AsReadOnly(copiedPresets),
            Array.AsReadOnly(copiedSlots),
            Array.AsReadOnly(copiedThemes));
    }

    internal bool Supports(ContentVisualPreset preset) =>
        _presetTokens.Contains(preset.Token);

    internal bool TryGetSlot(
        string name,
        [NotNullWhen(true)] out TrustedVisualSlotDefinition? definition) =>
        _slotsByName.TryGetValue(name, out definition);

    internal bool TryGetTheme(
        string name,
        [NotNullWhen(true)] out TrustedThemeTokenDefinition? definition) =>
        _themesByName.TryGetValue(name, out definition);

    private static ContentVisualPreset[] CopyUniquePresets(
        IEnumerable<ContentVisualPreset> presets)
    {
        var copied = ContentVisualValidation.CopyBounded(
            presets,
            ContentVisualPreset.Supported.Count,
            "template_presets_invalid",
            "$.template.presets");
        if (copied.Length == 0)
            throw ContentVisualValidation.Error("template_presets_required", "$.template.presets");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in copied)
        {
            if (preset is null || !ContentVisualPreset.TryParse(preset.Token, out _))
                throw ContentVisualValidation.Error("preset_not_allowed", "$.template.presets");
            if (!seen.Add(preset.Token))
                throw ContentVisualValidation.Error("template_preset_duplicate", "$.template.presets");
        }

        Array.Sort(copied, (left, right) => StringComparer.Ordinal.Compare(left.Token, right.Token));
        return copied;
    }

    private static TrustedVisualSlotDefinition[] CopyUniqueSlots(
        IEnumerable<TrustedVisualSlotDefinition> slots)
    {
        var copied = ContentVisualValidation.CopyBounded(
            slots,
            ContentVisualLimits.MaximumSlotsPerSpec,
            "template_slots_invalid",
            "$.template.slots");
        if (copied.Length == 0)
            throw ContentVisualValidation.Error("template_slots_invalid", "$.template.slots");

        EnsureUniqueNames(copied, slot => slot.Name, "template_slot_duplicate", "$.template.slots");
        Array.Sort(copied, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return copied;
    }

    private static TrustedThemeTokenDefinition[] CopyUniqueThemes(
        IEnumerable<TrustedThemeTokenDefinition> themes)
    {
        var copied = ContentVisualValidation.CopyBounded(
            themes,
            ContentVisualLimits.MaximumThemeBindingsPerSpec,
            "template_themes_invalid",
            "$.template.themeTokens");

        EnsureUniqueNames(
            copied,
            theme => theme.Name,
            "template_theme_duplicate",
            "$.template.themeTokens");
        Array.Sort(copied, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return copied;
    }

    private static void EnsureUniqueNames<T>(
        IEnumerable<T> values,
        Func<T, string> getName,
        string code,
        string path)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null || !seen.Add(getName(value)))
                throw ContentVisualValidation.Error(code, path);
        }
    }
}

public interface ITrustedTemplateCatalog
{
    bool TryGetExact(
        string templateId,
        int version,
        string sha256,
        [NotNullWhen(true)] out TrustedTemplateDefinition? definition);
}

public sealed class TrustedTemplateCatalog : ITrustedTemplateCatalog
{
    private readonly FrozenDictionary<(string TemplateId, int Version), TrustedTemplateDefinition>
        _definitionsByVersion;

    public TrustedTemplateCatalog(IEnumerable<TrustedTemplateDefinition> definitions)
    {
        var copied = ContentVisualValidation.CopyBounded(
            definitions,
            ContentVisualLimits.MaximumTrustedTemplates,
            "template_catalog_limit_exceeded",
            "$.catalog");
        var byVersion = new Dictionary<
            (string TemplateId, int Version),
            TrustedTemplateDefinition>();

        foreach (var definition in copied)
        {
            if (definition is null)
                throw ContentVisualValidation.Error("template_definition_required", "$.catalog");
            var key = (definition.Identity.TemplateId, definition.Identity.Version);
            if (!byVersion.TryAdd(key, definition))
                throw ContentVisualValidation.Error("template_version_duplicate", "$.catalog");
        }

        Array.Sort(
            copied,
            (left, right) =>
            {
                var idComparison = StringComparer.Ordinal.Compare(
                    left.Identity.TemplateId,
                    right.Identity.TemplateId);
                return idComparison != 0
                    ? idComparison
                    : left.Identity.Version.CompareTo(right.Identity.Version);
            });
        Definitions = Array.AsReadOnly(copied);
        _definitionsByVersion = byVersion.ToFrozenDictionary();
    }

    public IReadOnlyList<TrustedTemplateDefinition> Definitions { get; }

    public bool TryGetExact(
        string templateId,
        int version,
        string sha256,
        [NotNullWhen(true)] out TrustedTemplateDefinition? definition)
    {
        definition = null;
        if (!_definitionsByVersion.TryGetValue((templateId, version), out var candidate)
            || !string.Equals(candidate.Identity.Sha256, sha256, StringComparison.Ordinal))
        {
            return false;
        }

        definition = candidate;
        return true;
    }
}
