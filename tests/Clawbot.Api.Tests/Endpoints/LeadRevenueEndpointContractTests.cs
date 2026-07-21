namespace Clawbot.Api.Tests.Endpoints;

public sealed class LeadRevenueEndpointContractTests
{
    [Fact]
    public void Revenue_endpoints_use_existing_lead_permissions()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("MapGet(\"/{id:guid}/revenues\", ListRevenuesAsync).RequirePermission(\"leads:read\")", source);
        Assert.Contains("MapPost(\"/{id:guid}/revenues\", CreateRevenueAsync).RequirePermission(\"leads:write\")", source);
        Assert.Contains("MapPut(\"/revenues/{revenueId:guid}\", DecideRevenueAsync).RequirePermission(\"leads:write\")", source);
    }

    [Fact]
    public void Revenue_decisions_are_limited_to_approve_or_reject()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("case \"approve\"", source);
        Assert.Contains("case \"reject\"", source);
        Assert.Contains("invalid_revenue_action", source);
        Assert.Contains("unsupported_currency", source);
    }

    [Fact]
    public void Customer_transition_launches_estimate_job_when_no_manual_amount()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));
        var program = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Program.cs"));

        Assert.Contains("TryLaunchRevenueEstimateAsync", source);
        Assert.Contains("lead-revenue-estimate", source);
        Assert.Contains("revenue_already_recorded", source);
        Assert.Contains("LeadRevenueEstimateJobHandler", program);
        Assert.Contains("CanManageLead", source);
        Assert.Contains("lead_not_owned", source);
    }

    [Fact]
    public void Assign_and_manage_use_role_id_not_IsInRole()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("IsLeadManager", source);
        Assert.Contains("RbacSeeder.RoleIds", source);
        Assert.Contains("can_only_claim_self", source);
        Assert.Contains("assignee_not_eligible", source);
        Assert.DoesNotContain("IsInRole(\"Admin\")", source);
        Assert.DoesNotContain("IsInRole(\"SalesLead\")", source);
    }

    [Fact]
    public void Payment_replace_pending_and_active_revenue_guard_exist()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Endpoints", "LeadsEndpoints.cs"));

        Assert.Contains("StatusPending", source);
        Assert.Contains("StatusApproved", source);
        Assert.Contains("proposal.Reject", source);
        Assert.Contains("revenue_already_decided", source);
        Assert.Contains("MarkCustomer(\"manual_revenue\"", source);
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
