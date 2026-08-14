using System.Security.Claims;
using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Infrastructure.Security;
using FluentAssertions;
using Grpc.Core;

namespace Clawbot.Infrastructure.Tests;

public sealed class AgentServiceTenantBindingTests
{
    [Fact]
    public void EnsurePrincipalMatchesRequest_AllowsMatchingTenant()
    {
        var tenantId = Guid.NewGuid();
        var principal = CreatePrincipal(tenantId);
        var request = new DraftRequest { TenantId = tenantId.ToString("D") };

        var act = () => AgentServiceTenantBinding.EnsurePrincipalMatchesRequest(
            principal,
            request);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsurePrincipalMatchesRequest_RejectsMismatchedTenant()
    {
        var principal = CreatePrincipal(Guid.NewGuid());
        var request = new DraftRequest { TenantId = Guid.NewGuid().ToString("D") };

        var act = () => AgentServiceTenantBinding.EnsurePrincipalMatchesRequest(
            principal,
            request);

        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.PermissionDenied &&
                exception.Status.Detail == "agent_service_tenant_mismatch");
    }

    [Fact]
    public void EnsurePrincipalMatchesRequest_RejectsInvalidRequestTenant()
    {
        var principal = CreatePrincipal(Guid.NewGuid());
        var request = new DraftRequest();

        var act = () => AgentServiceTenantBinding.EnsurePrincipalMatchesRequest(
            principal,
            request);

        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.Unauthenticated &&
                exception.Status.Detail == "agent_service_tenant_required");
    }

    [Fact]
    public void EnsurePrincipalMatchesRequest_RejectsContractsWithoutTenantId()
    {
        var principal = CreatePrincipal(Guid.NewGuid());

        var act = () => AgentServiceTenantBinding.EnsurePrincipalMatchesRequest(
            principal,
            new object());

        act.Should().Throw<RpcException>()
            .Where(exception =>
                exception.StatusCode == StatusCode.Unauthenticated &&
                exception.Status.Detail == "agent_service_tenant_required");
    }

    private static ClaimsPrincipal CreatePrincipal(Guid tenantId) =>
        new(new ClaimsIdentity(
        [
            new Claim("tenant_id", tenantId.ToString("D")),
        ], "test"));
}
