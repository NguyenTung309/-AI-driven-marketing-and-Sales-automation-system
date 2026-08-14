using System.IdentityModel.Tokens.Jwt;
using Clawbot.Api.Auth;
using Clawbot.Infrastructure.Security;
using Clawbot.SharedKernel.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Tests.Services;

public sealed class AgentServiceTokenIssuerTests
{
    [Fact]
    public void Issue_CreatesShortLivedTokenBoundToCallerIdentity()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var issuer = CreateIssuer();

        var token = new JwtSecurityTokenHandler().ReadJwtToken(issuer.Issue(userId, tenantId, roleId));

        token.Issuer.Should().Be(AgentServiceAuthenticationOptions.Issuer);
        token.Audiences.Should().ContainSingle(AgentServiceAuthenticationOptions.Audience);
        token.Claims.Should().Contain(claim => claim.Type == JwtRegisteredClaimNames.Sub
            && claim.Value == userId.ToString("D"));
        token.Claims.Should().Contain(claim => claim.Type == "tenant_id"
            && claim.Value == tenantId.ToString("D"));
        // role_id travels with the call so the agent service enforces the exact role the API
        // session was issued for, rather than the union of every role the account holds.
        token.Claims.Should().Contain(claim => claim.Type == "role_id"
            && claim.Value == roleId.ToString("D"));
        token.Claims.Should().Contain(claim => claim.Type == "client_id"
            && claim.Value == AgentServiceAuthenticationOptions.ClientId);
        token.Claims.Should().ContainSingle(claim => claim.Type == JwtRegisteredClaimNames.Jti);
        token.ValidTo.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(2), TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("role")]
    public void Issue_RejectsEmptyIdentityComponent(string component)
    {
        var issuer = CreateIssuer();
        var userId = component == "user" ? Guid.Empty : Guid.NewGuid();
        var tenantId = component == "tenant" ? Guid.Empty : Guid.NewGuid();
        var roleId = component == "role" ? Guid.Empty : Guid.NewGuid();

        var action = () => issuer.Issue(userId, tenantId, roleId);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IssueServiceToken_RejectsEmptyTenantId()
    {
        var issuer = CreateIssuer();

        var action = () => issuer.IssueServiceToken(Guid.Empty);

        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EnsureGrpcTransportSecurity_RejectsCleartextOutsideDevelopment()
    {
        var action = () => AgentServiceAuthenticationOptions.EnsureGrpcTransportSecurity(
            "http://agentservice:15875",
            "/run/secrets/agentservice-grpc.pfx",
            isDevelopment: false);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_https_required");
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("c2hvcnQ=")]
    public void Issue_RejectsInvalidDedicatedSigningKey(string signingKey)
    {
        var issuer = new AgentServiceTokenIssuer(Options.Create(
            new AgentServiceAuthenticationOptions { SigningKey = signingKey }));

        var action = () => issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_invalid");
    }

    [Fact]
    public void EnsureSigningKeyIsDistinct_RejectsPublicJwtSigningKeyMaterialReuse()
    {
        var publicJwtKey = new string('A', 64);
        var signingKey = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(publicJwtKey));

        var action = () => AgentServiceAuthenticationOptions.EnsureDistinctFromPublicJwtKey(
            signingKey,
            publicJwtKey);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_reused");
    }

    [Fact]
    public void Issue_RejectsMissingDedicatedSigningKey()
    {
        var issuer = new AgentServiceTokenIssuer(Options.Create(
            new AgentServiceAuthenticationOptions { SigningKey = " " }));

        var action = () => issuer.Issue(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_required");
    }

    private static AgentServiceTokenIssuer CreateIssuer() =>
        new(Options.Create(new AgentServiceAuthenticationOptions
        {
            SigningKey = CreateSigningKey(),
            TokenLifetimeMinutes = 2,
        }));

    private static string CreateSigningKey() =>
        Convert.ToBase64String(Enumerable.Repeat((byte)0xA5, 64).ToArray());
}
