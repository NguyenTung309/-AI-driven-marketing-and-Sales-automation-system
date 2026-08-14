namespace Clawbot.Api.Auth;

public static class AgentServiceTransportSecurity
{
    public static void ValidateConfiguration(
        Uri serviceUrl,
        AgentServiceTlsOptions tls,
        bool isDevelopment,
        bool isProduction)
    {
        var usesHttps = string.Equals(
            serviceUrl.Scheme,
            Uri.UriSchemeHttps,
            StringComparison.OrdinalIgnoreCase);
        if (!usesHttps) {
            if (!isDevelopment || !serviceUrl.IsLoopback)
                throw new InvalidOperationException("agent_service_https_required");

            return;
        }

        if (isProduction && string.IsNullOrWhiteSpace(tls.TrustedCertificatePath))
            throw new InvalidOperationException("agent_service_trusted_certificate_required");
    }
}
