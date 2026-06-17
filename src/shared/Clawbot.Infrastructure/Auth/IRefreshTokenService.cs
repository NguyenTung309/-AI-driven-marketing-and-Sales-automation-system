namespace Clawbot.Infrastructure.Auth;

public enum RotateOutcome
{
    /// <summary>Token was valid (or a within-grace sibling) — a fresh token was issued.</summary>
    Success,

    /// <summary>A revoked token was replayed outside the grace window — family revoked.</summary>
    Reuse,

    /// <summary>Token not found or expired.</summary>
    Invalid,
}

public readonly record struct RotateResult(
    RotateOutcome Outcome,
    string? RawToken,
    DateTimeOffset ExpiresAt,
    Guid UserId,
    Guid FamilyId);

/// <summary>
/// SPEC-11 §6/§Refresh — issuance, one-time rotation with sibling grace window (D10),
/// reuse-detection family revoke, and full-user revoke (logout / password reset).
/// </summary>
public interface IRefreshTokenService
{
    Task<(string RawToken, DateTimeOffset ExpiresAt)> IssueAsync(Guid userId, string? ip, CancellationToken ct = default);

    Task<RotateResult> RotateAsync(string rawToken, string? ip, CancellationToken ct = default);

    /// <summary>Revoke a single token (logout). Idempotent — unknown/already-revoked is a no-op.</summary>
    Task RevokeAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Revoke every refresh token of a user (password reset / disable).</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}
