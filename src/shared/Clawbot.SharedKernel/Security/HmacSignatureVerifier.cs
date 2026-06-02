using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Clawbot.SharedKernel.Security;

public static class HmacSignatureVerifier
{
    public static bool VerifyHexSha256(string rawBody, string providedSignature, string secret)
    {
        if (string.IsNullOrEmpty(rawBody)) return false;
        if (string.IsNullOrEmpty(providedSignature)) return false;
        if (string.IsNullOrEmpty(secret)) return false;

        var trimmed = StripPrefix(providedSignature);
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);
        var keyBytes = Encoding.UTF8.GetBytes(secret);

        using var hmac = new HMACSHA256(keyBytes);
        var expected = hmac.ComputeHash(bodyBytes);
        var expectedHex = ToHex(expected);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expectedHex),
            Encoding.ASCII.GetBytes(trimmed));
    }

    public static bool VerifyBase64Sha256(string rawBody, string providedSignature, string secret)
    {
        if (string.IsNullOrEmpty(rawBody)) return false;
        if (string.IsNullOrEmpty(providedSignature)) return false;
        if (string.IsNullOrEmpty(secret)) return false;

        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);
        var keyBytes = Encoding.UTF8.GetBytes(secret);

        using var hmac = new HMACSHA256(keyBytes);
        var expected = hmac.ComputeHash(bodyBytes);
        var providedBytes = TryDecodeBase64(providedSignature);
        if (providedBytes is null) return false;
        return CryptographicOperations.FixedTimeEquals(expected, providedBytes);
    }

    private static string StripPrefix(string signature)
    {
        var idx = signature.IndexOf('=', StringComparison.Ordinal);
        return idx >= 0 && idx < signature.Length - 1 ? signature[(idx + 1)..] : signature;
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    private static byte[]? TryDecodeBase64(string s)
    {
        try { return Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }
}
