using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class RecurrenceCalculatorTests
{
    [Fact]
    public void NextRunUtc_AddsOneDay_ForDailySchedule()
    {
        var from = new DateTimeOffset(2026, 6, 24, 9, 30, 0, TimeSpan.Zero);

        var next = RecurrenceCalculator.NextRunUtc("daily", from, "UTC");

        next.Should().Be(new DateTimeOffset(2026, 6, 25, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_ClampsMonthlySchedule_ToLastValidDay()
    {
        var from = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);

        var next = RecurrenceCalculator.NextRunUtc("monthly", from, "UTC");

        next.Should().Be(new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_AddsThreeMonths_ForQuarterlySchedule()
    {
        var from = new DateTimeOffset(2026, 2, 28, 8, 0, 0, TimeSpan.Zero);

        var next = RecurrenceCalculator.NextRunUtc("quarterly", from, "UTC");

        next.Should().Be(new DateTimeOffset(2026, 5, 28, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void WindowKey_UsesTenantLocalPeriod()
    {
        var due = new DateTimeOffset(2026, 6, 24, 17, 30, 0, TimeSpan.Zero);

        var daily = RecurrenceCalculator.WindowKey("daily", due, "UTC");
        var weekly = RecurrenceCalculator.WindowKey("weekly", due, "UTC");
        var monthly = RecurrenceCalculator.WindowKey("monthly", due, "UTC");
        var quarterly = RecurrenceCalculator.WindowKey("quarterly", due, "UTC");

        daily.Should().Be("daily:2026-06-24");
        weekly.Should().Be("weekly:2026-W26");
        monthly.Should().Be("monthly:2026-06");
        quarterly.Should().Be("quarterly:2026-Q2");
    }

    [Fact]
    public void WindowKey_RejectsUnknownCadence()
    {
        var act = () => RecurrenceCalculator.WindowKey("yearly", DateTimeOffset.UtcNow, "UTC");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
