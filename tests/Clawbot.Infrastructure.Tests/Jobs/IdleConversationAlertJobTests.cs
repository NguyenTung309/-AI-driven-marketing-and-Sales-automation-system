using Clawbot.Domain.Conversations;
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
        using var fx = new TestAppDb();
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
