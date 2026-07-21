namespace Clawbot.Api.Tests.Endpoints;

public sealed class LeadLifecycleEndpointContractTests
{
    [Fact]
    public void Lead_stage_endpoint_requires_write_permission_and_limits_actions()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("MapPut(\"/{id:guid}/stage\", UpdateStageAsync).RequirePermission(\"leads:write\")", source);
        Assert.Contains("case \"customer\"", source);
        Assert.Contains("case \"lost\"", source);
        Assert.Contains("case \"reopen\"", source);
        Assert.Contains("invalid_stage_action", source);
    }

    [Fact]
    public void Payment_confirmed_bypasses_scoring_and_marks_customer()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("string.Equals(body.EventCode, \"payment_confirmed\"", source);
        Assert.Contains("trigger: \"payment_event\"", source);
        Assert.Contains("new LeadActivityResponse(lead.Score, lead.Stage, \"payment_confirmed\", [])", source);
    }

    [Fact]
    public void Stage_mutation_uses_object_level_CanManageLead()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("if (!CanManageLead(http, lead))", source);
        Assert.Contains("role_id", source);
        Assert.Contains("RbacSeeder.Admin", source);
        Assert.Contains("RbacSeeder.SalesLead", source);
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
