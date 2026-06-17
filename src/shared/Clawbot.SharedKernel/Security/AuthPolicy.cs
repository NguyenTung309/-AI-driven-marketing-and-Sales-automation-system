namespace Clawbot.SharedKernel.Security;

/// <summary>
/// SPEC-11 — non-secret auth timing/policy. CODE is the source of truth here (not
/// appsettings): both the API (JwtOptions) and Infrastructure (RefreshTokenOptions, Identity
/// lockout) PostConfigure their options from these constants so a stray appsettings value
/// cannot drift the access-token lifetime, clock skew, refresh window or lockout out of sync.
///
/// The signing key stays in configuration/secret (Constitution: no secrets in source).
/// The Gateway is isolated (ADR-007: zero project references) and cannot read this class —
/// its <c>ClockSkewSeconds</c> must be kept identical in the Gateway's own configuration.
/// </summary>
public static class AuthPolicy
{
    /// <summary>Access token (JWT) lifetime in minutes — short-lived now refresh tokens exist.</summary>
    public const int AccessTokenMinutes = 15;

    /// <summary>JWT clock skew tolerance, identical on backend and Gateway (must match Gateway config).</summary>
    public const int ClockSkewSeconds = 30;

    /// <summary>Refresh token lifetime in days.</summary>
    public const int RefreshTokenDays = 7;

    /// <summary>Sibling-rotation grace window (D10) for benign multi-tab F5 races.</summary>
    public const int RefreshGraceSeconds = 10;

    /// <summary>Consecutive failed logins before lockout.</summary>
    public const int MaxFailedAccessAttempts = 5;

    /// <summary>Lockout duration in minutes after <see cref="MaxFailedAccessAttempts"/> failures.</summary>
    public const int LockoutMinutes = 30;
}
