using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Clawbot.Api.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Api.Tests;

// M02 — JwtTokenIssuer claim + expiry issuance.
public sealed class JwtTokenIssuerTests
{
    private static JwtTokenIssuer Build(int minutes = 60) =>
        new(Options.Create(new JwtOptions
        {
            Issuer = "clawbot",
            Audience = "clawbot-clients",
            SigningKey = "super-secret-signing-key-of-at-least-32-bytes!!",
            AccessTokenMinutes = minutes,
        }));

    private static JwtSecurityToken Decode(string token) =>
        new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void Issues_token_with_core_claims()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var (token, _) = Build().Issue(userId, tenantId, "demo", new[] { "Admin" });

        var jwt = Decode(token);
        jwt.Issuer.Should().Be("clawbot");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == userId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == tenantId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "tenant_slug" && c.Value == "demo");
        // Role claim type is remapped on write; assert by value to stay robust.
        jwt.Claims.Should().Contain(c => c.Value == "Admin");
    }

    [Fact]
    public void Includes_permission_claims_when_provided()
    {
        var (token, _) = Build().Issue(Guid.NewGuid(), Guid.NewGuid(), "demo",
            new[] { "Admin" }, new[] { "kb:read", "kb:write" });

        var jwt = Decode(token);
        jwt.Claims.Where(c => c.Type == "perm").Select(c => c.Value)
           .Should().BeEquivalentTo("kb:read", "kb:write");
    }

    [Fact]
    public void Omits_permission_claims_when_null()
    {
        var (token, _) = Build().Issue(Guid.NewGuid(), Guid.NewGuid(), "demo", new[] { "Viewer" });

        var jwt = Decode(token);
        jwt.Claims.Should().NotContain(c => c.Type == "perm");
    }

    [Fact]
    public void Expiry_reflects_configured_minutes()
    {
        var before = DateTimeOffset.UtcNow;

        var (_, expiresAt) = Build(minutes: 30)
            .Issue(Guid.NewGuid(), Guid.NewGuid(), "demo", Array.Empty<string>());

        expiresAt.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromMinutes(1));
    }
}
