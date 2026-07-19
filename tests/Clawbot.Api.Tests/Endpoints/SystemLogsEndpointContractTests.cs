namespace Clawbot.Api.Tests.Endpoints;

/// <summary>
/// Source-level contract checks for system-logs endpoints (no full WebApplicationFactory in this suite).
/// </summary>
public sealed class SystemLogsEndpointContractTests
{
    [Fact]
    public void Admin_system_logs_endpoints_require_system_logs_permission()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "AdminSystemLogsEndpoints.cs"));
        Assert.Contains("RequirePermission(\"system.logs\")", source);
        Assert.Contains("MapGet(\"/\", ListAsync)", source);
        Assert.Contains("MapGet(\"/{id:long}\", GetAsync)", source);
        Assert.Contains("MapGet(\"/stats/hourly\", StatsHourlyAsync)", source);
    }

    [Fact]
    public void Admin_audit_logs_require_system_logs_permission()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "AdminEndpoints.cs"));
        Assert.Contains("MapGet(\"/audit-logs\", ListAuditLogsAsync).RequirePermission(\"system.logs\")", source);
        Assert.Contains("UserEmail", source);
    }

    [Fact]
    public void Logs_audit_endpoint_requires_system_logs_permission()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LogsEndpoints.cs"));
        Assert.Contains("MapGet(\"/audit\", ListAuditAsync).RequirePermission(\"system.logs\")", source);
    }

    [Fact]
    public void Exception_handler_and_request_logging_wire_tenant_user()
    {
        var program = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Program.cs"));
        Assert.Contains("diag.Set(\"TenantId\"", program);
        Assert.Contains("diag.Set(\"UserId\"", program);
        Assert.Contains("AddExceptionHandler<Clawbot.Api.Middleware.GlobalExceptionHandler>()", program);
        Assert.Contains("RequestStatsMiddleware", program);

        var handler = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Middleware", "GlobalExceptionHandler.cs"));
        Assert.Contains("TenantId={TenantId}", handler);
        Assert.Contains("UserId={UserId}", handler);
        Assert.Contains("errorCode = \"internal_error\"", handler);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate: {Path.Combine(segments)}");
    }
}
