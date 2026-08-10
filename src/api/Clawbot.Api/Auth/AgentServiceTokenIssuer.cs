using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Clawbot.SharedKernel.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.Api.Auth;

public sealed class AgentServiceTokenIssuer(IOptions<AgentServiceAuthenticationOptions> options)
{
    public string Issue(Guid userId, Guid tenantId)
    {
        var settings = options.Value;
        var signingKeyBytes = AgentServiceAuthenticationOptions.GetSigningKeyBytes(settings.SigningKey);
        if (settings.TokenLifetimeMinutes is < 1 or > 5)
            throw new InvalidOperationException("agent_service_auth_token_lifetime_invalid");

        var now = DateTimeOffset.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new("tenant_id", tenantId.ToString("D")),
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
