namespace Clawbot.Infrastructure.Auth;

public sealed class RefreshTokenOptions
{
    // SPEC-11 §7: refresh token lifetime.
    public int Days { get; set; } = 7;

    // SPEC-11 D10: sibling-rotation grace window for multi-tab F5 races.
    public int GraceSeconds { get; set; } = 10;
}
