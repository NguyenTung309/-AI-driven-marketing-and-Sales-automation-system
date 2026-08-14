using System.Security.Cryptography;
using System.Text;

namespace Clawbot.SharedKernel.Security;

public sealed class AgentServiceAuthenticationOptions
{
    public const string SectionName = "AgentServiceAuthentication";
    public const string Issuer = "clawbot-api";
    public const string Audience = "clawbot-agent-service";
    public const string ClientId = "clawbot-api";
    public const int MinimumSigningKeyBytes = 32;

    // Danh tính cố định của token "service" mà background job phát ra. 6 service agent còn lại
    // (report/content/lead/sale-assist/docs/research) không đọc claim nào — tenant/user đi theo
    // field của message — nên danh tính service không nới cửa quyền nào; nó chỉ chứng minh
    // cuộc gọi xuất phát từ chính API host. Đường orchestrator vẫn bắt danh tính phiên thật.
    public static readonly Guid ServiceUserId = new("11111111-0000-0000-0000-000000000002");
    public static readonly Guid ServiceRoleId = new("11111111-0000-0000-0000-000000000003");

    public string SigningKey { get; set; } = string.Empty;
    public int TokenLifetimeMinutes { get; set; } = 2;

    public static byte[] GetSigningKeyBytes(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
            throw new InvalidOperationException("agent_service_auth_signing_key_required");

        try
        {
            var bytes = Convert.FromBase64String(signingKey.Trim());
            if (bytes.Length < MinimumSigningKeyBytes)
                throw new InvalidOperationException("agent_service_auth_signing_key_invalid");

            return bytes;
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("agent_service_auth_signing_key_invalid");
        }
    }

    public static void EnsureGrpcTransportSecurity(
        string? grpcEndpointUrl,
        string? certificatePath,
        bool isDevelopment)
    {
        if (isDevelopment)
            return;

        if (!Uri.TryCreate(grpcEndpointUrl, UriKind.Absolute, out var grpcEndpoint)
            || !string.Equals(grpcEndpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("agent_service_https_required");
        }

        if (string.IsNullOrWhiteSpace(certificatePath))
            throw new InvalidOperationException("agent_service_tls_certificate_required");
    }

    public static void EnsureDistinctFromPublicJwtKey(
        string? agentServiceSigningKey,
        string? publicJwtSigningKey)
    {
        if (string.IsNullOrWhiteSpace(publicJwtSigningKey))
            return;

        var agentServiceKeyBytes = GetSigningKeyBytes(agentServiceSigningKey);
        var publicJwtKeyBytes = Encoding.UTF8.GetBytes(publicJwtSigningKey);
        if (CryptographicOperations.FixedTimeEquals(agentServiceKeyBytes, publicJwtKeyBytes))
            throw new InvalidOperationException("agent_service_auth_signing_key_reused");
    }
}
