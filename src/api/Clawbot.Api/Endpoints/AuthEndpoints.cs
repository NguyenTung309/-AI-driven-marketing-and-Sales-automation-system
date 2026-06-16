using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clawbot.Api.Endpoints;

public static partial class AuthEndpoints
{
    private const string RefreshCookie = "refresh_token";

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Password reset token for {Email}: {Token}")]
    private static partial void LogResetTokenIssued(ILogger logger, string email, string token);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", LoginAsync).AllowAnonymous().RequireRateLimiting(RateLimitingExtensions.AuthPolicy);
        group.MapPost("/login/2fa", LoginWithTwoFactorAsync).AllowAnonymous().RequireRateLimiting(RateLimitingExtensions.AuthPolicy);
        group.MapPost("/refresh", RefreshAsync).AllowAnonymous().RequireRateLimiting(RateLimitingExtensions.AuthPolicy);
        group.MapPost("/logout", LogoutAsync).AllowAnonymous();
        group.MapPost("/reset/request", RequestResetAsync).AllowAnonymous();
        group.MapPost("/reset/confirm", ConfirmResetAsync).AllowAnonymous();
        group.MapPost("/2fa/enable", EnableTwoFactorAsync).RequireAuthorization();
        group.MapPost("/2fa/verify", VerifyTwoFactorAsync).RequireAuthorization();
        group.MapPost("/2fa/disable", DisableTwoFactorAsync).RequireAuthorization();
        group.MapGet("/me", Me).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest req,
        HttpContext http,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDbContext db,
        JwtTokenIssuer issuer,
        IRefreshTokenService refreshTokens,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive()) return Results.Unauthorized();

        var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
        {
            return Results.Unauthorized();
        }
        if (!check.Succeeded) return Results.Unauthorized();

        // CheckPasswordSignInAsync only validates password + lockout — it never reports
        // RequiresTwoFactor (only PasswordSignInAsync does). Check 2FA explicitly so an
        // account with 2FA enabled is challenged instead of being signed in directly.
        if (await users.GetTwoFactorEnabledAsync(user))
            return Results.Json(new { requiresTwoFactor = true }, statusCode: 202);

        return await IssueSessionAsync(user, http, users, db, issuer, refreshTokens, env, ct);
    }

    private static async Task<IResult> LoginWithTwoFactorAsync(
        TwoFactorLoginRequest req,
        HttpContext http,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDbContext db,
        JwtTokenIssuer issuer,
        IRefreshTokenService refreshTokens,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive()) return Results.Unauthorized();

        var ok = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: false);
        if (!ok.Succeeded) return Results.Unauthorized();

        var valid = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, req.Code);
        if (!valid) return Results.Unauthorized();

        return await IssueSessionAsync(user, http, users, db, issuer, refreshTokens, env, ct);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        UserManager<AppUser> users,
        AppDbContext db,
        JwtTokenIssuer issuer,
        IRefreshTokenService refreshTokens,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var raw = http.Request.Cookies[RefreshCookie];
        if (string.IsNullOrEmpty(raw))
        {
            ClearRefreshCookie(http, env);
            return Results.Unauthorized();
        }

        var result = await refreshTokens.RotateAsync(raw, ClientIp(http), ct);
        if (result.Outcome != RotateOutcome.Success)
        {
            // Invalid (expired/unknown) or Reuse (family already revoked) → clear + 401.
            ClearRefreshCookie(http, env);
            return Results.Unauthorized();
        }

        // Re-check the account is still usable; otherwise revoke and bounce to login.
        var user = await users.FindByIdAsync(result.UserId.ToString());
        if (user is null || !user.IsActive())
        {
            await refreshTokens.RevokeAllForUserAsync(result.UserId, ct);
            ClearRefreshCookie(http, env);
            return Results.Unauthorized();
        }

        SetRefreshCookie(http, env, result.RawToken!, result.ExpiresAt);
        var (token, expires) = await IssueAccessTokenAsync(user, users, db, issuer, ct);
        return Results.Ok(new LoginResponse(token, expires));
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        IRefreshTokenService refreshTokens,
        IHostEnvironment env,
        CancellationToken ct)
    {
        // Idempotent: no cookie / already-revoked token still returns 204.
        var raw = http.Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(raw))
            await refreshTokens.RevokeAsync(raw, ct);

        ClearRefreshCookie(http, env);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestResetAsync(
        PasswordResetRequest req,
        UserManager<AppUser> users,
        ILogger<Program> log)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.Ok(); // Avoid email enumeration.

        var token = await users.GeneratePasswordResetTokenAsync(user);
        // TODO(M03): emit via email service. For now log so dev can copy.
        LogResetTokenIssued(log, req.Email, token);
        return Results.Ok();
    }

    private static async Task<IResult> ConfirmResetAsync(
        PasswordResetConfirm req,
        UserManager<AppUser> users,
        IRefreshTokenService refreshTokens,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.BadRequest("invalid_token");

        var result = await users.ResetPasswordAsync(user, req.Token, req.NewPassword);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors.Select(e => e.Description));

        // SPEC-11: reset commonly follows a suspected compromise — force re-login on every
        // device by revoking the whole refresh-token family.
        await refreshTokens.RevokeAllForUserAsync(user.Id, ct);
        return Results.Ok();
    }

    private static async Task<IResult> EnableTwoFactorAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users,
        UrlEncoder urlEncoder)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        await users.ResetAuthenticatorKeyAsync(user);
        var key = await users.GetAuthenticatorKeyAsync(user)
                  ?? throw new InvalidOperationException("Failed to issue authenticator key.");

        var email = await users.GetEmailAsync(user) ?? user.Email ?? string.Empty;
        var uri = BuildAuthenticatorUri(urlEncoder, email, key);
        return Results.Ok(new TwoFactorEnableResponse(key, uri));
    }

    private static async Task<IResult> VerifyTwoFactorAsync(
        TwoFactorVerifyRequest req,
        ClaimsPrincipal principal,
        UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var valid = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, req.Code);
        if (!valid) return Results.BadRequest("invalid_code");

        await users.SetTwoFactorEnabledAsync(user, true);
        return Results.Ok();
    }

    private static async Task<IResult> DisableTwoFactorAsync(
        ClaimsPrincipal principal,
        UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user);
        return Results.Ok();
    }

    private static async Task<IResult> Me(
        ClaimsPrincipal principal,
        IPermissionResolver permissions,
        CancellationToken ct)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        var roleId = Guid.TryParse(principal.FindFirstValue("role_id"), out var parsed) ? parsed : Guid.Empty;
        var roleName = RbacSeeder.RoleIds.FirstOrDefault(kv => kv.Value == roleId).Key;
        var perms = await permissions.GetPermissionsAsync(roleId, ct);

        return Results.Ok(new
        {
            sub,
            roleId = roleId == Guid.Empty ? null : roleId.ToString(),
            role = roleName,
            permissions = perms.OrderBy(p => p).ToArray(),
        });
    }

    // Issues the access token and a fresh refresh-token session (new family) + cookie.
    private static async Task<IResult> IssueSessionAsync(
        AppUser user,
        HttpContext http,
        UserManager<AppUser> users,
        AppDbContext db,
        JwtTokenIssuer issuer,
        IRefreshTokenService refreshTokens,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var (refreshRaw, refreshExpires) = await refreshTokens.IssueAsync(user.Id, ClientIp(http), ct);
        SetRefreshCookie(http, env, refreshRaw, refreshExpires);

        var (token, expires) = await IssueAccessTokenAsync(user, users, db, issuer, ct);
        return Results.Ok(new LoginResponse(token, expires));
    }

    private static async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAccessTokenAsync(
        AppUser user,
        UserManager<AppUser> users,
        AppDbContext db,
        JwtTokenIssuer issuer,
        CancellationToken ct)
    {
        var roles = await users.GetRolesAsync(user);
        var slug = await db.Tenants
            .Where(t => t.Id == user.TenantId)
            .Select(t => t.Slug)
            .FirstOrDefaultAsync(ct) ?? "default";

        var roleId = ResolveRoleId(roles);
        return issuer.Issue(user.Id, user.TenantId, slug, roleId);
    }

    // Maps the user's Identity role name to its fixed Id. 0 roles / an unknown role yields
    // Guid.Empty → backend default-denies any permission-gated endpoint (AC).
    private static Guid ResolveRoleId(IEnumerable<string> roleNames)
    {
        foreach (var name in roleNames)
            if (RbacSeeder.RoleIds.TryGetValue(name, out var id))
                return id;
        return Guid.Empty;
    }

    private static void SetRefreshCookie(HttpContext http, IHostEnvironment env, string raw, DateTimeOffset expires) =>
        http.Response.Cookies.Append(RefreshCookie, raw, BuildCookieOptions(env, expires));

    private static void ClearRefreshCookie(HttpContext http, IHostEnvironment env) =>
        // Must match the set attributes (Path/SameSite/Secure) or the browser keeps a zombie cookie.
        http.Response.Cookies.Delete(RefreshCookie, BuildCookieOptions(env, expires: null));

    private static CookieOptions BuildCookieOptions(IHostEnvironment env, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !env.IsDevelopment(), // dev runs http; SameSite=Strict still works via vite same-origin proxy.
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Expires = expires,
    };

    private static string? ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();

    private static string BuildAuthenticatorUri(UrlEncoder encoder, string email, string sharedKey)
    {
        const string issuer = "ClawBot";
        return $"otpauth://totp/{encoder.Encode(issuer)}:{encoder.Encode(email)}" +
               $"?secret={sharedKey}&issuer={encoder.Encode(issuer)}&digits=6";
    }

    // SPEC-11 D11: a usable account must be active (is_active flag) AND not locked out.
    private static bool IsActive(this AppUser user)
    {
        if (!user.IsActive) return false;
        if (!user.LockoutEnabled) return true;
        return !user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow;
    }
}

