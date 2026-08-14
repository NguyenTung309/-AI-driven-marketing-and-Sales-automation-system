using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Clawbot.SharedKernel.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.Infrastructure.Security;

// Nguồn phát token AgentService duy nhất — đặt ở lớp shared để CẢ 2 host đều dùng được:
// API phát cho 6 client gRPC + job nền, AgentService phát cho ChatAgentClient (consumer
// MassTransit chạy ở cả hai host, gọi ChatAgent gRPC của chính AgentService).
public sealed class AgentServiceTokenIssuer(IOptions<AgentServiceAuthenticationOptions> options)
{
    // The token carries the single role the caller's session was issued for. Without it the agent
    // service has to re-derive authority from the account, which grants the union of every role the
    // account holds — a wider door into the same permissions than the API itself opens.
    public string Issue(Guid userId, Guid tenantId, Guid roleId) =>
        BuildToken(userId, tenantId, roleId);

    // Token cho ngữ cảnh nền (Hangfire, MassTransit consumer) — không có phiên HTTP nên không
    // có danh tính người dùng thật. Các service nhận (report/content/lead/sale-assist/docs/
    // research/chat) không đọc claim nào, tenant/user đi theo field của message; token này chỉ
    // chứng minh cuộc gọi xuất phát từ chính host trong hệ thống.
    // KHÔNG được dùng cho đường orchestrator (bên đó bắt danh tính phiên + permission qua claim).
    public string IssueServiceToken(Guid tenantId)
        => BuildToken(AgentServiceAuthenticationOptions.ServiceUserId, tenantId, AgentServiceAuthenticationOptions.ServiceRoleId);

    private string BuildToken(Guid userId, Guid tenantId, Guid roleId)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("A caller user is required.", nameof(userId));
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A caller tenant is required.", nameof(tenantId));
        if (roleId == Guid.Empty)
            throw new ArgumentException("A caller role is required.", nameof(roleId));

        var settings = options.Value;
        var signingKeyBytes = AgentServiceAuthenticationOptions.GetSigningKeyBytes(settings.SigningKey);
        if (settings.TokenLifetimeMinutes is < 1 or > 5)
            throw new InvalidOperationException("agent_service_auth_token_lifetime_invalid");

        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new("tenant_id", tenantId.ToString("D")),
            new("role_id", roleId.ToString("D")),
            new("client_id", AgentServiceAuthenticationOptions.ClientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKeyBytes),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            AgentServiceAuthenticationOptions.Issuer,
            AgentServiceAuthenticationOptions.Audience,
            claims,
            notBefore: now.UtcDateTime,
            expires: now.AddMinutes(settings.TokenLifetimeMinutes).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
