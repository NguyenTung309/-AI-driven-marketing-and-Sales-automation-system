using Hangfire.Dashboard;

namespace Clawbot.Api.Auth;

public sealed class HangfireAdminFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();
        return httpContext.User.HasClaim("perm", "admin.system")
            || httpContext.User.IsInRole("Admin");
    }
}
