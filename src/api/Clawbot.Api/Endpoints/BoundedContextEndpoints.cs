namespace Clawbot.Api.Endpoints;

// Skeleton endpoint groups per bounded context that still lack real handlers.
// Do not add stubs for implemented groups: duplicate root routes can shadow
// real endpoints or create ambiguous matches.
//
// Trace: every group below ties to docs/spec-audit.md row.

public static class BoundedContextEndpoints
{
    public static IEndpointRouteBuilder MapBoundedContexts(this IEndpointRouteBuilder app)
    {
        Stub(app, "/api/integrations",   "SPEC-10 / SW-089..096");
        return app;
    }

    private static void Stub(IEndpointRouteBuilder app, string prefix, string spec)
    {
        var group = app.MapGroup(prefix).RequireAuthorization();
        group.MapGet("/", () => Results.Problem(
            statusCode: 501,
            title: "Not Implemented",
            detail: $"Endpoint group {prefix} pending implementation. Tracks {spec}."));
    }
}
