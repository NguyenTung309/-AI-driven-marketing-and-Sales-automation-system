using System.Net;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Identity;
using Clawbot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Clawbot.Api.Endpoints;

public static partial class AuthEndpoints
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Information,
        Message = "Password reset OTP for {Email}: {Otp}")]
    private static partial void LogResetOtpIssued(ILogger logger, string email, string otp);

    private static readonly TimeSpan ResetOtpTtl = TimeSpan.FromMinutes(10);

    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth").RequireRateLimiting(RateLimitingExtensions.AuthPolicy);

        group.MapPost("/login", LoginAsync).AllowAnonymous();
        group.MapPost("/login/2fa", LoginWithTwoFactorAsync).AllowAnonymous();
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
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDbContext db,
        JwtTokenIssuer issuer,
        HttpContext http,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive()) return Results.Unauthorized();

        var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (check.IsLockedOut) return Results.Problem("Account locked", statusCode: (int)HttpStatusCode.Locked);
        if (check.RequiresTwoFactor) return Results.Json(new { requiresTwoFactor = true }, statusCode: 202);
        if (!check.Succeeded) return Results.Unauthorized();

        var (token, expires) = await IssueAsync(user, users, db, issuer, ct);
        await RecordLoginAsync(db, user, http, ct);
        return Results.Ok(new LoginResponse(token, expires));
    }

    private static async Task<IResult> LoginWithTwoFactorAsync(
        TwoFactorLoginRequest req,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        AppDbContext db,
        JwtTokenIssuer issuer,
        HttpContext http,
        CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null || !user.IsActive()) return Results.Unauthorized();

        var ok = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: false);
        if (!ok.Succeeded) return Results.Unauthorized();

        var valid = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, req.Code);
        if (!valid) return Results.Unauthorized();

        var (token, expires) = await IssueAsync(user, users, db, issuer, ct);
        await RecordLoginAsync(db, user, http, ct);
        return Results.Ok(new LoginResponse(token, expires));
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

        LogResetOtpIssued(log, req.Email, otp); // also logged so dev can copy when SMTP unset
        await email.SendAsync(req.Email, "Đặt lại mật khẩu Học Bá",
            $"Mã OTP đặt lại mật khẩu của bạn: {otp}. Mã có hiệu lực trong 10 phút.");
        return Results.Ok();
    }

    private static async Task<IResult> ChangePasswordAsync(
        ChangePasswordRequest req,
        System.Security.Claims.ClaimsPrincipal principal,
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
        IMemoryCache cache)
    {
        var user = await users.FindByEmailAsync(req.Email);
        if (user is null) return Results.BadRequest("invalid_token");

        var cacheKey = ResetOtpCacheKey(req.Email, req.Token);
        var identityToken = cache.Get<string>(cacheKey);
        if (string.IsNullOrWhiteSpace(identityToken))
            return Results.BadRequest("invalid_or_expired_otp");

        var result = await users.ResetPasswordAsync(user, identityToken, req.NewPassword);
        if (result.Succeeded) cache.Remove(cacheKey);
        return result.Succeeded
            ? Results.Ok()
            : Results.BadRequest(result.Errors.Select(e => e.Description));
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

    private static IResult Me(ClaimsPrincipal user) =>
        Results.Ok(new
        {
            sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
            tenantId = user.FindFirstValue("tenant_id"),
            tenantSlug = user.FindFirstValue("tenant_slug"),
            roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            permissions = user.FindAll("perm").Select(c => c.Value).ToArray(),
        });

    private static async Task<(string Token, DateTimeOffset ExpiresAt)> IssueAsync(
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

        var perms = await db.RolePermissions
            .Join(db.RbacRoles, rp => rp.RoleId, r => r.Id, (rp, r) => new { rp.PermissionId, r.Name, r.TenantId })
            .Where(x => x.TenantId == user.TenantId && roles.Contains(x.Name))
            .Join(db.Permissions, x => x.PermissionId, p => p.Id, (x, p) => p.Code)
            .Distinct()
            .ToListAsync(ct);

        return issuer.Issue(user.Id, user.TenantId, slug, roles, perms);
    }

    private static string BuildAuthenticatorUri(UrlEncoder encoder, string email, string sharedKey)
    {
        const string issuer = "ClawBot";
        return $"otpauth://totp/{encoder.Encode(issuer)}:{encoder.Encode(email)}" +
               $"?secret={sharedKey}&issuer={encoder.Encode(issuer)}&digits=6";
    }

    private static string GenerateOtp() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

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
        if (!user.IsActive) return false; // admin-set deactivation flag (M23)
        if (!user.LockoutEnabled) return true;
        return !user.LockoutEnd.HasValue || user.LockoutEnd <= DateTimeOffset.UtcNow;
    }
}
