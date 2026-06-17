using Clawbot.Infrastructure.Auth;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Auth;

// SPEC-11 §Refresh — rotation, reuse-detection, grace-window siblings, family revoke.
public sealed class RefreshTokenServiceTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    private static RefreshTokenService Build(TestAppDb db, TestClock clock, int graceSeconds = 10) =>
        new(db.Db, clock, Options.Create(new RefreshTokenOptions { Days = 7, GraceSeconds = graceSeconds }));

    [Fact]
    public async Task Issue_stores_only_the_hash_not_the_raw_token()
    {
        using var db = new TestAppDb();
        var svc = Build(db, new TestClock());

        var (raw, _) = await svc.IssueAsync(Guid.NewGuid(), "ip");

        var stored = await db.Db.RefreshTokens.SingleAsync();
        stored.TokenHash.Should().NotBe(raw).And.HaveLength(64); // SHA-256 hex
    }

    [Fact]
    public async Task Rotate_is_one_time_revoking_and_replacing_the_old_token()
    {
        using var db = new TestAppDb();
        var clock = new TestClock();
        var svc = Build(db, clock);
        var userId = Guid.NewGuid();
        var (raw, _) = await svc.IssueAsync(userId, "ip");

        var result = await svc.RotateAsync(raw, "ip");

        result.Outcome.Should().Be(RotateOutcome.Success);
        result.RawToken.Should().NotBeNullOrEmpty().And.NotBe(raw);
        result.UserId.Should().Be(userId);

        // Sort client-side: SQLite cannot ORDER BY DateTimeOffset.
        var tokens = (await db.Db.RefreshTokens.ToListAsync()).OrderBy(t => t.CreatedAt).ToList();
        tokens.Should().HaveCount(2);
        var original = tokens.Single(t => t.RevokedAt != null);
        var successor = tokens.Single(t => t.RevokedAt == null);
        original.ReplacedBy.Should().Be(successor.Id);
        successor.FamilyId.Should().Be(original.FamilyId); // successor inherits family
    }

    [Fact]
    public async Task Reusing_a_rotated_token_within_grace_issues_a_sibling_not_theft()
    {
        using var db = new TestAppDb();
        var clock = new TestClock();
        var svc = Build(db, clock, graceSeconds: 10);
        var (raw, _) = await svc.IssueAsync(Guid.NewGuid(), "ip");
        var first = await svc.RotateAsync(raw, "ip"); // T0 -> T1

        clock.UtcNow = clock.UtcNow.AddSeconds(5); // still within grace
        var second = await svc.RotateAsync(raw, "ip"); // late multi-tab caller reuses T0

        second.Outcome.Should().Be(RotateOutcome.Success);
        second.RawToken.Should().NotBe(first.RawToken);
        // No theft → the first successor (T1) is still usable.
        (await svc.RotateAsync(first.RawToken!, "ip")).Outcome.Should().Be(RotateOutcome.Success);
    }

    [Fact]
    public async Task Reusing_a_revoked_token_outside_grace_revokes_the_whole_family()
    {
        using var db = new TestAppDb();
        var clock = new TestClock();
        var svc = Build(db, clock, graceSeconds: 10);
        var userId = Guid.NewGuid();
        var (raw, _) = await svc.IssueAsync(userId, "ip");
        var rotated = await svc.RotateAsync(raw, "ip"); // T0 -> T1

        clock.UtcNow = clock.UtcNow.AddSeconds(30); // outside grace
        var reuse = await svc.RotateAsync(raw, "ip"); // replay the dead T0

        reuse.Outcome.Should().Be(RotateOutcome.Reuse);
        // Family revoked → the live successor T1 is now dead too.
        (await svc.RotateAsync(rotated.RawToken!, "ip")).Outcome.Should().Be(RotateOutcome.Reuse);
        (await db.Db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null))
            .Should().Be(0);
    }

    [Fact]
    public async Task Expired_token_is_invalid()
    {
        using var db = new TestAppDb();
        var clock = new TestClock();
        var svc = Build(db, clock);
        var (raw, _) = await svc.IssueAsync(Guid.NewGuid(), "ip");

        clock.UtcNow = clock.UtcNow.AddDays(8); // past 7-day TTL

        (await svc.RotateAsync(raw, "ip")).Outcome.Should().Be(RotateOutcome.Invalid);
    }

    [Fact]
    public async Task Unknown_token_is_invalid()
    {
        using var db = new TestAppDb();
        var svc = Build(db, new TestClock());

        (await svc.RotateAsync("not-a-real-token", "ip")).Outcome.Should().Be(RotateOutcome.Invalid);
    }

    [Fact]
    public async Task RevokeAllForUser_kills_every_live_token()
    {
        using var db = new TestAppDb();
        var svc = Build(db, new TestClock());
        var userId = Guid.NewGuid();
        await svc.IssueAsync(userId, "ip");
        await svc.IssueAsync(userId, "ip");

        await svc.RevokeAllForUserAsync(userId);

        (await db.Db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAt == null))
            .Should().Be(0);
    }

    [Fact]
    public async Task Logout_revoke_is_idempotent()
    {
        using var db = new TestAppDb();
        var svc = Build(db, new TestClock());
        var (raw, _) = await svc.IssueAsync(Guid.NewGuid(), "ip");

        await svc.RevokeAsync(raw);
        await svc.RevokeAsync(raw); // second call is a no-op, must not throw

        (await db.Db.RefreshTokens.SingleAsync()).RevokedAt.Should().NotBeNull();
    }
}
