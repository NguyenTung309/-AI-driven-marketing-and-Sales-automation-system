using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class OrchestratorCostGuardTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public async Task CanStartAsync_rejects_estimate_over_remaining_cap()
    {
        var tracker = new FixedSummaryTracker(new CostSummary(TenantId, 199m, 200m, 0.995f));
        var guard = new OrchestratorCostGuard(tracker);

        var result = await guard.CanStartAsync(TenantId, 2m, DateTimeOffset.UtcNow, CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("cost_cap_preflight");
    }

    [Fact]
    public async Task TryReserveAsync_allows_cost_within_remaining_cap()
    {
        var tracker = new FixedSummaryTracker(new CostSummary(TenantId, 10m, 200m, 0.05f));
        var guard = new OrchestratorCostGuard(tracker);

        var result = await guard.TryReserveAsync(TenantId, 5m, DateTimeOffset.UtcNow, CancellationToken.None);

        result.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseReservationAsync_frees_reserved_budget()
    {
        var tracker = new FixedSummaryTracker(new CostSummary(TenantId, 198m, 200m, 0.99f));
        var guard = new OrchestratorCostGuard(tracker);
        var at = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

        var reservation = await guard.TryReserveAsync(TenantId, 2m, at, CancellationToken.None);
        reservation.Allowed.Should().BeTrue();
        (await guard.TryReserveAsync(TenantId, 1m, at, CancellationToken.None)).Allowed.Should().BeFalse();

        await guard.ReleaseReservationAsync(TenantId, reservation.ReservationId, CancellationToken.None);

        (await guard.TryReserveAsync(TenantId, 1m, at, CancellationToken.None)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task AdjustReservationAsync_replaces_estimate_with_actual_cost()
    {
        var tracker = new FixedSummaryTracker(new CostSummary(TenantId, 198m, 200m, 0.99f));
        var guard = new OrchestratorCostGuard(tracker);
        var at = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

        var reservation = await guard.TryReserveAsync(TenantId, 1.5m, at, CancellationToken.None);
        reservation.Allowed.Should().BeTrue();
        await guard.AdjustReservationAsync(TenantId, reservation.ReservationId, CancellationToken.None);

        (await guard.TryReserveAsync(TenantId, 1.5m, at, CancellationToken.None)).Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task TryReserveAsync_refreshes_month_to_date_spend_for_later_reservations()
    {
        var tracker = new SequencedSummaryTracker([
            new CostSummary(TenantId, 10m, 200m, 0.05f),
            new CostSummary(TenantId, 199.75m, 200m, 0.99875f),
        ]);
        var guard = new OrchestratorCostGuard(tracker);
        var at = new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero);

        (await guard.TryReserveAsync(TenantId, 0.10m, at, CancellationToken.None)).Allowed.Should().BeTrue();
        var result = await guard.TryReserveAsync(TenantId, 0.20m, at, CancellationToken.None);

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be("cost_cap_midrun");
    }

    [Fact]
    public async Task TryReserveAsync_serializes_concurrent_reservations_per_tenant_month()
    {
        var tracker = new FixedSummaryTracker(new CostSummary(TenantId, 198m, 200m, 0.99f));
        var guard = new OrchestratorCostGuard(tracker);

        var results = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => guard.TryReserveAsync(TenantId, 1m, new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), CancellationToken.None)));

        results.Count(result => result.Allowed).Should().Be(2);
        results.Count(result => !result.Allowed).Should().Be(1);
    }

    private sealed class FixedSummaryTracker(CostSummary summary) : ILlmCostTracker, ILlmCostReservationStore
    {
        private readonly object _gate = new();
        private decimal _reserved;
        private readonly Dictionary<Guid, decimal> _reservations = [];

        public string Name => "cost";

        public Task RecordAsync(CostEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct) =>
            Task.FromResult(summary);

        public Task<CostReservationResult> TryReserveAsync(Guid tenantId, decimal estimatedUsd, DateTimeOffset at, CancellationToken ct)
        {
            var cost = Math.Max(0m, estimatedUsd);
            lock (_gate)
            {
                if (summary.MonthToDateUsd + _reserved + cost > summary.CapUsd)
                    return Task.FromResult(CostReservationResult.Deny("cost_cap_midrun"));

                var reservationId = Guid.NewGuid();
                _reserved += cost;
                _reservations[reservationId] = cost;
                return Task.FromResult(CostReservationResult.Allow(reservationId));
            }
        }

        public Task ReleaseReservationAsync(Guid tenantId, Guid reservationId, CancellationToken ct)
        {
            lock (_gate)
            {
                if (_reservations.Remove(reservationId, out var cost))
                    _reserved = Math.Max(0m, _reserved - cost);
                return Task.CompletedTask;
            }
        }
    }

    private sealed class SequencedSummaryTracker(IReadOnlyList<CostSummary> summaries) : ILlmCostTracker, ILlmCostReservationStore
    {
        private readonly object _gate = new();
        private int _index;
        private decimal _reserved;
        private readonly Dictionary<Guid, decimal> _reservations = [];

        public string Name => "cost";
        public Task RecordAsync(CostEntry entry, CancellationToken ct) => Task.CompletedTask;

        public Task<CostSummary> SummaryAsync(Guid tenantId, DateTimeOffset month, CancellationToken ct)
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, summaries.Count - 1);
            return Task.FromResult(summaries[index]);
        }

        public Task<CostReservationResult> TryReserveAsync(Guid tenantId, decimal estimatedUsd, DateTimeOffset at, CancellationToken ct)
        {
            var cost = Math.Max(0m, estimatedUsd);
            lock (_gate)
            {
                var index = Math.Min(Interlocked.Increment(ref _index) - 1, summaries.Count - 1);
                var summary = summaries[index];
                if (summary.MonthToDateUsd + _reserved + cost > summary.CapUsd)
                    return Task.FromResult(CostReservationResult.Deny("cost_cap_midrun"));

                var reservationId = Guid.NewGuid();
                _reserved += cost;
                _reservations[reservationId] = cost;
                return Task.FromResult(CostReservationResult.Allow(reservationId));
            }
        }

        public Task ReleaseReservationAsync(Guid tenantId, Guid reservationId, CancellationToken ct)
        {
            lock (_gate)
            {
                if (_reservations.Remove(reservationId, out var cost))
                    _reserved = Math.Max(0m, _reserved - cost);
                return Task.CompletedTask;
            }
        }
    }
}
