using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Contacts;
using Clawbot.Infrastructure.Audit;
using Clawbot.SharedKernel.Audit;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Audit;

// M03 — AuditSaveChangesInterceptor writes audit rows with PII-redacted diffs.
public sealed class AuditSaveChangesInterceptorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);

    private static AuditSaveChangesInterceptor Build(Guid tenantId, IPiiRedactor pii)
    {
        var audit = new StaticAuditContext(Guid.NewGuid());
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Current.Returns(new TenantContext(tenantId, "test"));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new AuditSaveChangesInterceptor(audit, tenants, pii, clock);
    }

    private static IPiiRedactor Passthrough()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(call => new RedactionResult(call.Arg<string>(), Array.Empty<PiiSpan>()));
        return pii;
    }

    [Fact]
    public async Task Records_audit_log_on_create()
    {
        var tenantId = Guid.NewGuid();
        using var fx = new TestAppDb(tenantId, Build(tenantId, Passthrough()));

        fx.Db.Contacts.Add(Contact.Create(tenantId, "John Doe", Now));
        await fx.Db.SaveChangesAsync();

        var logs = await fx.Db.AuditLogs.IgnoreQueryFilters().ToListAsync();
        logs.Should().ContainSingle();
        logs[0].Action.Should().Be("create");
        logs[0].ResourceType.Should().Be(nameof(Contact));
        logs[0].DiffJson.Should().Contain("John Doe");
    }

    [Fact]
    public async Task Redacts_string_values_in_diff()
    {
        var tenantId = Guid.NewGuid();
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(new RedactionResult("[REDACTED]", Array.Empty<PiiSpan>()));
        using var fx = new TestAppDb(tenantId, Build(tenantId, pii));

        fx.Db.Contacts.Add(Contact.Create(tenantId, "0912345678", Now));
        await fx.Db.SaveChangesAsync();

        var log = await fx.Db.AuditLogs.IgnoreQueryFilters().SingleAsync();
        log.DiffJson.Should().Contain("[REDACTED]");
        log.DiffJson.Should().NotContain("0912345678");
    }
}
