using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentScheduleTests
{
    [Fact]
    public void Create_PersistsInitiatorForScheduledOrchestrationAuthority()
    {
        var tenantId = Guid.NewGuid();
        var initiatorUserId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;

        var schedule = AgentSchedule.Create(
            tenantId,
            "Weekly content",
            "Create weekly content",
            "weekly",
            cronExpression: null,
            "Asia/Ho_Chi_Minh",
            createdAt.AddDays(7),
            requiresApproval: false,
            createdAt,
            initiatorUserId: initiatorUserId);

        schedule.TenantId.Should().Be(tenantId);
        schedule.InitiatorUserId.Should().Be(initiatorUserId);
    }

    [Fact]
    public void UpdateSchedule_PreservesOriginalInitiator()
    {
        var initiatorUserId = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var schedule = AgentSchedule.Create(
            Guid.NewGuid(),
            "Weekly content",
            "Create weekly content",
            "weekly",
            cronExpression: null,
            "Asia/Ho_Chi_Minh",
            createdAt.AddDays(7),
            requiresApproval: false,
            createdAt,
            initiatorUserId: initiatorUserId);

        schedule.UpdateSchedule(
            "Updated weekly content",
            "Create updated content",
            "weekly",
            cronExpression: null,
            "Asia/Ho_Chi_Minh",
            createdAt.AddDays(14),
            requiresApproval: true,
            overlapPolicy: "skip",
            misfirePolicy: "skip_missed",
            approvalPolicyJson: null,
            createdAt.AddMinutes(1));

        schedule.InitiatorUserId.Should().Be(initiatorUserId);
    }
}
