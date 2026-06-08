using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.Api.Auth;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options)
{
    // SPEC-11 D3: JWT carries sub (userId) + role_id (fixed AppRole.Id) only.
    // Permission ("perm") and role[] claims are removed — permission is resolved at
    // runtime on the backend so role permission changes take effect immediately.
    public (string Token, DateTimeOffset ExpiresAt) Issue(
        Guid userId,
        Guid tenantId,
        string tenantSlug,
        Guid roleId)
    {
        var opt = options.Value;
        var expires = DateTimeOffset.UtcNow.AddMinutes(opt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("role_id", roleId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("tenant_slug", tenantSlug),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            opt.Issuer,
            opt.Audience,
            claims,
            expires: expires.UtcDateTime,
            signingCredentials: creds);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
