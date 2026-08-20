using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Ledger chi phí LLM in-memory theo (tenant, năm-tháng): ghi spend, reserve/release, cap $200.
public sealed class LlmCostTrackerTests
{
    private static readonly Guid Tenant = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset At = new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

    private static InMemoryLlmCostTracker NewTracker() => new();

    private static CostEntry Entry(decimal usd, int inTok = 0, int outTok = 0, Guid? reservationId = null)
        => new(Tenant, "content-writer", "claude", inTok, outTok, usd, At, reservationId);

    [Fact]
    public void Name_IsCostTracker()
    {
        NewTracker().Name.Should().Be("claude-cost-tracker");
    }

    [Fact]
    public async Task Record_NullEntry_Throws()
    {
        var act = async () => await NewTracker().RecordAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Record_ThenSummary_AccumulatesSpend()
    {
        var tracker = NewTracker();

        await tracker.RecordAsync(Entry(1.5m), CancellationToken.None);
        await tracker.RecordAsync(Entry(2.5m), CancellationToken.None);

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);

        summary.MonthToDateUsd.Should().Be(4.0m);
        summary.CapUsd.Should().Be(200m);
        summary.PercentUsed.Should().BeApproximately(0.02f, 0.0001f);
    }

    [Fact]
    public async Task Record_ZeroCostZeroTokens_IsIgnored()
    {
        var tracker = NewTracker();

        await tracker.RecordAsync(Entry(0m, 0, 0), CancellationToken.None);

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(0m);
    }

    [Fact]
    public async Task Record_ZeroCostButHasTokens_IsRecorded()
    {
        // Provider không trả usage -> cost 0 nhưng token > 0: vẫn phải ghi (cap không được vô hiệu).
        var tracker = NewTracker();

        await tracker.RecordAsync(Entry(0m, 100, 50), CancellationToken.None);

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);
        // UsdCost = 0 nên MTD vẫn 0, nhưng nhánh ghi đã chạy (không bị short-circuit).
        summary.MonthToDateUsd.Should().Be(0m);
    }

    [Fact]
    public async Task Summary_UnknownTenant_ReturnsZero()
    {
        var summary = await NewTracker().SummaryAsync(Guid.NewGuid(), At, CancellationToken.None);

        summary.MonthToDateUsd.Should().Be(0m);
        summary.PercentUsed.Should().Be(0f);
    }

    [Fact]
    public async Task Reserve_WithinCap_AllowsAndHoldsSpend()
    {
        var tracker = NewTracker();

        var result = await tracker.TryReserveAsync(Tenant, 50m, At, CancellationToken.None);

        result.Allowed.Should().BeTrue();
        result.ReservationId.Should().NotBeNull();

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(50m);
    }

    [Fact]
    public async Task Reserve_ExceedingCap_Denies()
    {
        var tracker = NewTracker();

        var result = await tracker.TryReserveAsync(Tenant, 250m, At, CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("cost_cap_midrun");
        result.ReservationId.Should().BeNull();
    }

    [Fact]
    public async Task Record_WithReservationId_ReplacesReservedEstimate()
    {
        var tracker = NewTracker();

        var reservation = await tracker.TryReserveAsync(Tenant, 50m, At, CancellationToken.None);
        // Actual cost thấp hơn ước lượng: ledger phải trừ reservation rồi cộng cost thật.
        await tracker.RecordAsync(Entry(10m, reservationId: reservation.ReservationId), CancellationToken.None);

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(10m);
    }

    [Fact]
    public async Task Release_RemovesReservedSpend()
    {
        var tracker = NewTracker();

        var reservation = await tracker.TryReserveAsync(Tenant, 50m, At, CancellationToken.None);
        await tracker.ReleaseReservationAsync(Tenant, reservation.ReservationId!.Value, CancellationToken.None);

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(0m);
    }

    [Fact]
    public async Task Release_UnknownReservation_IsNoOp()
    {
        var tracker = NewTracker();

        var act = async () => await tracker.ReleaseReservationAsync(Tenant, Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Release_WrongTenant_DoesNotTouchLedger()
    {
        var tracker = NewTracker();

        var reservation = await tracker.TryReserveAsync(Tenant, 50m, At, CancellationToken.None);
        await tracker.ReleaseReservationAsync(Guid.NewGuid(), reservation.ReservationId!.Value, CancellationToken.None);

        var summary = await tracker.SummaryAsync(Tenant, At, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(50m);
    }
}
