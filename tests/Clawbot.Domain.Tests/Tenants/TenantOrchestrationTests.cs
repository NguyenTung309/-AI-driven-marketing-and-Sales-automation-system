using Clawbot.Domain.Tenants;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Tenants;

public sealed class TenantOrchestrationTests
{
    [Fact]
    public void New_tenant_allows_orchestration_auto_run_by_default()
    {
        var tenant = Tenant.Create("demo", "Demo", "free", DateTimeOffset.UtcNow);

        tenant.RequireOrchestrationApproval.Should().BeFalse();
    }

    [Fact]
    public void SetRequireOrchestrationApproval_updates_tenant_toggle()
    {
        var tenant = Tenant.Create("demo", "Demo", "free", DateTimeOffset.UtcNow);

        tenant.SetRequireOrchestrationApproval(true);

        tenant.RequireOrchestrationApproval.Should().BeTrue();
    }
}
