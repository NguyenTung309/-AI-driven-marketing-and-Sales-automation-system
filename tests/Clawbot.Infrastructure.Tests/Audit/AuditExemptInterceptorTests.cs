using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Audit;
using Clawbot.SharedKernel.Audit;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Audit;

public sealed class AuditExemptInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 17, 0, 0, 0, TimeSpan.Zero);

    private static AuditSaveChangesInterceptor Build(Guid tenantId)
    {
        var audit = new StaticAuditContext(Guid.NewGuid());
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Current.Returns(new TenantContext(tenantId, "test"));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new RedactionResult(call.Arg<string>(), Array.Empty<PiiSpan>()));
        return new AuditSaveChangesInterceptor(audit, tenants, pii, clock);
    }

    [Fact]
    public async Task Skips_audit_for_IAuditExempt_entities()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new TestAppDb(tenantId, Build(tenantId));

        // Notification implements IAuditExempt — must not create audit_logs rows.
        // (same shape as RetentionPurgeJobTests — no tenant FK required on notifications.)
        fx.Db.Notifications.Add(Notification.Create(tenantId, null, "system", "exempt-check", Now));
        await fx.Db.SaveChangesAsync();

        var logs = await fx.Db.AuditLogs.IgnoreQueryFilters()
            .Where(a => a.ResourceType == nameof(Notification))
            .ToListAsync();
        logs.Should().BeEmpty();
    }

    [Fact]
    public async Task Still_audits_normal_entities()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new TestAppDb(tenantId, Build(tenantId));

        fx.Db.Contacts.Add(Contact.Create(tenantId, "Ada", Now));
        await fx.Db.SaveChangesAsync();

        var logs = await fx.Db.AuditLogs.IgnoreQueryFilters().ToListAsync();
        logs.Should().ContainSingle();
        logs[0].ResourceType.Should().Be(nameof(Contact));
    }
}
