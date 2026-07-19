using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Clawbot.Api.Common.Pagination;

/// <summary>
/// Base64Url JSON cursor for keyset pagination with BIGINT ids: { "ts": DateTimeOffset, "id": long }.
/// </summary>
public static class LongCursorCodec
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Encode(DateTimeOffset ts, long id)
    {
        var payload = JsonSerializer.Serialize(new LongCursorPayload(ts, id), JsonOptions);
        return Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
    }

    public static LongCursorKey? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;

        try
        {
            var bytes = Base64UrlDecode(cursor.Trim());
            var payload = JsonSerializer.Deserialize<LongCursorPayload>(bytes, JsonOptions);
            if (payload is null || payload.Id <= 0)
                return null;
            return new LongCursorKey(payload.Ts, payload.Id);
        }
        catch
        {
            return null;
        }
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

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

    private sealed record LongCursorPayload(
        [property: JsonPropertyName("ts")] DateTimeOffset Ts,
        [property: JsonPropertyName("id")] long Id);
}

public readonly record struct LongCursorKey(DateTimeOffset Ts, long Id);
