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
    public async Task ClaudeCostLedger_persists_and_sums()
    {
        using var t = new TestAppDb();
        var now = DateTimeOffset.UtcNow;
        t.Db.ClaudeCostLedger.Add(ClaudeCostEntry.Create(t.TenantId, "chat-agent", "claude", 100, 50, 0.01m, now));
        t.Db.ClaudeCostLedger.Add(ClaudeCostEntry.Create(t.TenantId, "chat-agent", "claude", 200, 80, 0.02m, now));
        await t.Db.SaveChangesAsync();

        // Sum client-side: SQLite SUM over decimal-as-TEXT drifts to float.
        var entries = await t.Db.ClaudeCostLedger.IgnoreQueryFilters().ToListAsync();
        entries.Sum(e => e.Usd).Should().Be(0.03m);
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
        var config = new ConfigurationBuilder().Build(); // no Email:Smtp:Host
        var sender = new SmtpEmailSender(config, NullLogger<SmtpEmailSender>.Instance);

        var act = async () => await sender.SendAsync("user@example.com", "subject", "body");
        await act.Should().NotThrowAsync();
    }
}
