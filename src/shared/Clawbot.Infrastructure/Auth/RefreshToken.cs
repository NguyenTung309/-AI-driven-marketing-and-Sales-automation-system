namespace Clawbot.Infrastructure.Auth;

/// <summary>
/// SPEC-11 §6 — persisted refresh token. Only the SHA-256 <see cref="TokenHash"/> is
/// stored, never the raw token. A <see cref="FamilyId"/> groups every token rotated from
/// one login so the whole session can be revoked with a single UPDATE on reuse-detection.
/// Intentionally has no EF navigation/FK to AspNetUsers — the prod DDL owns the FK to
/// users(id); runtime never relies on it.
/// </summary>
public sealed class RefreshToken : Clawbot.Domain.Common.IAuditExempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedIp { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
}
