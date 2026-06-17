using System.IdentityModel.Tokens.Jwt;
using Clawbot.Api.Auth;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Clawbot.Api.Tests;

// M02 / SPEC-11 — JwtTokenIssuer claim + expiry issuance.
public sealed class JwtTokenIssuerTests
{
    private static JwtTokenIssuer Build(int minutes = 15) =>
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
        var roleId = Guid.NewGuid();

        var (token, _) = Build().Issue(userId, tenantId, "demo", roleId);

        var jwt = Decode(token);
        jwt.Issuer.Should().Be("clawbot");
        jwt.Claims.Should().Contain(c => c.Type == JwtRegisteredClaimNames.Sub && c.Value == userId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "tenant_id" && c.Value == tenantId.ToString());
        jwt.Claims.Should().Contain(c => c.Type == "tenant_slug" && c.Value == "demo");
        jwt.Claims.Should().Contain(c => c.Type == "role_id" && c.Value == roleId.ToString());
    }

    [Fact]
    public void Does_not_emit_perm_or_role_claims()
    {
        var (token, _) = Build().Issue(Guid.NewGuid(), Guid.NewGuid(), "demo", Guid.NewGuid());

        var jwt = Decode(token);
        // SPEC-11 D3: permissions are resolved at runtime, not frozen into the token.
        jwt.Claims.Should().NotContain(c => c.Type == "perm");
        jwt.Claims.Should().NotContain(c => c.Type == "role" || c.Type == "roles");
    }

    [Fact]
    public void Expiry_reflects_configured_minutes()
    {
        var before = DateTimeOffset.UtcNow;

        var (_, expiresAt) = Build(minutes: 30)
            .Issue(Guid.NewGuid(), Guid.NewGuid(), "demo", Guid.NewGuid());

        expiresAt.Should().BeCloseTo(before.AddMinutes(30), TimeSpan.FromMinutes(1));
    }
}
