using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Agents;
using Clawbot.Infrastructure.Email;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests;

// M23/M24/M25 — notification center, agent toggle gate, cost ledger, SMTP sender.
public sealed class BackendAdminOpsTests
{
    private static IServiceScopeFactory ScopeFactoryFor(AppDbContext db)
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(AppDbContext)).Returns(db);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    [Fact]
    public async Task Notification_persists_and_marks_read_once()
    {
        using var t = new TestAppDb();
        var n = Notification.Create(t.TenantId, null, "hot_lead", "Khách nóng", DateTimeOffset.UtcNow, "warning");
        t.Db.Notifications.Add(n);
        await t.Db.SaveChangesAsync();

        n.IsRead.Should().BeFalse();
        n.MarkRead(DateTimeOffset.UtcNow);
        var firstReadAt = n.ReadAt;
        n.MarkRead(DateTimeOffset.UtcNow.AddMinutes(5)); // idempotent
        n.ReadAt.Should().Be(firstReadAt); // second MarkRead is a no-op (checked in-memory)
        await t.Db.SaveChangesAsync();

        var loaded = await t.Db.Notifications.IgnoreQueryFilters().SingleAsync();
        loaded.IsRead.Should().BeTrue();
        loaded.ReadAt.Should().NotBeNull();
    }

    [Fact]
    public async Task LlmCostLedger_persists_and_sums()
    {
        using var t = new TestAppDb();
        var now = DateTimeOffset.UtcNow;
        t.Db.LlmCostLedger.Add(LlmCostEntry.Create(t.TenantId, "chat-agent", "claude", 100, 50, 0.01m, now));
        t.Db.LlmCostLedger.Add(LlmCostEntry.Create(t.TenantId, "chat-agent", "claude", 200, 80, 0.02m, now));
        await t.Db.SaveChangesAsync();

        // Sum client-side: SQLite SUM over decimal-as-TEXT drifts to float.
        var entries = await t.Db.LlmCostLedger.IgnoreQueryFilters().ToListAsync();
        entries.Sum(e => e.Usd).Should().Be(0.03m);
    }

    [Fact]
    public async Task DbLlmCostTracker_records_actual_spend_even_when_monthly_cap_is_exceeded()
    {
        using var t = new TestAppDb();
        var sut = new DbLlmCostTracker(ScopeFactoryFor(t.Db));
        var month = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await sut.RecordAsync(new CostEntry(t.TenantId, "chat", "claude", 100, 50, 199m, month), CancellationToken.None);
        await sut.RecordAsync(new CostEntry(t.TenantId, "chat", "claude", 100, 50, 2m, month.AddDays(1)), CancellationToken.None);

        var entries = await t.Db.LlmCostLedger.IgnoreQueryFilters().ToListAsync();
        entries.Should().HaveCount(2);
        entries.Sum(e => e.Usd).Should().Be(201m);

        var summary = await sut.SummaryAsync(t.TenantId, month, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(201m);
        summary.PercentUsed.Should().BeGreaterThan(1f);
    }

    [Fact]
    public async Task DbLlmCostTracker_stamps_session_id_for_per_run_cost()
    {
        using var t = new TestAppDb();
        var sut = new DbLlmCostTracker(ScopeFactoryFor(t.Db));
        var now = DateTimeOffset.UtcNow;
        var sessionId = Guid.NewGuid();

        await sut.RecordAsync(new CostEntry(t.TenantId, "content-agent", "claude", 100, 50, 0.02m, now, SessionId: sessionId), CancellationToken.None);
        await sut.RecordAsync(new CostEntry(t.TenantId, "research-agent", "claude", 80, 40, 0.03m, now, SessionId: sessionId), CancellationToken.None);
        await sut.RecordAsync(new CostEntry(t.TenantId, "chat-agent", "claude", 10, 5, 0.99m, now), CancellationToken.None); // ngoài run

        var entries = await t.Db.LlmCostLedger.IgnoreQueryFilters().ToListAsync();
        // Per-run cost = tổng đúng của session, không dính chi phí ngoài run.
        entries.Where(e => e.SessionId == sessionId).Sum(e => e.Usd).Should().Be(0.05m);
        entries.Single(e => e.AgentCode == "chat-agent").SessionId.Should().BeNull();
    }

    [Fact]
    public async Task DbLlmCostTracker_honors_per_tenant_cap()
    {
        var tenant = Clawbot.Domain.Tenants.Tenant.Create("acme", "Acme", "pro", DateTimeOffset.UtcNow);
        tenant.SetMonthlyCostCapUsd(50m);
        using var t = new TestAppDb(tenant.Id);
        t.Db.Tenants.Add(tenant);
        await t.Db.SaveChangesAsync();
        var sut = new DbLlmCostTracker(ScopeFactoryFor(t.Db));
        var month = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await sut.RecordAsync(new CostEntry(tenant.Id, "chat", "claude", 100, 50, 49m, month), CancellationToken.None);
        var denied = await sut.TryReserveAsync(tenant.Id, 2m, month.AddDays(1), CancellationToken.None);
        var summary = await sut.SummaryAsync(tenant.Id, month, CancellationToken.None);

        // Cap riêng $50 (không phải mặc định $200): 49 + 2 > 50 → chặn.
        denied.Allowed.Should().BeFalse();
        summary.CapUsd.Should().Be(50m);
    }

    [Fact]
    public async Task DbLlmCostTracker_reserves_and_releases_budget_through_ledger()
    {
        using var t = new TestAppDb();
        var sut = new DbLlmCostTracker(ScopeFactoryFor(t.Db));
        var month = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await sut.RecordAsync(new CostEntry(t.TenantId, "chat", "claude", 100, 50, 198m, month), CancellationToken.None);

        var allowed = await sut.TryReserveAsync(t.TenantId, 2m, month.AddDays(1), CancellationToken.None);
        var denied = await sut.TryReserveAsync(t.TenantId, 1m, month.AddDays(1), CancellationToken.None);

        allowed.Allowed.Should().BeTrue();
        allowed.ReservationId.Should().NotBeNull();
        denied.Allowed.Should().BeFalse();
        denied.Reason.Should().Be("cost_cap_midrun");
        (await sut.SummaryAsync(t.TenantId, month, CancellationToken.None)).MonthToDateUsd.Should().Be(200m);

        await sut.ReleaseReservationAsync(t.TenantId, allowed.ReservationId!.Value, CancellationToken.None);
        await sut.ReleaseReservationAsync(t.TenantId, allowed.ReservationId.Value, CancellationToken.None);

        var summary = await sut.SummaryAsync(t.TenantId, month, CancellationToken.None);
        summary.MonthToDateUsd.Should().Be(198m);
    }

    [Fact]
    public async Task DbLlmCostTracker_release_targets_only_requested_reservation_row()
    {
        using var t = new TestAppDb();
        var tracker = new DbLlmCostTracker(ScopeFactoryFor(t.Db));
        var guard = new Clawbot.Agents.Core.Orchestrator.OrchestratorCostGuard(tracker);
        var month = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        await tracker.RecordAsync(new CostEntry(t.TenantId, "chat", "claude", 100, 50, 10m, month), CancellationToken.None);
        var first = await guard.TryReserveAsync(t.TenantId, 2m, month.AddDays(1), CancellationToken.None);
        var second = await guard.TryReserveAsync(t.TenantId, 3m, month.AddDays(1), CancellationToken.None);

        await guard.AdjustReservationAsync(t.TenantId, first.ReservationId, CancellationToken.None);
        await guard.AdjustReservationAsync(t.TenantId, first.ReservationId, CancellationToken.None);

        var entries = await t.Db.LlmCostLedger.IgnoreQueryFilters().ToListAsync();
        entries.Single(e => e.Id == first.ReservationId).Usd.Should().Be(0m);
        entries.Single(e => e.Id == second.ReservationId).Usd.Should().Be(3m);
        entries.Single(e => e.AgentCode == "chat").Usd.Should().Be(10m);
    }

    [Fact]
    public async Task DbLlmCostTracker_applies_actual_cost_to_reservation_row()
    {
        using var t = new TestAppDb();
        var tracker = new DbLlmCostTracker(ScopeFactoryFor(t.Db));
        var month = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

        var reserved = await tracker.TryReserveAsync(t.TenantId, 2m, month, CancellationToken.None);
        await tracker.RecordAsync(new CostEntry(t.TenantId, "content-agent", "claude", 10, 20, 0.50m, month, reserved.ReservationId), CancellationToken.None);
        await tracker.ReleaseReservationAsync(t.TenantId, reserved.ReservationId!.Value, CancellationToken.None);

        var entry = await t.Db.LlmCostLedger.IgnoreQueryFilters().SingleAsync();
        entry.AgentCode.Should().Be("content-agent");
        entry.Model.Should().Be("claude");
        entry.Usd.Should().Be(0.50m);
    }

    [Fact]
    public async Task DbAgentToggleGate_enabled_unless_stopped()
    {
        using var t = new TestAppDb();
        var gate = new DbAgentToggleGate(ScopeFactoryFor(t.Db));

        // No config row → enabled by default.
        (await gate.IsAutoActionEnabledAsync(t.TenantId, "chat")).Should().BeTrue();

        var agent = AgentConfig.Create(t.TenantId, "chat-agent", "Chat", "chat", "claude", DateTimeOffset.UtcNow);
        t.Db.AgentConfigs.Add(agent);
        await t.Db.SaveChangesAsync();

        // Default status is "stopped" → disabled.
        (await gate.IsAutoActionEnabledAsync(t.TenantId, "chat")).Should().BeFalse();

        agent.Start();
        await t.Db.SaveChangesAsync();
        (await gate.IsAutoActionEnabledAsync(t.TenantId, "chat")).Should().BeTrue();
    }

    [Fact]
    public async Task SmtpEmailSender_noops_when_unconfigured()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new SmtpOptions()); // Host null → no-op
        var sender = new SmtpEmailSender(options, NullLogger<SmtpEmailSender>.Instance);

        var act = async () => await sender.SendAsync("user@example.com", "subject", "body");
        await act.Should().NotThrowAsync();
    }
}
