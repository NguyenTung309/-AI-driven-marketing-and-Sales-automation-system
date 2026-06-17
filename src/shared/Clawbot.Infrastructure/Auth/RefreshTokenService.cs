using System.Security.Cryptography;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Auth;

public sealed class RefreshTokenService(
    AppDbContext db,
    IClock clock,
    IOptions<RefreshTokenOptions> options) : IRefreshTokenService
{
    private readonly RefreshTokenOptions _opt = options.Value;

    public async Task<(string RawToken, DateTimeOffset ExpiresAt)> IssueAsync(
        Guid userId, string? ip, CancellationToken ct = default)
    {
        // New login = new family (D, AC "mỗi phiên login = 1 family").
        var (entity, raw) = Create(userId, Guid.NewGuid(), ip);
        db.RefreshTokens.Add(entity);
        await db.SaveChangesAsync(ct);
        return (raw, entity.ExpiresAt);
    }

    public async Task<RotateResult> RotateAsync(string rawToken, string? ip, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null) return new RotateResult(RotateOutcome.Invalid, null, default, default, default);

        if (token.RevokedAt is not null)
        {
            // A token that was rotated (has a successor) and is replayed within the grace
            // window is a benign multi-tab F5 race → issue a NEW sibling in the same family
            // instead of treating it as theft (D10). We cannot return the original successor
            // because only the hash is stored.
            var rotatedWithinGrace = token.ReplacedBy is not null
                && token.RevokedAt.Value.AddSeconds(_opt.GraceSeconds) >= now;

            if (rotatedWithinGrace)
            {
                var (sibling, siblingRaw) = Create(token.UserId, token.FamilyId, ip);
                db.RefreshTokens.Add(sibling);
                await db.SaveChangesAsync(ct);
                return new RotateResult(RotateOutcome.Success, siblingRaw, sibling.ExpiresAt, token.UserId, token.FamilyId);
            }

            // Reuse of a revoked token outside the grace window → treat as theft, revoke family.
            await RevokeFamilyAsync(token.FamilyId, now, ct);
            return new RotateResult(RotateOutcome.Reuse, null, default, token.UserId, token.FamilyId);
        }

        if (token.ExpiresAt <= now)
            return new RotateResult(RotateOutcome.Invalid, null, default, token.UserId, token.FamilyId);

        // One-time rotation: mint successor, mark current revoked + replaced_by.
        var (next, nextRaw) = Create(token.UserId, token.FamilyId, ip);
        db.RefreshTokens.Add(next);
        token.RevokedAt = now;
        token.ReplacedBy = next.Id;
        await db.SaveChangesAsync(ct);
        return new RotateResult(RotateOutcome.Success, nextRaw, next.ExpiresAt, token.UserId, token.FamilyId);
    }

    public async Task RevokeAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = Hash(rawToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.RevokedAt == null, ct);
        if (token is null) return; // idempotent
        token.RevokedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        var live = await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in live) token.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken ct)
    {
        // Revoke every live token of the family in one SaveChanges batch (AC reuse-detection).
        // Tracked updates (not ExecuteUpdate) so a stale successor cannot survive in the same
        // unit of work.
        var live = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in live) token.RevokedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private (RefreshToken Entity, string Raw) Create(Guid userId, Guid familyId, string? ip)
    {
        var now = clock.UtcNow;
        var raw = Base64Url(RandomNumberGenerator.GetBytes(32));
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(raw),
            ExpiresAt = now.AddDays(_opt.Days),
            CreatedAt = now,
            CreatedIp = ip,
        };
        return (entity, raw);
    }

    internal static string Hash(string raw)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
