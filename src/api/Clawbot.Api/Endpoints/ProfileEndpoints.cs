using System.Globalization;
using System.Security.Claims;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Clawbot.Api.Endpoints;

public sealed record UpdateProfileRequest(string? DisplayName, string? Phone, DateOnly? DateOfBirth);

// M23 — current user's own profile (read/update). Avatar upload deferred (needs IDocumentStorage in API).
public static class ProfileEndpoints
{
    public static IEndpointRouteBuilder MapProfile(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/profile").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", GetAsync);
        grp.MapPut("/", UpdateAsync);

        return grp;
    }

    private static async Task<IResult> GetAsync(UserManager<AppUser> users, ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        var roles = await users.GetRolesAsync(user);
        return Results.Ok(new
        {
            user.Id,
            user.Email,
            user.DisplayName,
            Phone = user.PhoneNumber,
            user.DateOfBirth,
            user.AvatarUrl,
            user.IsActive,
            roles,
            tenantSlug = principal.FindFirstValue("tenant_slug"),
        });
    }

    private static async Task<IResult> UpdateAsync(
        UpdateProfileRequest req, UserManager<AppUser> users, ClaimsPrincipal principal)
    {
        var user = await users.GetUserAsync(principal);
        if (user is null) return Results.Unauthorized();

        if (req.DisplayName is not null) user.DisplayName = req.DisplayName;
        if (req.Phone is not null) user.PhoneNumber = req.Phone;
        if (req.DateOfBirth is not null) user.DateOfBirth = req.DateOfBirth;

        var result = await users.UpdateAsync(user);
        return result.Succeeded
            ? Results.Ok(new { user.Id, user.DisplayName, Phone = user.PhoneNumber, user.DateOfBirth })
            : Results.BadRequest(result.Errors.Select(e => e.Description));
    }
}
