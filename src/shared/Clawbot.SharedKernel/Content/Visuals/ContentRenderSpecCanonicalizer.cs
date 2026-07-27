using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Clawbot.SharedKernel.Content.Visuals;

public static class ContentRenderSpecCanonicalizer
{
    public static string ToCanonicalJson(ContentRenderSpec spec) =>
        Encoding.UTF8.GetString(GetCanonicalUtf8(spec));

    public static string ToCanonicalSlotsJson(IReadOnlyList<ContentVisualSlot> slots) =>
        Encoding.UTF8.GetString(GetCanonicalSlotsUtf8(slots));

    public static byte[] GetCanonicalUtf8(ContentRenderSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", spec.SchemaVersion);
            writer.WriteString("preset", spec.Preset.Token);

            writer.WriteStartObject("template");
            writer.WriteString("id", spec.Template.TemplateId);
            writer.WriteNumber("version", spec.Template.Version);
            writer.WriteString("sha256", spec.Template.Sha256);
            writer.WriteEndObject();

            writer.WriteStartArray("slots");
            WriteSlotItems(writer, spec.Slots);
            writer.WriteEndArray();
            writer.WriteStartArray("themeTokens");
            foreach (var binding in spec.ThemeTokens.OrderBy(
                         binding => binding.Name,
                         StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("name", binding.Name);
                writer.WriteString("token", binding.Token);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return buffer.WrittenSpan.ToArray();
    }

    public static byte[] GetCanonicalSlotsUtf8(IReadOnlyList<ContentVisualSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var validatedSlots = ContentVisualSlotCollection.Validate(slots);
        return GetCanonicalSlotsUtf8Validated(validatedSlots);
    }

    internal static byte[] GetCanonicalSlotsUtf8Validated(
        IReadOnlyList<ContentVisualSlot> slots)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            WriteSlotItems(writer, slots);
            writer.WriteEndArray();
            writer.Flush();
        }

        if (buffer.WrittenCount > ContentVisualLimits.MaximumJsonUtf8Bytes)
            throw ContentVisualValidation.Error("json_size_exceeded", "$");

        return buffer.WrittenSpan.ToArray();
    }

    public static string ComputeSha256(ContentRenderSpec spec)
    {
        var hash = SHA256.HashData(GetCanonicalUtf8(spec));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string ComputeSlotsSha256(IReadOnlyList<ContentVisualSlot> slots)
    {
        var hash = SHA256.HashData(GetCanonicalSlotsUtf8(slots));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void WriteSlotItems(
        Utf8JsonWriter writer,
        IEnumerable<ContentVisualSlot> slots)
    {
        foreach (var slot in slots.OrderBy(slot => slot.Name, StringComparer.Ordinal))
        {
            writer.WriteStartObject();
            writer.WriteString("name", slot.Name);
            writer.WriteStartArray("lines");
            foreach (var line in slot.Lines)
                writer.WriteStringValue(line);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
