using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawbot.Api.Common.Pagination;

/// <summary>
/// Base64Url JSON cursor for keyset pagination: { "ts": DateTimeOffset, "id": Guid }.
/// Decode failures are treated as first-page (null cursor) — never throw to clients.
/// </summary>
public static class CursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(DateTimeOffset ts, Guid id)
    {
        var payload = JsonSerializer.Serialize(new CursorPayload(ts, id), JsonOptions);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>Returns null when cursor is missing or invalid (tamper / corrupt).</summary>
    public static CursorKey? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var bytes = Base64UrlDecode(cursor.Trim());
            var payload = JsonSerializer.Deserialize<CursorPayload>(bytes, JsonOptions);
            if (payload is null || payload.Id == Guid.Empty)
                return null;
            return new CursorKey(payload.Ts, payload.Id);
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    private sealed record CursorPayload(
        [property: JsonPropertyName("ts")] DateTimeOffset Ts,
        [property: JsonPropertyName("id")] Guid Id);
}

public readonly record struct CursorKey(DateTimeOffset Ts, Guid Id);
