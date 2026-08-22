using Clawbot.SharedKernel.Security;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Security;

public sealed class AgentServiceAuthenticationOptionsTests
{
    private static string ValidKey(int bytes = 32) =>
        Convert.ToBase64String(Enumerable.Range(0, bytes).Select(i => (byte)i).ToArray());

    [Fact]
    public void Defaults_AreCorrect()
    {
        var options = new AgentServiceAuthenticationOptions();

        options.SigningKey.Should().BeEmpty();
        options.TokenLifetimeMinutes.Should().Be(2);
    }

    [Fact]
    public void Constants_MatchTokenContract()
    {
        AgentServiceAuthenticationOptions.SectionName.Should().Be("AgentServiceAuthentication");
        AgentServiceAuthenticationOptions.Issuer.Should().Be("clawbot-api");
        AgentServiceAuthenticationOptions.Audience.Should().Be("clawbot-agent-service");
        AgentServiceAuthenticationOptions.MinimumSigningKeyBytes.Should().Be(32);
    }

    [Fact]
    public void GetSigningKeyBytes_ValidBase64_ReturnsDecodedBytes()
    {
        var bytes = AgentServiceAuthenticationOptions.GetSigningKeyBytes(ValidKey());

        bytes.Should().HaveCount(32);
    }

    [Fact]
    public void GetSigningKeyBytes_TrimsWhitespace()
    {
        var bytes = AgentServiceAuthenticationOptions.GetSigningKeyBytes($"  {ValidKey()}  ");

        bytes.Should().HaveCount(32);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetSigningKeyBytes_MissingKey_Throws(string? key)
    {
        var act = () => AgentServiceAuthenticationOptions.GetSigningKeyBytes(key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_required");
    }

    [Fact]
    public void GetSigningKeyBytes_TooShort_Throws()
    {
        var act = () => AgentServiceAuthenticationOptions.GetSigningKeyBytes(ValidKey(16));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_invalid");
    }

    [Fact]
    public void GetSigningKeyBytes_NotBase64_Throws()
    {
        var act = () => AgentServiceAuthenticationOptions.GetSigningKeyBytes("not base64 !!!");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_invalid");
    }

    [Fact]
    public void EnsureGrpcTransportSecurity_Development_SkipsAllChecks()
    {
        var act = () => AgentServiceAuthenticationOptions.EnsureGrpcTransportSecurity(
            "http://localhost:5001",
            certificatePath: null,
            isDevelopment: true);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureGrpcTransportSecurity_HttpsWithCertificate_Passes()
    {
        var act = () => AgentServiceAuthenticationOptions.EnsureGrpcTransportSecurity(
            "https://agents.internal:5001",
            "/etc/ssl/agents.pfx",
            isDevelopment: false);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("http://agents.internal:5001")]
    [InlineData("not-a-url")]
    [InlineData(null)]
    public void EnsureGrpcTransportSecurity_NonHttpsEndpoint_Throws(string? endpoint)
    {
        var act = () => AgentServiceAuthenticationOptions.EnsureGrpcTransportSecurity(
            endpoint,
            "/etc/ssl/agents.pfx",
            isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_https_required");
    }

    [Fact]
    public void EnsureGrpcTransportSecurity_MissingCertificate_Throws()
    {
        var act = () => AgentServiceAuthenticationOptions.EnsureGrpcTransportSecurity(
            "https://agents.internal:5001",
            certificatePath: "  ",
            isDevelopment: false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_tls_certificate_required");
    }

    [Fact]
    public void EnsureDistinctFromPublicJwtKey_NoPublicKey_Passes()
    {
        var act = () => AgentServiceAuthenticationOptions.EnsureDistinctFromPublicJwtKey(
            ValidKey(),
            publicJwtSigningKey: null);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureDistinctFromPublicJwtKey_DifferentKeys_Passes()
    {
        var act = () => AgentServiceAuthenticationOptions.EnsureDistinctFromPublicJwtKey(
            ValidKey(),
            "a-completely-different-public-jwt-signing-key");

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureDistinctFromPublicJwtKey_ReusedKey_Throws()
    {
        // Public JWT key được so sánh dưới dạng UTF8 thô, nên tái dùng đúng chuỗi bytes là vi phạm.
        var shared = new string('k', 32);
        var agentKey = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(shared));

        var act = () => AgentServiceAuthenticationOptions.EnsureDistinctFromPublicJwtKey(
            agentKey,
            shared);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_auth_signing_key_reused");
    }
}
