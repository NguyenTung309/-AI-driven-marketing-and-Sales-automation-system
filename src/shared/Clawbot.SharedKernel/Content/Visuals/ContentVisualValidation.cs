using System.Buffers;
using System.Globalization;
using System.Text;

namespace Clawbot.SharedKernel.Content.Visuals;

public static class ContentVisualLimits
{
    public const int MaximumLinesPerSlot = 8;
    public const int MaximumGraphemesPerLine = 120;
    public const int MaximumUtf8BytesPerLine = 4_096;
    public const int MaximumSlotsPerSpec = 32;
    public const int MaximumThemeBindingsPerSpec = 16;
    public const int MaximumTrustedTemplates = 256;
    public const int MaximumJsonUtf8Bytes = 131_072;
}

internal static class ContentVisualValidation
{
    private const int MaximumIdentifierLength = 64;

    internal static string ValidateIdentifier(string? value, string path)
    {
        if (string.IsNullOrEmpty(value) || value.Length > MaximumIdentifierLength)
            throw Error("identifier_invalid", path);

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            var isAlphaNumeric = character is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9';
            var isSeparator = index > 0 && character is '-' or '_' or '.';
            if (!isAlphaNumeric && !isSeparator)
                throw Error("identifier_invalid", path);
        }

        return value;
    }

    internal static string ValidateSha256(string? value, string path)
    {
        if (value is null || value.Length != 64)
            throw Error("template_hash_invalid", path);

        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9')
                && character is not (>= 'a' and <= 'f'))
            {
                throw Error("template_hash_invalid", path);
            }
        }

        return value;
    }

    internal static string NormalizeLine(string? value, int maxGraphemes, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw Error("text_required", path);
        if (value.Length > ContentVisualLimits.MaximumUtf8BytesPerLine)
            throw Error("line_utf8_limit_exceeded", path);

        ValidateUnicodeScalars(value, path);
        if (Encoding.UTF8.GetByteCount(value) > ContentVisualLimits.MaximumUtf8BytesPerLine)
            throw Error("line_utf8_limit_exceeded", path);

        var normalized = value.Normalize(NormalizationForm.FormC);
        if (Encoding.UTF8.GetByteCount(normalized) > ContentVisualLimits.MaximumUtf8BytesPerLine)
            throw Error("line_utf8_limit_exceeded", path);
        if (CountGraphemes(normalized) > maxGraphemes)
            throw Error("line_grapheme_limit_exceeded", path);

        return normalized;
    }

    internal static T[] CopyBounded<T>(
        IEnumerable<T> values,
        int maximumCount,
        string overflowCode,
        string path)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copied = new List<T>(maximumCount);
        foreach (var value in values)
        {
            if (copied.Count == maximumCount)
                throw Error(overflowCode, path);
            copied.Add(value);
        }

        return copied.ToArray();
    }

    internal static int CountGraphemes(string value) =>
        new StringInfo(value).LengthInTextElements;

    internal static ContentVisualContractException Error(string code, string path) =>
        new(code, path);

    private static void ValidateUnicodeScalars(string value, string path)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done)
                throw Error("text_invalid_unicode", path);
            if (IsForbiddenControl(rune.Value))
                throw Error("text_control_not_allowed", path);

            remaining = remaining[consumed..];
        }
    }

    private static bool IsForbiddenControl(int value) =>
        value is >= 0x0000 and <= 0x001f
            or >= 0x007f and <= 0x009f
            or 0x061c
            or 0x200e
            or 0x200f
            or 0x2028
            or 0x2029
            or >= 0x202a and <= 0x202e
            or >= 0x2066 and <= 0x2069;
}
