namespace Clawbot.SharedKernel.Content.Visuals;

public sealed class ContentRenderSpec
{
    public const int CurrentSchemaVersion = 1;

    private ContentRenderSpec(
        TrustedTemplateReference template,
        ContentVisualPreset preset,
        IReadOnlyList<ContentVisualSlot> slots,
        IReadOnlyList<ContentThemeTokenBinding> themeTokens)
    {
        SchemaVersion = CurrentSchemaVersion;
        Template = template;
        Preset = preset;
        Slots = slots;
        ThemeTokens = themeTokens;
    }

    public int SchemaVersion { get; }
    public TrustedTemplateReference Template { get; }
    public ContentVisualPreset Preset { get; }
    public IReadOnlyList<ContentVisualSlot> Slots { get; }
    public IReadOnlyList<ContentThemeTokenBinding> ThemeTokens { get; }
    public string CanonicalJson => ContentRenderSpecCanonicalizer.ToCanonicalJson(this);
    public string CanonicalSha256 => ContentRenderSpecCanonicalizer.ComputeSha256(this);

    public static ContentRenderSpec Create(
        ITrustedTemplateCatalog catalog,
        TrustedTemplateReference template,
        ContentVisualPreset preset,
        IEnumerable<ContentVisualSlot> slots,
        IEnumerable<ContentThemeTokenBinding> themeTokens)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(themeTokens);

        if (!catalog.TryGetExact(
                template.TemplateId,
                template.Version,
                template.Sha256,
                out var definition))
        {
            throw ContentVisualValidation.Error("template_not_trusted", "$.template");
        }

        if (!definition.Supports(preset))
        {
            throw ContentVisualValidation.Error(
                "preset_not_trusted_for_template",
                "$.preset");
        }

        var validatedSlots = ValidateSlots(slots, definition);
        var validatedThemes = ValidateThemeTokens(themeTokens, definition);
        var spec = new ContentRenderSpec(
            template,
            preset,
            Array.AsReadOnly(validatedSlots),
            Array.AsReadOnly(validatedThemes));
        if (ContentRenderSpecCanonicalizer.GetCanonicalUtf8(spec).Length
            > ContentVisualLimits.MaximumJsonUtf8Bytes)
        {
            throw ContentVisualValidation.Error("json_size_exceeded", "$");
        }

        return spec;
    }

    private static ContentVisualSlot[] ValidateSlots(
        IEnumerable<ContentVisualSlot> slots,
        TrustedTemplateDefinition template)
    {
        var copied = ContentVisualValidation.CopyBounded(
            slots,
            ContentVisualLimits.MaximumSlotsPerSpec,
            "slot_count_exceeded",
            "$.slots");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in copied)
        {
            if (slot is null)
                throw ContentVisualValidation.Error("slot_required", "$.slots");
            if (!seen.Add(slot.Name))
                throw ContentVisualValidation.Error("slot_duplicate", $"$.slots.{slot.Name}");
            if (!template.TryGetSlot(slot.Name, out var trustedSlot))
                throw ContentVisualValidation.Error("slot_not_trusted", $"$.slots.{slot.Name}");

            ValidateSlotLimits(slot, trustedSlot);
        }

        foreach (var trustedSlot in template.Slots)
        {
            if (trustedSlot.IsRequired && !seen.Contains(trustedSlot.Name))
            {
                throw ContentVisualValidation.Error(
                    "required_slot_missing",
                    $"$.slots.{trustedSlot.Name}");
            }
        }

        Array.Sort(copied, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return copied;
    }

    private static void ValidateSlotLimits(
        ContentVisualSlot slot,
        TrustedVisualSlotDefinition trustedSlot)
    {
        if (slot.Lines.Count > trustedSlot.MaxLines)
        {
            throw ContentVisualValidation.Error(
                "slot_line_limit_exceeded",
                $"$.slots.{slot.Name}.lines");
        }

        for (var index = 0; index < slot.Lines.Count; index++)
        {
            if (ContentVisualValidation.CountGraphemes(slot.Lines[index])
                > trustedSlot.MaxGraphemesPerLine)
            {
                throw ContentVisualValidation.Error(
                    "line_grapheme_limit_exceeded",
                    $"$.slots.{slot.Name}.lines[{index}]");
            }
        }
    }

    private static ContentThemeTokenBinding[] ValidateThemeTokens(
        IEnumerable<ContentThemeTokenBinding> themeTokens,
        TrustedTemplateDefinition template)
    {
        var copied = ContentVisualValidation.CopyBounded(
            themeTokens,
            ContentVisualLimits.MaximumThemeBindingsPerSpec,
            "theme_binding_count_exceeded",
            "$.themeTokens");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in copied)
        {
            if (binding is null)
                throw ContentVisualValidation.Error("theme_binding_required", "$.themeTokens");
            if (!seen.Add(binding.Name))
            {
                throw ContentVisualValidation.Error(
                    "theme_binding_duplicate",
                    $"$.themeTokens.{binding.Name}");
            }

            if (!template.TryGetTheme(binding.Name, out var trustedBinding))
            {
                throw ContentVisualValidation.Error(
                    "theme_binding_not_trusted",
                    $"$.themeTokens.{binding.Name}");
            }

            if (!trustedBinding.Allows(binding.Token))
            {
                throw ContentVisualValidation.Error(
                    "theme_token_not_trusted_for_binding",
                    $"$.themeTokens.{binding.Name}.token");
            }
        }

        foreach (var trustedBinding in template.ThemeTokens)
        {
            if (trustedBinding.IsRequired && !seen.Contains(trustedBinding.Name))
            {
                throw ContentVisualValidation.Error(
                    "required_theme_binding_missing",
                    $"$.themeTokens.{trustedBinding.Name}");
            }
        }

        Array.Sort(copied, (left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return copied;
    }
}
