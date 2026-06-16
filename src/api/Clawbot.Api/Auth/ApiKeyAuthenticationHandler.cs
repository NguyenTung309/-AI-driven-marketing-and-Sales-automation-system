using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Auth;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IServiceScopeFactory scopeFactory)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var header = authHeader.ToString();
        if (!header.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var plaintext = header["ApiKey ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(plaintext))
            return AuthenticateResult.NoResult();

        var hash = Hash(plaintext);

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var key = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == hash && k.RevokedAt == null);

        if (key is null)
            return AuthenticateResult.Fail("Invalid API key.");

        if (key.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow)
            return AuthenticateResult.Fail("API key expired.");

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, key.Id.ToString()),
            new("tenant_id", key.TenantId.ToString()),
            new("tenant_slug", "api-key"),
            new("api_key_id", key.Id.ToString()),
        };

        if (!string.IsNullOrWhiteSpace(key.ScopesJson))
        {
            try
            {
                var scopes = JsonSerializer.Deserialize<string[]>(key.ScopesJson);
                if (scopes is not null)
                {
                    foreach (var perm in scopes)
                        claims.Add(new Claim("perm", perm));
                }
            }
            catch (JsonException)
            {
                // Malformed scopes JSON — skip perm claims
            }
        }

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private static string Hash(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
