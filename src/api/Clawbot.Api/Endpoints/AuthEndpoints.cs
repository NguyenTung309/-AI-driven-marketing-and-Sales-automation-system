using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Auth;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Clawbot.Api.Endpoints;

public static partial class AuthEndpoints
{
    private const string RefreshCookie = "refresh_token";

    private static readonly JsonSerializerOptions MeJsonOptions = new()
    {
        PropertyNamingPolicy = null // Preserve C# property names
    };

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Password reset OTP for {Email}: {Otp}")]
    private static partial void LogResetOtpIssued(ILogger logger, string email, string otp);

    private static readonly TimeSpan ResetOtpTtl = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

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
        group.MapPost("/change-password", ChangePasswordAsync).RequireAuthorization();

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
        if (check.IsLockedOut) return Results.Unauthorized();
        if (!check.Succeeded) return Results.Unauthorized();

        // CheckPasswordSignInAsync validates password + lockout only; it never reports
        // RequiresTwoFactor. Check 2FA explicitly so enabled accounts are challenged.
        if (await users.GetTwoFactorEnabledAsync(user))
            return Results.Json(new { requiresTwoFactor = true }, statusCode: 202);

        var result = await IssueSessionAsync(user, http, users, db, issuer, refreshTokens, env, ct);
        await RecordLoginAsync(db, user, http, ct);
        return result;
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

        var result = await IssueSessionAsync(user, http, users, db, issuer, refreshTokens, env, ct);
        await RecordLoginAsync(db, user, http, ct);
        return result;
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
            ClearRefreshCookie(http, env);
            return Results.Unauthorized();
        }

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
        var raw = http.Request.Cookies[RefreshCookie];
        if (!string.IsNullOrEmpty(raw))
            await refreshTokens.RevokeAsync(raw, ct);

        ClearRefreshCookie(http, env);
        return Results.NoContent();
    }

    private static async Task<IResult> RequestResetAsync(
        PasswordResetRequest req,
        UserManager<AppUser> users,
        IMemoryCache cache,
        IEmailSender email,
        ILogger<Program> log)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.Ok(); // Avoid email enumeration.

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var otp = GenerateOtp();
        cache.Set(ResetOtpCacheKey(req.Email, otp), token, ResetOtpTtl);

        LogResetOtpIssued(log, req.Email, otp);
        await email.SendAsync(req.Email, "Dat lai mat khau Hoc Ba",
            $"Ma OTP dat lai mat khau cua ban: {otp}. Ma co hieu luc trong 10 phut.");
        return Results.Ok();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest req,
        ClaimsPrincipal principal,
        UserManager<AppUser> users)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var result = await users.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
        return result.Succeeded
            ? Results.Ok()
            : Results.BadRequest(result.Errors.Select(e => e.Description));
    }

    private static async Task<IResult> ConfirmResetAsync(
        PasswordResetConfirm req,
        UserManager<AppUser> users,
        IMemoryCache cache,
        IRefreshTokenService refreshTokens,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.BadRequest("invalid_token");

        var cacheKey = ResetOtpCacheKey(req.Email, req.Token);
        var identityToken = cache.Get<string>(cacheKey);
        if (string.IsNullOrWhiteSpace(identityToken))
            return Results.BadRequest("invalid_or_expired_otp");

        var result = await users.ResetPasswordAsync(user, identityToken, req.NewPassword);
        if (!result.Succeeded)
            return Results.BadRequest(result.Errors.Select(e => e.Description));

        cache.Remove(cacheKey);
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

        return Results.Json(new
        {
            sub,
            tenant_id = principal.FindFirstValue("tenant_id"),
            tenant_slug = principal.FindFirstValue("tenant_slug"),
            role_id = roleId == Guid.Empty ? null : roleId.ToString(),
            role = roleName,
            permissions = perms.OrderBy(p => p).ToArray(),
        }, MeJsonOptions);
    }

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
        http.Response.Cookies.Delete(RefreshCookie, BuildCookieOptions(env, expires: null));

    private static CookieOptions BuildCookieOptions(IHostEnvironment env, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = !env.IsDevelopment(),
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

    private static string GenerateOtp() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    private static string ResetOtpCacheKey(string email, string otp) =>
        $"password-reset:{email.Trim().ToUpperInvariant()}:{otp.Trim()}";

    private static async Task RecordLoginAsync(AppDbContext db, AppUser user, HttpContext http, CancellationToken ct)
    {
        var userAgent = http.Request.Headers.UserAgent.ToString();
        db.AuditLogs.Add(AuditLog.Create(
            user.TenantId,
            user.Id,
            "auth.login",
            "user",
            user.Id,
            DateTimeOffset.UtcNow,
            ip: http.Connection.RemoteIpAddress,
            userAgent: string.IsNullOrWhiteSpace(userAgent) ? null : userAgent));
        await db.SaveChangesAsync(ct);
    }

    private static bool IsActive(this AppUser user)
    {
        if (!user.IsActive) return false;
        if (!user.LockoutEnabled) return true;
        return !user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow;
    }
}
