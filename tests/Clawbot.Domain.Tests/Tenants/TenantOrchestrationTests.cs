using System.Reflection;
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

    [Fact]
    public void New_tenant_uses_default_lead_lifecycle_settings()
    {
        var tenant = Tenant.Create("demo", "Demo", "free", DateTimeOffset.UtcNow);

        tenant.LeadLostAfterDays.Should().Be(60);
        tenant.AutoApproveLeadRevenue.Should().BeFalse();
    }

    [Theory]
    [InlineData(-1, 60)]
    [InlineData(0, 0)]
    [InlineData(30, 30)]
    [InlineData(400, 365)]
    public void SetLeadLostAfterDays_normalizes_value(int requested, int expected)
    {
        var tenant = Tenant.Create("demo", "Demo", "free", DateTimeOffset.UtcNow);

        tenant.SetLeadLostAfterDays(requested);

        tenant.LeadLostAfterDays.Should().Be(expected);
    }

    [Fact]
    public void SetAutoApproveLeadRevenue_updates_tenant_toggle()
    {
        var tenant = Tenant.Create("demo", "Demo", "free", DateTimeOffset.UtcNow);

        tenant.SetAutoApproveLeadRevenue(true);

        tenant.AutoApproveLeadRevenue.Should().BeTrue();
    }

    [Fact]
    public void New_tenant_defaults_content_publishing_policy_to_human_required()
    {
        var createdAt = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create("demo", "Demo", "free", createdAt);

        ReadRequiredProperty<string>(tenant, "ContentPublishingApprovalPolicy")
            .Should().Be("human_required");
        ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion").Should().Be(1);
        ReadRequiredProperty<DateTimeOffset>(tenant, "ContentPublishingPolicyUpdatedAt")
            .Should().Be(createdAt);
    }

    [Theory]
    [InlineData("automatic")]
    [InlineData("human_required")]
    public void SetContentPublishingApprovalPolicy_accepts_only_supported_policy_values(string policy)
    {
        var createdAt = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var updatedAt = createdAt.AddMinutes(5);
        var tenant = Tenant.Create("demo", "Demo", "free", createdAt);
        if (policy == "human_required")
        {
            InvokeRequired(
                tenant,
                "SetContentPublishingApprovalPolicy",
                "automatic",
                createdAt.AddMinutes(1));
        }

        var initialVersion = ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion");

        InvokeRequired(tenant, "SetContentPublishingApprovalPolicy", policy, updatedAt);

        ReadRequiredProperty<string>(tenant, "ContentPublishingApprovalPolicy")
            .Should().Be(policy);
        ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion")
            .Should().Be(initialVersion + 1);
        ReadRequiredProperty<DateTimeOffset>(tenant, "ContentPublishingPolicyUpdatedAt")
            .Should().Be(updatedAt);
    }

    [Fact]
    public void SetContentPublishingApprovalPolicy_same_value_is_idempotent()
    {
        var createdAt = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create("demo", "Demo", "free", createdAt);
        var initialVersion = ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion");
        var initialUpdatedAt = ReadRequiredProperty<DateTimeOffset>(
            tenant,
            "ContentPublishingPolicyUpdatedAt");

        InvokeRequired(
            tenant,
            "SetContentPublishingApprovalPolicy",
            "human_required",
            createdAt.AddHours(1));

        ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion")
            .Should().Be(initialVersion);
        ReadRequiredProperty<DateTimeOffset>(tenant, "ContentPublishingPolicyUpdatedAt")
            .Should().Be(initialUpdatedAt);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("manual")]
    public void SetContentPublishingApprovalPolicy_rejects_unknown_policy_values(string policy)
    {
        var createdAt = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create("demo", "Demo", "free", createdAt);

        var act = () => InvokeRequired(
            tenant,
            "SetContentPublishingApprovalPolicy",
            policy,
            createdAt.AddHours(1));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeAssignableTo<ArgumentException>();
        ReadRequiredProperty<string>(tenant, "ContentPublishingApprovalPolicy")
            .Should().Be("human_required");
        ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion").Should().Be(1);
        ReadRequiredProperty<DateTimeOffset>(tenant, "ContentPublishingPolicyUpdatedAt")
            .Should().Be(createdAt);
    }

    [Fact]
    public void SetContentPublishingApprovalPolicy_overflow_does_not_partially_mutate_tenant()
    {
        var createdAt = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var tenant = Tenant.Create("demo", "Demo", "free", createdAt);
        SetRequiredProperty(tenant, "ContentPublishingPolicyVersion", long.MaxValue);

        var act = () => InvokeRequired(
            tenant,
            "SetContentPublishingApprovalPolicy",
            "automatic",
            createdAt.AddHours(1));

        act.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<OverflowException>();
        ReadRequiredProperty<string>(tenant, "ContentPublishingApprovalPolicy")
            .Should().Be("human_required");
        ReadRequiredProperty<long>(tenant, "ContentPublishingPolicyVersion")
            .Should().Be(long.MaxValue);
        ReadRequiredProperty<DateTimeOffset>(tenant, "ContentPublishingPolicyUpdatedAt")
            .Should().Be(createdAt);
    }

    private static T ReadRequiredProperty<T>(object target, string propertyName)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        property.Should().NotBeNull($"{target.GetType().Name} must expose {propertyName}");

        var value = property!.GetValue(target);
        value.Should().BeAssignableTo<T>();
        return (T)value!;
    }

    private static void SetRequiredProperty(object target, string propertyName, object value)
    {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        property.Should().NotBeNull($"{target.GetType().Name} must expose {propertyName}");
        var setter = property!.GetSetMethod(nonPublic: true);
        setter.Should().NotBeNull($"{propertyName} must have a setter for this boundary test");
        setter!.Invoke(target, [value]);
    }

    private static object? InvokeRequired(object target, string methodName, params object?[] arguments)
    {
        var method = target.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .SingleOrDefault(candidate =>
                candidate.Name == methodName
                && candidate.GetParameters().Length == arguments.Length);
        method.Should().NotBeNull($"{target.GetType().Name} must expose {methodName}");

        return method!.Invoke(target, arguments);
    }
}
