using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class OrchestrationV2EndpointPermissionTests
{
    [Fact]
    public void OrchestrationV2Endpoints_require_expected_permissions()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "OrchestrationV2Endpoints.cs"));

        source.Should().Contain("group.MapPost(\"/runs\", CreateRunAsync).RequirePermission(\"orchestration:run\")");
        source.Should().Contain("group.MapGet(\"/runs/{id:guid}\", GetRunAsync).RequirePermission(\"orchestration:view\")");
        source.Should().Contain("group.MapPost(\"/runs/{id:guid}/control\", ControlRunAsync).RequirePermission(\"orchestration:manage\")");
        source.Should().Contain("group.MapGet(\"/agents\", ListAgentsAsync).RequirePermission(\"orchestration:view\")");
        source.Should().Contain("group.MapPost(\"/agents\", UpsertAgentAsync).RequirePermission(\"orchestration:manage\")");
        source.Should().Contain("group.MapGet(\"/schedules\", ListSchedulesAsync).RequirePermission(\"orchestration:view\")");
        source.Should().Contain("group.MapPost(\"/schedules\", CreateScheduleAsync).RequirePermission(\"orchestration:manage\")");
        source.Should().Contain("group.MapPost(\"/schedules/{id:guid}/run-now\", RunScheduleNowAsync).RequirePermission(\"orchestration:run\")");
    }

    [Fact]
    public void AdminChannelsEndpoints_require_channels_manage_permission()
    {
        // SPEC-16 Module M-2: admin channel config endpoints are gated by channels:manage.
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "AdminChannelsEndpoints.cs"));

        source.Should().Contain("RequirePermission(\"channels:manage\")");
        source.Should().Contain("MapPost(\"/pancake/connect\", ConnectPancakeAsync)");
        source.Should().Contain("MapPost(\"/pancake/pages\", MintPancakePagesAsync)");
        source.Should().Contain("MapGet(\"/pancake/pages\", ListConnectedPagesAsync)");
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

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
