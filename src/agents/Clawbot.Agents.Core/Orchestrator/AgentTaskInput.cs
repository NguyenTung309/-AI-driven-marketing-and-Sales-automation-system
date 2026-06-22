using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.SaleAssist;

namespace Clawbot.Agents.Core.Orchestrator;

public static class AgentTaskInput
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Guid RequiredGuid(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || !Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            throw new ArgumentException($"{key} must be a valid GUID.");

        return parsed;
    }

    public static Guid? OptionalGuid(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        if (!Guid.TryParse(value, out var parsed) || parsed == Guid.Empty)
            throw new ArgumentException($"{key} must be a valid GUID.");

        return parsed;
    }

    public static string RequiredString(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{key} is required.");

        return value.Trim();
    }

    public static string? OptionalString(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    public static IReadOnlyList<string> StringList(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return [];

        var trimmed = value.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(trimmed, JsonOptions)?
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Select(item => item.Trim())
                    .ToArray() ?? [];
            }
            catch (JsonException ex)
            {
                throw new ArgumentException($"{key} must be a JSON string array or comma-separated list.", ex);
            }
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => item.Length > 0)
            .ToArray();
    }

    public static IReadOnlyDictionary<string, string> StringMap(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return new Dictionary<string, string>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(value.Trim(), JsonOptions)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{key} must be a JSON object.", ex);
        }
    }

    public static decimal? OptionalDecimal(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return null;

        if (!decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw new ArgumentException($"{key} must be a decimal number.");

        return parsed;
    }

    public static IReadOnlyList<TurnSnapshot> Turns(IReadOnlyDictionary<string, string> input, string key)
    {
        if (!input.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            return [];

        try
        {
            return JsonSerializer.Deserialize<TurnSnapshot[]>(value.Trim(), JsonOptions) ?? [];
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{key} must be a JSON array of conversation turns.", ex);
        }
    }
}
