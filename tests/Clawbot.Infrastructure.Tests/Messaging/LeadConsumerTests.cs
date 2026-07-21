using Clawbot.Agents.Core.Lead;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Leads.Events;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Messaging;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Messaging;

public sealed class LeadBecameHotConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_assigns_unowned_hot_lead_to_least_busy_sale_and_notifies_owner()
    {
        using var fx = new TestAppDb();
        var lead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", Now.AddHours(-1));
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var ownerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var assignment = Substitute.For<ILeadAssignmentService>();
        assignment.PickOwnerAsync(fx.TenantId, Arg.Any<CancellationToken>()).Returns(ownerId);
        var publisher = new RecordingNotificationPublisher();
        var consumer = new LeadBecameHotConsumer(
            fx.Db,
            assignment,
            publisher,
            NullLogger<LeadBecameHotConsumer>.Instance);

        await consumer.Consume(Context(new LeadBecameHot(fx.TenantId, lead.Id, null, 72, Now)));

        var savedLead = await fx.Db.Leads.IgnoreQueryFilters().SingleAsync(l => l.Id == lead.Id);
        savedLead.OwnerUserId.Should().Be(ownerId);
        publisher.Requests.Should().ContainSingle(r =>
            r.Type == "hot_lead"
            && r.UserId == ownerId
            && r.TenantId == fx.TenantId
            && r.Link == $"/leads/{lead.Id}");
    }

    private static ConsumeContext<LeadBecameHot> Context(LeadBecameHot message)
    {
        var context = Substitute.For<ConsumeContext<LeadBecameHot>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}

public sealed class LeadBecameWarmConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_enrolls_warm_lead_once_into_default_warm_drip()
    {
        using var fx = new TestAppDb();
        var lead = Lead.Create(fx.TenantId, Guid.NewGuid(), "pancake", Now.AddHours(-1));
        var sequence = DripSequence.Create(fx.TenantId, "Nuôi dưỡng khách ấm", "warm_lead", Now.AddDays(-1));
        sequence.AddStep(1, 1, "pancake", "Xin chào {lead_name}");
        fx.Db.Leads.Add(lead);
        fx.Db.Set<DripSequence>().Add(sequence);
        await fx.Db.SaveChangesAsync();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var consumer = new LeadBecameWarmConsumer(
            fx.Db,
            clock,
            NullLogger<LeadBecameWarmConsumer>.Instance);
        var message = new LeadBecameWarm(fx.TenantId, lead.Id, 40, Now);

        await consumer.Consume(Context(message));
        await consumer.Consume(Context(message));

        var enrollment = await fx.Db.Set<DripEnrollment>().IgnoreQueryFilters().SingleAsync();
        enrollment.TenantId.Should().Be(fx.TenantId);
        enrollment.SequenceId.Should().Be(sequence.Id);
        enrollment.LeadId.Should().Be(lead.Id);
        enrollment.CurrentStep.Should().Be(0);
        enrollment.NextSendAt.Should().Be(Now.AddHours(1));
        enrollment.Status.Should().Be("active");
    }

    private static ConsumeContext<LeadBecameWarm> Context(LeadBecameWarm message)
    {
        var context = Substitute.For<ConsumeContext<LeadBecameWarm>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}

public sealed class LeadBecameCustomerConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_NotifiesAssignedOwner()
    {
        using var fx = new TestAppDb();
        var ownerId = Guid.NewGuid();
        var lead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", Now.AddHours(-1));
        lead.Assign(ownerId);
        lead.MarkCustomer("paid", Now);
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingNotificationPublisher();
        var recipients = Substitute.For<ILeadNotificationRecipientResolver>();
        recipients.ResolveAsync(fx.TenantId, ownerId, Arg.Any<CancellationToken>()).Returns(ownerId);
        var consumer = new LeadBecameCustomerConsumer(
            fx.Db,
            publisher,
            recipients,
            NullLogger<LeadBecameCustomerConsumer>.Instance);

        await consumer.Consume(Context(new LeadBecameCustomer(fx.TenantId, lead.Id, ownerId, 72, Now)));

        publisher.Requests.Should().ContainSingle(r =>
            r.Type == "lead_customer"
            && r.UserId == ownerId
            && r.TenantId == fx.TenantId
            && r.Link == $"/leads/{lead.Id}");
    }

    [Fact]
    public async Task Consume_WhenNoOwner_NotifiesAdminFallback_NotBroadcast()
    {
        using var fx = new TestAppDb();
        var adminId = Guid.NewGuid();
        var lead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", Now.AddHours(-1));
        lead.MarkCustomer("paid", Now);
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingNotificationPublisher();
        var recipients = Substitute.For<ILeadNotificationRecipientResolver>();
        recipients.ResolveAsync(fx.TenantId, null, Arg.Any<CancellationToken>()).Returns(adminId);
        var consumer = new LeadBecameCustomerConsumer(
            fx.Db,
            publisher,
            recipients,
            NullLogger<LeadBecameCustomerConsumer>.Instance);

        await consumer.Consume(Context(new LeadBecameCustomer(fx.TenantId, lead.Id, null, 50, Now)));

        publisher.Requests.Should().ContainSingle(r => r.UserId == adminId && r.Type == "lead_customer");
    }

    private static ConsumeContext<LeadBecameCustomer> Context(LeadBecameCustomer message)
    {
        var context = Substitute.For<ConsumeContext<LeadBecameCustomer>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}

public sealed class LeadReactivatedConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Consume_NotifiesAssignedOwnerWithWarning()
    {
        using var fx = new TestAppDb();
        var ownerId = Guid.NewGuid();
        var lead = Lead.Create(fx.TenantId, Guid.NewGuid(), "facebook", Now.AddHours(-1));
        lead.Assign(ownerId);
        fx.Db.Leads.Add(lead);
        await fx.Db.SaveChangesAsync();
        var publisher = new RecordingNotificationPublisher();
        var recipients = Substitute.For<ILeadNotificationRecipientResolver>();
        recipients.ResolveAsync(fx.TenantId, ownerId, Arg.Any<CancellationToken>()).Returns(ownerId);
        var consumer = new LeadReactivatedConsumer(
            fx.Db,
            publisher,
            recipients,
            NullLogger<LeadReactivatedConsumer>.Instance);

        await consumer.Consume(Context(new LeadReactivated(fx.TenantId, lead.Id, ownerId, 45, Now)));

        publisher.Requests.Should().ContainSingle(r =>
            r.Type == "lead_reactivated"
            && r.UserId == ownerId
            && r.Severity == "warning"
            && r.Link == $"/leads/{lead.Id}");
    }

    private static ConsumeContext<LeadReactivated> Context(LeadReactivated message)
    {
        var context = Substitute.For<ConsumeContext<LeadReactivated>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        return context;
    }
}

file sealed class RecordingNotificationPublisher : INotificationPublisher
{
    public List<NotificationRequest> Requests { get; } = [];

    public Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
    {
        _ = ct;
        Requests.Add(request);
        return Task.CompletedTask;
    }
}
