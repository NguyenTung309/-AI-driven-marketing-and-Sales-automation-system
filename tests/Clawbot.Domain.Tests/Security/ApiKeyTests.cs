using Clawbot.Domain.Security;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Security;

public sealed class ApiKeyTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Issue_SetsAllFields()
    {
        var key = ApiKey.Issue(TenantId, "test-key", "hash-abc", Now, expiresAt: Now.AddDays(30), scopes: ["read", "write"]);

        key.TenantId.Should().Be(TenantId);
        key.Name.Should().Be("test-key");
        key.KeyHash.Should().Be("hash-abc");
        key.ScopesJson.Should().Contain("read").And.Contain("write");
        key.ExpiresAt.Should().Be(Now.AddDays(30));
        key.RevokedAt.Should().BeNull();
        key.CreatedAt.Should().Be(Now);
        key.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Issue_NullScopes_DefaultsToEmptyArray()
    {
        var key = ApiKey.Issue(TenantId, "k", "h", Now);

        key.ScopesJson.Should().Be("[]");
    }

    [Fact]
    public void Issue_EmptyScopes_DefaultsToEmptyArray()
    {
        var key = ApiKey.Issue(TenantId, "k", "h", Now, scopes: []);

        key.ScopesJson.Should().Be("[]");
    }

    [Fact]
    public void Revoke_SetsRevokedAt()
    {
        var key = ApiKey.Issue(TenantId, "k", "h", Now);

        key.Revoke(Now.AddHours(1));

        key.RevokedAt.Should().Be(Now.AddHours(1));
    }
}
