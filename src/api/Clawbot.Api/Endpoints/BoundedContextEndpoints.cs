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
        // /api/contacts now implemented in ContactsEndpoints (W6.13).
        Stub(app, "/api/inbox",          "SPEC-01 / SW-011..022");
        Stub(app, "/api/kb",             "SPEC-02 / SW-023..034");
        Stub(app, "/api/kb/accuracy",    "SPEC-02 / SW-115..120");
        // /api/chat-scenarios now implemented in ChatScenariosEndpoints (M05).
        Stub(app, "/api/agents",         "SPEC-03 / SW-035..046");
        Stub(app, "/api/sale-assist",    "SPEC-04 / SW-047..056");
        Stub(app, "/api/leads",          "SPEC-05 / SW-057..068");
        // /api/content now implemented in ContentEndpoints (M18).
        Stub(app, "/api/docs",           "SPEC-07 / SW-107..114");
        // /api/analytics now implemented in AnalyticsEndpoints (M20).
        Stub(app, "/api/ads",            "SPEC-09 / SW-094..096");
        // /api/admin now implemented in AdminEndpoints (W6.11).
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
