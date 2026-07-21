using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ContentPublishingPolicyPermissionTests
{
    [Fact]
    public void Canonical_policy_routes_require_read_and_admin_configuration_permissions()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "api",
            "Clawbot.Api",
            "Endpoints",
            "ContentPublishingPolicyEndpoints.cs"));

        source.Should().MatchRegex(
            "MapGet\\(\"/settings/publishing-policy\"[^\\r\\n]*RequirePermission\\(\"content:read\"\\)");
        source.Should().MatchRegex(
            "MapPut\\(\"/settings/publishing-policy\"[^\\r\\n]*RequirePermission\\(\"system:config\"\\)");
    }

    [Fact]
    public void Human_decision_routes_require_content_approve_permission()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "api",
            "Clawbot.Api",
            "Endpoints",
            "ContentEndpoints.cs"));

        source.Should().Contain(
            "grp.MapPost(\"/items/{id:guid}/approve\", ApproveItemAsync).RequirePermission(\"content:approve\")");
        source.Should().Contain(
            "grp.MapPost(\"/items/{id:guid}/reject\", RejectItemAsync).RequirePermission(\"content:approve\")");
    }

    [Fact]
    public void Publish_retry_and_reconciliation_require_content_publish_permission()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src",
            "api",
            "Clawbot.Api",
            "Endpoints",
            "ContentEndpoints.cs"));

        source.Should().MatchRegex(
            "MapPost\\(\"/schedules/\\{id:guid\\}/publish/retry\"[^\\r\\n]*RequirePermission\\(\"content:publish\"\\)");
        source.Should().MatchRegex(
            "MapPost\\(\"/schedules/\\{id:guid\\}/publish/reconcile\"[^\\r\\n]*RequirePermission\\(\"content:publish\"\\)");
        source.Should().NotContain(
            "grp.MapPost(\"/schedule/{id:guid}/retry\", RetryScheduleAsync).RequirePermission(\"content:write\")",
            "content:write must not retain the legacy immediate-publication bypass");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
