using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Clawbot.Integration.Tests;

public sealed class TestAuthHandlerInboxAdmin(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestAuthHandler.UserId),
            new Claim("tenant_id", TestAuthHandler.TenantId),
            new Claim("tenant_slug", "test"),
            new Claim("perm", "admin.system"),
            new Claim("perm", "admin:inboxes"),
            new Claim("perm", "users:pancake-token:manage"),
        };
        var identity = new ClaimsIdentity(claims, "TestInboxAdmin");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestInboxAdmin");
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
