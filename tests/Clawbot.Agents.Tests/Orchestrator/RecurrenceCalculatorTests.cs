using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class RecurrenceCalculatorTests
{
    // Helpers: chon timezone on dinh de ket qua lap lai tren moi may
    private const string TzUtc = "UTC";
    private static readonly TimeSpan TzOffset = TimeSpan.Zero;

    // --- NextRunUtc: 4 cadence co ban ---
    [Theory]
    [InlineData("daily")]
    [InlineData("Daily")]
    [InlineData(" DAILY ")]
    [InlineData("DaIlY")]
    public void NextRunUtc_Daily_AddsOneLocalDay(string cadence)
    {
        var from = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.NextRunUtc(cadence, from, TzUtc);
        next.Should().Be(new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_Weekly_AddsSevenDays()
    {
        var from = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.NextRunUtc("weekly", from, TzUtc);
        next.Should().Be(new DateTimeOffset(2026, 3, 22, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_Monthly_ClampsDay_WhenNextMonthShorter()
    {
        // 31/01 -> 28/02 (2026 khong nhuan)
        var from = new DateTimeOffset(2026, 1, 31, 9, 30, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.NextRunUtc("monthly", from, TzUtc);
        next.Should().Be(new DateTimeOffset(2026, 2, 28, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_Monthly_KeepsDay_WhenNextMonthHasIt()
    {
        var from = new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.NextRunUtc("monthly", from, TzUtc);
        next.Should().Be(new DateTimeOffset(2026, 2, 15, 9, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_Quarterly_AddsThreeMonths_Clamped()
    {
        var from = new DateTimeOffset(2026, 1, 31, 8, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.NextRunUtc("quarterly", from, TzUtc);
        next.Should().Be(new DateTimeOffset(2026, 4, 30, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_Quarterly_NormalCase()
    {
        var from = new DateTimeOffset(2026, 2, 10, 8, 0, 0, TimeSpan.Zero);
        var next = RecurrenceCalculator.NextRunUtc("quarterly", from, TzUtc);
        next.Should().Be(new DateTimeOffset(2026, 5, 10, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void NextRunUtc_Unsupported_Throws()
    {
        var from = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        FluentActions.Invoking(() => RecurrenceCalculator.NextRunUtc("hourly", from, TzUtc))
            .Should().Throw<ArgumentOutOfRangeException>()
            .WithMessage("*Unsupported cadence*");
    }

    [Fact]
    public void NextRunUtc_InvalidTimezone_Throws()
    {
        var from = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        FluentActions.Invoking(() => RecurrenceCalculator.NextRunUtc("daily", from, "No/Such_Zone"))
            .Should().Throw<TimeZoneNotFoundException>();
    }

    // --- WindowKey: 4 cadence ---
    [Fact]
    public void WindowKey_Daily_Format()
    {
        var due = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        RecurrenceCalculator.WindowKey("daily", due, TzUtc).Should().Be("daily:2026-03-15");
    }

    [Fact]
    public void WindowKey_Weekly_Format_IsoWeek()
    {
        // 2026-03-15 is Sunday, ISO week 11 of 2026
        var due = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        var key = RecurrenceCalculator.WindowKey("weekly", due, TzUtc);
        key.Should().StartWith("weekly:2026-W");
    }

    [Fact]
    public void WindowKey_Monthly_Format()
    {
        var due = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        RecurrenceCalculator.WindowKey("monthly", due, TzUtc).Should().Be("monthly:2026-03");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    [InlineData(7, 3)]
    [InlineData(10, 4)]
    public void WindowKey_Quarterly_QuarterNumber(int month, int expectedQ)
    {
        var due = new DateTimeOffset(2026, month, 15, 10, 0, 0, TimeSpan.Zero);
        RecurrenceCalculator.WindowKey("quarterly", due, TzUtc).Should().Be($"quarterly:2026-Q{expectedQ}");
    }

    [Fact]
    public void WindowKey_Unsupported_Throws()
    {
        var due = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        FluentActions.Invoking(() => RecurrenceCalculator.WindowKey("hourly", due, TzUtc))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void WindowKey_Trims_And_Lowercases_Cadence()
    {
        var due = new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        RecurrenceCalculator.WindowKey(" DAILY ", due, TzUtc).Should().Be("daily:2026-03-15");
    }
}
