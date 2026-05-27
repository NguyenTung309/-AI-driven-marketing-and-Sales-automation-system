namespace Clawbot.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "clawbot";
    public string Audience { get; set; } = "clawbot-clients";
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 60;
}
