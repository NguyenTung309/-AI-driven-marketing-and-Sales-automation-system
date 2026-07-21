using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Integration.Tests;

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string TenantId = "00000000-0000-0000-0000-000000000001";
    public const string UserId = "00000000-0000-0000-0000-000000000002";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, UserId),
            new Claim("tenant_id", TenantId),
            new Claim("tenant_slug", "test"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("role_id", "11111111-1111-1111-1111-111111111111"),
            // Exact codes from RbacSeeder matrix (colon form). role_id Admin also resolves via IPermissionResolver.
            new Claim("perm", "kb:read"),
            new Claim("perm", "kb:write"),
            new Claim("perm", "conversations:read"),
            new Claim("perm", "leads:read"),
            new Claim("perm", "leads:write"),
            new Claim("perm", "content:read"),
            new Claim("perm", "content:write"),
            new Claim("perm", "analytics:read"),
            new Claim("perm", "sale-assist:use"),
            new Claim("perm", "system:config"),
            new Claim("perm", "system.logs"),
        };
        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "Test");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
