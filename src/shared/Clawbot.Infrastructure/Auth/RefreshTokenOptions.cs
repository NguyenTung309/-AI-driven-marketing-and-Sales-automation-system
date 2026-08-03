namespace Clawbot.Infrastructure.Auth;

public sealed class RefreshTokenOptions
{
    public int Days { get; set; } = 7;

    public int GraceSeconds { get; set; } = 10;
}
