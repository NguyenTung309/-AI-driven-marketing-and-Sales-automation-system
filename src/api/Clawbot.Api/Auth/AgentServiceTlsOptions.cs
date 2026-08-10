namespace Clawbot.Api.Auth;

public sealed class AgentServiceTlsOptions
{
    public const string SectionName = "AgentServiceTls";

    public string TrustedCertificatePath { get; set; } = string.Empty;
}
