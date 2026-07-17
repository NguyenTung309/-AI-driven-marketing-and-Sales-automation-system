using Clawbot.Domain.Conversations;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Notifications;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class IdleConversationAlertJobTests
{
    [Fact]
    public async Task RunAsync_escalates_10_minute_idle_conversation_to_sales_lead_users()
    {
        var tenant = Tenant.Create("idle-default", "Idle Default", "free", DateTimeOffset.UtcNow);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var assignedSaleId = Guid.NewGuid();
        var salesLeadA = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var salesLeadB = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var now = DateTimeOffset.UtcNow;
        var conversation = Conversation.Open(fx.TenantId, "pancake", "thread-1", now.AddMinutes(-30));
        conversation.Assign(assignedSaleId);
        conversation.AppendMessage("in", "customer", "Cần tư vấn thêm", "text", now.AddMinutes(-11));
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var inbox = Substitute.For<IInboxNotifier>();
        var publisher = new RecordingNotificationPublisher();
        var salesLeadResolver = new RecordingIdleEscalationRecipientResolver(salesLeadA, salesLeadB);
        var job = new IdleConversationAlertJob(
            fx.Db,
            inbox,
            publisher,
            salesLeadResolver,
            NullLogger<IdleConversationAlertJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        salesLeadResolver.Requests.Should().ContainSingle().Which.Should().Be(fx.TenantId);
        publisher.Requests.Should().Contain(r =>
            r.Type == "idle"
            && r.UserId == assignedSaleId
            && r.Link == $"/conversations/{conversation.Id}");
        publisher.Requests.Where(r => r.Type == "idle_escalation")
            .Select(r => r.UserId)
            .Should().BeEquivalentTo([salesLeadA, salesLeadB]);
        publisher.Requests.Should().NotContain(r => r.Type == "idle_escalation" && r.UserId == null);
    }

    [Fact]
    public async Task RunAsync_uses_tenant_configured_idle_threshold()
    {
        var tenant = Tenant.Create("idle-20", "Idle 20", "free", DateTimeOffset.UtcNow);
        tenant.SetIdleAlertMinutes(20);
        using var fx = new TestAppDb(tenant.Id);
        fx.Db.Tenants.Add(tenant);
        var now = DateTimeOffset.UtcNow;
        // 11' im lặng: dưới ngưỡng 20' -> im; 41' im lặng: quá ngưỡng + trong band escalate (40'..42').
        var below = Conversation.Open(fx.TenantId, "pancake", "thread-below", now.AddMinutes(-30));
        below.AppendMessage("in", "customer", "Hỏi giá", "text", now.AddMinutes(-11));
        var past = Conversation.Open(fx.TenantId, "pancake", "thread-past", now.AddHours(-2));
        past.AppendMessage("in", "customer", "Cần tư vấn", "text", now.AddMinutes(-41));
        fx.Db.Conversations.AddRange(below, past);
        await fx.Db.SaveChangesAsync();

        var publisher = new RecordingNotificationPublisher();
        var job = new IdleConversationAlertJob(
            fx.Db,
            Substitute.For<IInboxNotifier>(),
            publisher,
            new RecordingIdleEscalationRecipientResolver(),
            NullLogger<IdleConversationAlertJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        publisher.Requests.Should().NotContain(r => r.Link == $"/conversations/{below.Id}");
        publisher.Requests.Should().Contain(r => r.Type == "idle" && r.Link == $"/conversations/{past.Id}");
        publisher.Requests.Should().Contain(r =>
            r.Type == "idle_escalation" && r.Link == $"/conversations/{past.Id}" && r.Title.Contains("40 phút"));
    }

    private sealed class RecordingNotificationPublisher : INotificationPublisher
    {
        public List<NotificationRequest> Requests { get; } = [];

        public Task PublishAsync(NotificationRequest request, CancellationToken ct = default)
        {
            _ = ct;
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingIdleEscalationRecipientResolver(params Guid[] recipients)
        : IIdleEscalationRecipientResolver
    {
        public List<Guid> Requests { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(Guid tenantId, CancellationToken ct = default)
        {
            _ = ct;
            Requests.Add(tenantId);
            return Task.FromResult<IReadOnlyList<Guid>>(recipients);
        }
    }
}
