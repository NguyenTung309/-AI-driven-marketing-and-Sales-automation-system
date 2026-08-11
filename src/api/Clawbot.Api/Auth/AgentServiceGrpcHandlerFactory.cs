using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Clawbot.Api.Auth;

public sealed class AgentServiceGrpcHandlerFactory(AgentServiceTlsOptions options)
{
    private readonly X509Certificate2? _trustedRoot = LoadTrustedRoot(options.TrustedCertificatePath);

    public HttpMessageHandler Create()
    {
        if (_trustedRoot is null)
            return new SocketsHttpHandler();

        return new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = ValidateCertificate,
            },
        };
    }

    private bool ValidateCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? serverChain,
        SslPolicyErrors errors) =>
        IsTrustedServerCertificate(_trustedRoot!, certificate, serverChain, errors);

    internal static bool IsTrustedServerCertificate(
        X509Certificate2 trustedRoot,
        X509Certificate? certificate,
        X509Chain? presentedChain,
        SslPolicyErrors errors)
    {
        ArgumentNullException.ThrowIfNull(trustedRoot);
        if (certificate is null
            || (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        using var serverCertificate = new X509Certificate2(certificate);
        if (presentedChain is not null) {
            foreach (var element in presentedChain.ChainElements) {
                if (!element.Certificate.RawData.AsSpan().SequenceEqual(serverCertificate.RawData)
                    && !element.Certificate.RawData.AsSpan().SequenceEqual(trustedRoot.RawData)) {
                    chain.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }
        }

        return chain.Build(serverCertificate);
    }

    internal static X509Certificate2? LoadTrustedRoot(string trustedCertificatePath)
    {
        if (string.IsNullOrWhiteSpace(trustedCertificatePath))
            return null;
        if (!File.Exists(trustedCertificatePath))
            throw new InvalidOperationException("agent_service_trusted_certificate_missing");

        return X509Certificate2.CreateFromPem(File.ReadAllText(trustedCertificatePath));
    }
}
