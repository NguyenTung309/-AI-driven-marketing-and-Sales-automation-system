using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Clawbot.Api.Auth;
using FluentAssertions;

namespace Clawbot.Api.Tests.Services;

public sealed class AgentServiceTransportSecurityTests
{
    [Fact]
    public void ValidateConfiguration_RejectsHttpInProduction()
    {
        var action = () => AgentServiceTransportSecurity.ValidateConfiguration(
            new Uri("http://agentservice:15875"),
            new AgentServiceTlsOptions { TrustedCertificatePath = "/run/secrets/agentservice-ca.pem" },
            isDevelopment: false,
            isProduction: true);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_https_required");
    }

    [Fact]
    public void ValidateConfiguration_RejectsMissingTrustedCertificateInProduction()
    {
        var action = () => AgentServiceTransportSecurity.ValidateConfiguration(
            new Uri("https://agentservice:15875"),
            new AgentServiceTlsOptions(),
            isDevelopment: false,
            isProduction: true);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_trusted_certificate_required");
    }

    [Fact]
    public void ValidateConfiguration_AllowsHttpForLocalDevelopment()
    {
        var action = () => AgentServiceTransportSecurity.ValidateConfiguration(
            new Uri("http://localhost:15875"),
            new AgentServiceTlsOptions(),
            isDevelopment: true,
            isProduction: false);

        action.Should().NotThrow();
    }

    [Fact]
    public void ValidateConfiguration_RejectsHttpOutsideDevelopment()
    {
        var action = () => AgentServiceTransportSecurity.ValidateConfiguration(
            new Uri("http://agentservice:15875"),
            new AgentServiceTlsOptions(),
            isDevelopment: false,
            isProduction: false);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_https_required");
    }

    [Fact]
    public void ValidateConfiguration_RejectsRemoteHttpInDevelopment()
    {
        var action = () => AgentServiceTransportSecurity.ValidateConfiguration(
            new Uri("http://agentservice:15875"),
            new AgentServiceTlsOptions(),
            isDevelopment: true,
            isProduction: false);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("agent_service_https_required");
    }

    [Fact]
    public void LoadTrustedRoot_LoadsCertificateOnlyPem()
    {
        using var rootKey = RSA.Create(2048);
        using var rootCertificate = CreateCertificateAuthority("CN=Clawbot test root", rootKey, null);
        var certificatePath = Path.GetTempFileName();
        File.WriteAllText(certificatePath, rootCertificate.ExportCertificatePem());
        try {
            using var trustedRoot = AgentServiceGrpcHandlerFactory.LoadTrustedRoot(certificatePath);

            trustedRoot.Should().NotBeNull();
            trustedRoot!.Thumbprint.Should().Be(rootCertificate.Thumbprint);
        }
        finally {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public void IsTrustedServerCertificate_RejectsClientAuthenticationOnlyCertificate()
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=Clawbot test root",
            rootKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        using var rootCertificate = rootRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(3));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            "CN=agentservice",
            leafKey,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") },
            critical: true));
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("agentservice");
        leafRequest.CertificateExtensions.Add(subjectAlternativeName.Build());
        using var clientCertificate = leafRequest.Create(
            rootCertificate,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            RandomNumberGenerator.GetBytes(16));

        var isTrusted = AgentServiceGrpcHandlerFactory.IsTrustedServerCertificate(
            rootCertificate,
            clientCertificate,
            presentedChain: null,
            errors: SslPolicyErrors.None);

        isTrusted.Should().BeFalse();
    }

    [Fact]
    public void IsTrustedServerCertificate_AcceptsPresentedIntermediateCertificate()
    {
        using var rootKey = RSA.Create(2048);
        using var rootCertificate = CreateCertificateAuthority("CN=Clawbot test root", rootKey, null);
        using var intermediateKey = RSA.Create(2048);
        using var intermediateCertificate = CreateCertificateAuthority(
            "CN=Clawbot test intermediate",
            intermediateKey,
            rootCertificate);
        using var leafKey = RSA.Create(2048);
        using var serverCertificate = CreateServerCertificate(leafKey, intermediateCertificate);
        using var presentedChain = new X509Chain();
        presentedChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        presentedChain.ChainPolicy.CustomTrustStore.Add(rootCertificate);
        presentedChain.ChainPolicy.ExtraStore.Add(intermediateCertificate);
        presentedChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        var presentedChainBuildSucceeded = presentedChain.Build(serverCertificate);
        var presentedChainStatus = string.Join(
            ", ",
            presentedChain.ChainStatus.Select(status => status.StatusInformation.Trim()));
        presentedChainBuildSucceeded.Should().BeTrue(presentedChainStatus);

        var isTrusted = AgentServiceGrpcHandlerFactory.IsTrustedServerCertificate(
            rootCertificate,
            serverCertificate,
            presentedChain,
            SslPolicyErrors.RemoteCertificateChainErrors);

        isTrusted.Should().BeTrue();
    }

    private static X509Certificate2 CreateCertificateAuthority(
        string subjectName,
        RSA key,
        X509Certificate2? issuerCertificate)
    {
        var request = new CertificateRequest(
            subjectName,
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 1, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
            critical: true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        if (issuerCertificate is null) {
            return request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(3));
        }

        var notBefore = new DateTimeOffset(issuerCertificate.NotBefore).AddMinutes(1);
        var notAfter = new DateTimeOffset(issuerCertificate.NotAfter).AddHours(-1);
        using var issuedCertificate = request.Create(
            issuerCertificate,
            notBefore,
            notAfter,
            RandomNumberGenerator.GetBytes(16));
        return issuedCertificate.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 CreateServerCertificate(
        RSA key,
        X509Certificate2 issuerCertificate)
    {
        var request = new CertificateRequest(
            "CN=agentservice",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
            critical: true));
        var subjectAlternativeName = new SubjectAlternativeNameBuilder();
        subjectAlternativeName.AddDnsName("agentservice");
        request.CertificateExtensions.Add(subjectAlternativeName.Build());
        return request.Create(
            issuerCertificate,
            new DateTimeOffset(issuerCertificate.NotBefore).AddMinutes(1),
            new DateTimeOffset(issuerCertificate.NotAfter).AddHours(-1),
            RandomNumberGenerator.GetBytes(16));
    }
}
