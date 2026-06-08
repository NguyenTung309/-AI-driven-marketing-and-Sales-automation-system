namespace Clawbot.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "clawbot";
    public string Audience { get; set; } = "clawbot-clients";
    public string SigningKey { get; set; } = string.Empty;

    // SPEC-11 §7: access token short-lived (15m) now that refresh tokens exist.
    public int AccessTokenMinutes { get; set; } = 15;

    // SPEC-11 §7: refresh token lifetime (days).
    public int RefreshTokenDays { get; set; } = 7;

    // SPEC-11 D10/§Gateway: explicit clock skew, kept identical on Gateway + backend.
    public int ClockSkewSeconds { get; set; } = 30;
}


