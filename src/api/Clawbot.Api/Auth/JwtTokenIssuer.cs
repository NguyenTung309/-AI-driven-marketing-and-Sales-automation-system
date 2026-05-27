using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Clawbot.Api.Auth;

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options)
{
    public (string Token, DateTimeOffset ExpiresAt) Issue(
        Guid userId,
        Guid tenantId,
        string tenantSlug,
        IEnumerable<string> roles)
    {
        var opt = options.Value;
        var expires = DateTimeOffset.UtcNow.AddMinutes(opt.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("tenant_slug", tenantSlug),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

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
