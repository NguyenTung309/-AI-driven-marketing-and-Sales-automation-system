using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace Clawbot.SharedKernel.Content.Visuals;

public static class ContentRenderSpecJson
{
    private static readonly FrozenSet<string> RootMembers =
        new[] { "schemaVersion", "preset", "template", "slots", "themeTokens" }
            .ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> TemplateMembers =
        new[] { "id", "version", "sha256" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SlotMembers =
        new[] { "name", "lines" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ThemeMembers =
        new[] { "name", "token" }.ToFrozenSet(StringComparer.Ordinal);

    public static ContentRenderSpec Parse(string json, ITrustedTemplateCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        using var document = ParseDocument(json);
        return ParseRoot(document.RootElement, catalog);
    }

    public static IReadOnlyList<ContentVisualSlot> ParseSlots(string json)
    {
        using var document = ParseDocument(json);
        var slots = ParseSlots(document.RootElement);
        _ = ContentRenderSpecCanonicalizer.GetCanonicalSlotsUtf8Validated(slots);
        return slots;
    }

    private static JsonDocument ParseDocument(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw ContentVisualValidation.Error("invalid_json", "$");
        if (Encoding.UTF8.GetByteCount(json) > ContentVisualLimits.MaximumJsonUtf8Bytes)
            throw ContentVisualValidation.Error("json_size_exceeded", "$");

        try
        {
            return JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                });
        }
        catch (JsonException)
        {
            throw ContentVisualValidation.Error("invalid_json", "$");
        }
        catch (ArgumentException)
        {
            throw ContentVisualValidation.Error("invalid_json", "$");
        }
        catch (InvalidOperationException)
        {
            throw ContentVisualValidation.Error("invalid_json", "$");
        }
    }

    private static ContentRenderSpec ParseRoot(
        JsonElement root,
        ITrustedTemplateCatalog catalog)
    {
        var members = ReadClosedObject(root, RootMembers, "$");
        var schemaVersion = ReadInt32(Required(members, "schemaVersion", "$"), "$.schemaVersion");
        if (schemaVersion != ContentRenderSpec.CurrentSchemaVersion)
            throw ContentVisualValidation.Error("schema_version_unsupported", "$.schemaVersion");

        var preset = ContentVisualPreset.Parse(
            ReadString(Required(members, "preset", "$"), "$.preset"),
            "$.preset");
        var template = ParseTemplate(Required(members, "template", "$"));
        var slots = ParseSlots(Required(members, "slots", "$"));
        var themeTokens = ParseThemes(Required(members, "themeTokens", "$"));
        return ContentRenderSpec.Create(catalog, template, preset, slots, themeTokens);
    }

    private static TrustedTemplateReference ParseTemplate(JsonElement element)
    {
        const string path = "$.template";
        var members = ReadClosedObject(element, TemplateMembers, path);
        var templateId = ReadString(Required(members, "id", path), $"{path}.id");
        var version = ReadInt32(Required(members, "version", path), $"{path}.version");
        var sha256 = ReadString(Required(members, "sha256", path), $"{path}.sha256");
        return TrustedTemplateReference.Create(templateId, version, sha256, path);
    }

    private static System.Collections.ObjectModel.ReadOnlyCollection<ContentVisualSlot> ParseSlots(
        JsonElement element)
    {
        const string path = "$.slots";
        RequireKind(element, JsonValueKind.Array, path);
        if (element.GetArrayLength() > ContentVisualLimits.MaximumSlotsPerSpec)
            throw ContentVisualValidation.Error("slot_count_exceeded", path);

        var slots = new List<ContentVisualSlot>(element.GetArrayLength());
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            var members = ReadClosedObject(item, SlotMembers, itemPath);
            var name = ReadString(Required(members, "name", itemPath), $"{itemPath}.name");
            var lines = ReadLines(Required(members, "lines", itemPath), $"{itemPath}.lines");
            slots.Add(ContentVisualSlot.Create(name, lines, itemPath));
            index++;
        }

        return ContentVisualSlotCollection.Validate(slots);
    }

    private static List<string> ReadLines(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.Array, path);
        if (element.GetArrayLength() == 0
            || element.GetArrayLength() > ContentVisualLimits.MaximumLinesPerSlot)
        {
            throw ContentVisualValidation.Error("slot_line_limit_exceeded", path);
        }

        var lines = new List<string>(element.GetArrayLength());
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            lines.Add(ReadString(item, $"{path}[{index}]")!);
            index++;
        }

        return lines;
    }

    private static List<ContentThemeTokenBinding> ParseThemes(JsonElement element)
    {
        const string path = "$.themeTokens";
        RequireKind(element, JsonValueKind.Array, path);
        if (element.GetArrayLength() > ContentVisualLimits.MaximumThemeBindingsPerSpec)
            throw ContentVisualValidation.Error("theme_binding_count_exceeded", path);

        var bindings = new List<ContentThemeTokenBinding>(element.GetArrayLength());
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            var itemPath = $"{path}[{index}]";
            var members = ReadClosedObject(item, ThemeMembers, itemPath);
            var name = ReadString(Required(members, "name", itemPath), $"{itemPath}.name");
            var token = ReadString(Required(members, "token", itemPath), $"{itemPath}.token");
            bindings.Add(ContentThemeTokenBinding.Create(name, token, itemPath));
            index++;
        }

        return bindings;
    }

    private static Dictionary<string, JsonElement> ReadClosedObject(
        JsonElement element,
        FrozenSet<string> allowedMembers,
        string path)
    {
        RequireKind(element, JsonValueKind.Object, path);
        var members = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            var propertyName = ReadPropertyName(property, path);
            if (!allowedMembers.Contains(propertyName))
                throw ContentVisualValidation.Error("unknown_member", $"{path}.*");
            if (!members.TryAdd(propertyName, property.Value))
            {
                throw ContentVisualValidation.Error(
                    "duplicate_member",
                    $"{path}.{propertyName}");
            }
        }

        return members;
    }

    private static string ReadPropertyName(JsonProperty property, string path)
    {
        try
        {
            return property.Name;
        }
        catch (InvalidOperationException)
        {
            throw ContentVisualValidation.Error("invalid_json", path);
        }
    }

    private static JsonElement Required(
        Dictionary<string, JsonElement> members,
        string name,
        string path)
    {
        if (!members.TryGetValue(name, out var value))
            throw ContentVisualValidation.Error("required_member_missing", $"{path}.{name}");
        return value;
    }

    private static string ReadString(JsonElement element, string path)
    {
        RequireKind(element, JsonValueKind.String, path);
        try
        {
            return element.GetString()
                ?? throw ContentVisualValidation.Error("member_type_invalid", path);
        }
        catch (InvalidOperationException)
        {
            throw ContentVisualValidation.Error("invalid_json", "$");
        }
    }

    private static int ReadInt32(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var value))
            throw ContentVisualValidation.Error("member_type_invalid", path);
        return value;
    }

    private static void RequireKind(JsonElement element, JsonValueKind expected, string path)
    {
        if (element.ValueKind != expected)
            throw ContentVisualValidation.Error("member_type_invalid", path);
    }
}
