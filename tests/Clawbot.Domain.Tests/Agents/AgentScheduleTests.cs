using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static AgentSchedule CreateDefault() => AgentSchedule.Create(
        TenantId,
        "daily-trend-scan",
        "Scan trending topics",
        "daily",
        "0 8 * * *",
        "Asia/Ho_Chi_Minh",
        Now.AddHours(1),
        requiresApproval: false,
        createdAt: Now);

    [Fact]
    public void Create_SetsInitialDefaults()
    {
        var schedule = CreateDefault();

        schedule.TenantId.Should().Be(TenantId);
        schedule.Name.Should().Be("daily-trend-scan");
        schedule.GoalTemplate.Should().Be("Scan trending topics");
        schedule.Cadence.Should().Be("daily");
        schedule.CronExpression.Should().Be("0 8 * * *");
        schedule.TimezoneId.Should().Be("Asia/Ho_Chi_Minh");
        schedule.IsActive.Should().BeTrue();
        schedule.OverlapPolicy.Should().Be("skip");
        schedule.MisfirePolicy.Should().Be("skip_missed");
        schedule.TriggerType.Should().Be("cadence");
        schedule.EventKey.Should().BeNull();
        schedule.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_NormalizesWhitespaceAndCasing()
    {
        var schedule = AgentSchedule.Create(
            TenantId, "  Daily Scan  ", " Goal ", " DAILY ", null, " UTC ",
            Now, false, Now, overlapPolicy: " SKIP ", misfirePolicy: " RUN_ALL ");

        schedule.Name.Should().Be("Daily Scan");
        schedule.GoalTemplate.Should().Be("Goal");
        schedule.Cadence.Should().Be("daily");
        schedule.CronExpression.Should().BeNull();
        schedule.OverlapPolicy.Should().Be("skip");
        schedule.MisfirePolicy.Should().Be("run_all");
    }

    [Fact]
    public void Create_SetsEventTriggerFields()
    {
        var schedule = AgentSchedule.Create(
            TenantId, "inbox-event", "Process inbox", "event", null, "UTC",
            Now, false, Now, triggerType: "event", eventKey: "INBOX_MESSAGE");

        schedule.TriggerType.Should().Be("event");
        schedule.EventKey.Should().Be("inbox_message");
    }

    [Fact]
    public void UpdateSchedule_UpdatesAllFields()
    {
        var schedule = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        schedule.UpdateSchedule(
            "weekly-scan", "New goal", "weekly", "0 9 * * 1",
            "UTC", Now.AddDays(7), true, "allow", "run_all",
            "{\"maxCost\":10}", updatedAt);

        schedule.Name.Should().Be("weekly-scan");
        schedule.GoalTemplate.Should().Be("New goal");
        schedule.Cadence.Should().Be("weekly");
        schedule.RequiresApproval.Should().BeTrue();
        schedule.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void Activate_SetsIsActiveTrue()
    {
        var schedule = CreateDefault();
        schedule.Pause(Now);

        schedule.Activate(Now.AddMinutes(1));

        schedule.IsActive.Should().BeTrue();
        schedule.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Pause_SetsIsActiveFalse()
    {
        var schedule = CreateDefault();

        schedule.Pause(Now.AddMinutes(1));

        schedule.IsActive.Should().BeFalse();
        schedule.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void RecordRun_UpdatesLastRunAndNextRun()
    {
        var schedule = CreateDefault();
        var runTime = Now.AddHours(1);
        var nextRun = Now.AddHours(25);

        schedule.RecordRun(runTime, nextRun, runTime);

        schedule.LastRunAt.Should().Be(runTime);
        schedule.NextRunAt.Should().Be(nextRun);
    }

    [Fact]
    public void Reschedule_UpdatesNextRunOnly()
    {
        var schedule = CreateDefault();
        var newNext = Now.AddDays(3);

        schedule.Reschedule(newNext, Now.AddMinutes(1));

        schedule.NextRunAt.Should().Be(newNext);
        schedule.LastRunAt.Should().BeNull();
    }

    [Fact]
    public void Archive_SoftDeletesAndDeactivates()
    {
        var schedule = CreateDefault();

        schedule.Archive(Now.AddMinutes(5));

        schedule.DeletedAt.Should().Be(Now.AddMinutes(5));
        schedule.IsActive.Should().BeFalse();
        schedule.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }
}
