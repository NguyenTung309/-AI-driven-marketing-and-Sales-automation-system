namespace Clawbot.Api.Endpoints;

// Skeleton endpoint groups per bounded context.
// Each MapGroup currently exposes a single ping/list stub returning 501.
// Replace stubs with real handlers (delegated to Application layer) as SPECs land.
//
// Trace: every group below ties to docs/spec-audit.md row.

public static class BoundedContextEndpoints
{
    public static IEndpointRouteBuilder MapBoundedContexts(this IEndpointRouteBuilder app)
    {
        Stub(app, "/api/contacts",       "SPEC-01 / SW-013, SW-016");
        Stub(app, "/api/inbox",          "SPEC-01 / SW-011..022");
        Stub(app, "/api/kb",             "SPEC-02 / SW-023..034");
        Stub(app, "/api/kb/accuracy",    "SPEC-02 / SW-115..120");
        Stub(app, "/api/scenarios",      "SPEC-01 / chat_scenarios");
        Stub(app, "/api/agents",         "SPEC-03 / SW-035..046");
        Stub(app, "/api/sale-assist",    "SPEC-04 / SW-047..056");
        Stub(app, "/api/leads",          "SPEC-05 / SW-057..068");
        Stub(app, "/api/content",        "SPEC-06 / SW-069..078");
        Stub(app, "/api/docs",           "SPEC-07 / SW-107..114");
        Stub(app, "/api/analytics",      "SPEC-08 / SW-079..088");
        Stub(app, "/api/ads",            "SPEC-09 / SW-094..096");
        Stub(app, "/api/admin",          "SPEC-10 / SW-097..106");
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
