using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Auth;
using Clawbot.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Clawbot.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuth(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (
            LoginRequest req,
            UserManager<AppUser> users,
            SignInManager<AppUser> signIn,
            JwtTokenIssuer issuer) =>
        {
            var user = await users.FindByEmailAsync(req.Email);
            if (user is null) return Results.Unauthorized();

            var check = await signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
            if (!check.Succeeded) return Results.Unauthorized();

            var roles = await users.GetRolesAsync(user);
            // TODO(student): resolve tenant slug from tenants table.
            var (token, expires) = issuer.Issue(user.Id, user.TenantId, "default", roles);
            return Results.Ok(new LoginResponse(token, expires));
        }).AllowAnonymous();

        return app;
    }
}
