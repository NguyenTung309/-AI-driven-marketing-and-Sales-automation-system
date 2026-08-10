using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace Clawbot.Agents.Core.Orchestrator;

// Planner LLMs return version as int (1), float ("1.0"), or string ("1.0"). The field is metadata only,
// so accept any of those forms and fall back to 1 rather than failing the whole plan parse on it.
internal sealed class TolerantVersionConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                if (reader.TryGetDouble(out var d)) return (int)d;
                return 1;
            case JsonTokenType.String:
                var raw = reader.GetString();
                if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                    return (int)parsed;
                return 1;
            default:
                return 1;
        }
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}

// Planner LLMs put rich values in task input (numbers, arrays, nested objects), but the plan model holds
// input as string→string. Coerce every value to a string (raw JSON for non-strings) instead of failing the
// whole parse, so a structurally-sound plan survives.
internal sealed class TolerantStringDictionaryConverter : JsonConverter<IReadOnlyDictionary<string, string>>
{
    public override IReadOnlyDictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new Dictionary<string, string>();
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            reader.Skip();
            return result;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            reader.Read();
            result[key] = reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString() ?? string.Empty,
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => string.Empty,
                JsonTokenType.Number => reader.GetRawValue(),
                _ => JsonDocument.ParseValue(ref reader).RootElement.GetRawText(),
            };
        }

        return result;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, val) in value)
            writer.WriteString(key, val);
        writer.WriteEndObject();
    }
}

// Planner LLMs sometimes emit task ids / dependsOn entries as numbers (1, 2) instead of strings ("t1").
// The id is an opaque handle, so coerce scalars to their textual form instead of failing the parse.
internal sealed class TolerantStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString() ?? string.Empty,
            JsonTokenType.Number => reader.GetRawValue(),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => string.Empty,
            _ => throw new JsonException("Expected a scalar value."),
        };

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}

internal sealed class TolerantStringListConverter : JsonConverter<IReadOnlyList<string>>
{
    private static readonly TolerantStringConverter Scalar = new();

    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var result = new List<string>();
        if (reader.TokenType != JsonTokenType.StartArray)
        {
            // Scalar đơn lẻ thay vì mảng -> coi như mảng 1 phần tử.
            result.Add(Scalar.Read(ref reader, typeof(string), options));
            return result;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            result.Add(Scalar.Read(ref reader, typeof(string), options));
        return result;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
            writer.WriteStringValue(item);
        writer.WriteEndArray();
    }
}

internal static class Utf8JsonReaderExtensions
{
    // Read a Number token's exact textual form without losing precision (e.g. 30, 1.5, 1e3).
    public static string GetRawValue(this ref Utf8JsonReader reader) =>
        System.Text.Encoding.UTF8.GetString(reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan);
}

public sealed record OrchestrationPlanTask(
    [property: JsonConverter(typeof(TolerantStringConverter))] string Id,
    string Agent,
    string Description,
    [property: JsonConverter(typeof(TolerantStringDictionaryConverter))] IReadOnlyDictionary<string, string> Input,
    [property: JsonConverter(typeof(TolerantStringListConverter))] IReadOnlyList<string> DependsOn,
    string Status,
    string? Output,
    string? Error,
    // Planner sinh system prompt rieng cho sub-agent tuy task (tuy chon). Null -> agent dung prompt mac dinh.
    string? RoleInstruction = null)
{
    // System.Text.Json gán null khi LLM bỏ hẳn một property record positional, dù property đó non-nullable.
    // Chuẩn hóa ngay ở biên JSON để validator và wave scheduler luôn nhận collection an toàn.
    public OrchestrationPlanTask Normalize() => this with
    {
        Id = Id ?? string.Empty,
        Agent = Agent ?? string.Empty,
        Description = Description ?? string.Empty,
        Input = Input ?? new Dictionary<string, string>(),
        DependsOn = DependsOn ?? Array.Empty<string>(),
        Status = Status ?? string.Empty,
    };
}

public sealed record OrchestrationPlanDocument(
    [property: JsonConverter(typeof(TolerantVersionConverter))] int Version,
    IReadOnlyList<OrchestrationPlanTask> Tasks)
{
    public OrchestrationPlanDocument Normalize() => this with
    {
        Tasks = (Tasks ?? Array.Empty<OrchestrationPlanTask>())
            .Where(task => task is not null)
            .Select(task => task.Normalize())
            .ToArray(),
    };

    public OrchestrationPlanDocument WithTaskStatus(string taskId, string status, string? output, string? error) =>
        this with
        {
            Tasks = (Tasks ?? Array.Empty<OrchestrationPlanTask>())
                .Select(task => task.Id == taskId
                    ? task with { Status = status, Output = output, Error = error }
                    : task)
                .ToArray(),
        };
}

public sealed record OrchestrationPlanValidationResult(bool IsValid, string? Error)
{
    public static OrchestrationPlanValidationResult Valid { get; } = new(true, null);
    public static OrchestrationPlanValidationResult Invalid(string error) => new(false, error);
}
