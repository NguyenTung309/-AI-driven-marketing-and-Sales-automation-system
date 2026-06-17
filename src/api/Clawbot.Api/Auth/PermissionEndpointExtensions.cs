using Clawbot.Infrastructure.Auth;

namespace Clawbot.Api.Auth;

/// <summary>
/// SPEC-11 §6a — enforce a permission code at the handler boundary (Phương án A): read
/// role_id from the JWT, resolve the role's permissions (Redis → role_permissions), 403 if
/// missing. Endpoints with no §6a entry keep plain RequireAuthorization() and skip this.
/// </summary>
public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string code)
        where TBuilder : IEndpointConventionBuilder
    {
        builder.RequireAuthorization();
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;

            // 0 role / role outside the seeded set / no role_id claim → default-deny (AC).
            if (!Guid.TryParse(http.User.FindFirst("role_id")?.Value, out var roleId) || roleId == Guid.Empty)
                return Forbidden(http);

            var resolver = http.RequestServices.GetRequiredService<IPermissionResolver>();
            var permissions = await resolver.GetPermissionsAsync(roleId, http.RequestAborted);
            if (!permissions.Contains(code))
                return Forbidden(http);

            return await next(ctx);
        });
        return builder;
    }

    private static IResult Forbidden(HttpContext http) =>
        Results.Json(
            new { errorCode = "forbidden", message = "Không có quyền", requestId = http.TraceIdentifier },
            statusCode: StatusCodes.Status403Forbidden);
}
