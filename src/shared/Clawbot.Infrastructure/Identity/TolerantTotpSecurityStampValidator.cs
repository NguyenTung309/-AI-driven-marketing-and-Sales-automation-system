using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;

namespace Clawbot.Infrastructure.Identity;

/// <summary>
/// Custom TOTP token provider with extended time window tolerance to handle clock drift
/// between client and server (±2 minutes instead of default ±30 seconds).
/// </summary>
public sealed class TolerantTotpTokenProvider<TUser> : IUserTwoFactorTokenProvider<TUser>
    where TUser : class
{
    // Default: 1 step = ±30s window. Tolerant: 4 steps = ±2 minutes to handle clock skew.
    private const int TolerantStepWindow = 4;
    private const int StepSeconds = 30;
    private const int TokenLength = 6;

    public Task<bool> CanGenerateTwoFactorTokenAsync(
        UserManager<TUser> manager,
        TUser user)
    {
        return Task.FromResult(true);
    }

    public async Task<string> GenerateAsync(
        string purpose,
        UserManager<TUser> manager,
        TUser user)
    {
        var securityToken = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(securityToken))
            throw new InvalidOperationException("No authenticator key set for user.");

        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var timestep = unixTimestamp / StepSeconds;
        return GenerateTotp(securityToken, timestep);
    }

    public async Task<bool> ValidateAsync(
        string purpose,
        string token,
        UserManager<TUser> manager,
        TUser user)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length != TokenLength)
            return false;

        var securityToken = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(securityToken))
            return false;

        var unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentStep = unixTimestamp / StepSeconds;

        // Check current step and ±TolerantStepWindow around it to tolerate clock drift.
        for (var offset = -TolerantStepWindow; offset <= TolerantStepWindow; offset++)
        {
            var candidate = GenerateTotp(securityToken, currentStep + offset);
            if (string.Equals(candidate, token, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string GenerateTotp(string key, long timestep)
    {
        var keyBytes = Base32Decode(key);
        var timestepBytes = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(timestepBytes);

        // TOTP standard (RFC 6238) requires HMACSHA1 for compatibility with authenticator apps.
        // CA5350 suppressed: HMACSHA1 is mandated by the TOTP spec, not a weakness here.
#pragma warning disable CA5350
        using var hmac = new HMACSHA1(keyBytes);
#pragma warning restore CA5350
        var hash = hmac.ComputeHash(timestepBytes);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        var otp = binary % 1_000_000;
        return otp.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static byte[] Base32Decode(string encoded)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var c in encoded.ToUpperInvariant())
        {
            if (c == '=') break;
            var index = alphabet.IndexOf(c);
            if (index < 0) throw new ArgumentException("Invalid Base32 character.", nameof(encoded));

            value = (value << 5) | index;
            bits += 5;

            if (bits >= 8)
            {
                output.Add((byte)(value >> (bits - 8)));
                bits -= 8;
            }
        }

        return output.ToArray();
    }
}
