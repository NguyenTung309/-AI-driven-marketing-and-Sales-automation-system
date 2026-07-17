using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Services;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.Api.Tests;

public sealed class FailedMessageRetryServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid ActorId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTimeOffset Now = new(2026, 7, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RetryAsync_resends_exact_content_and_updates_same_message_row()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:thread-1", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            "out", "agent", "Nội dung đã lưu", "text", Now.AddMinutes(-4), status: "send_failed");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var originalId = message.Id;
        var originalSentAt = message.SentAt;

        var adapter = Substitute.For<IChannelAdapter>();
        adapter.SendAsync(TenantId, conversation.ExternalThreadId, message.Content, Arg.Any<CancellationToken>())
            .Returns("zalo-message-42");
        var notifier = Substitute.For<IInboxNotifier>();
        var service = CreateService(fx.Db, adapter, notifier);

        var result = await service.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        result.Outcome.Should().Be(FailedMessageRetryOutcome.Sent);
        result.Message.Should().NotBeNull();
        result.Message!.Id.Should().Be(originalId);
        result.Message.Content.Should().Be("Nội dung đã lưu");
        result.Message.SentAt.Should().Be(originalSentAt);
        result.Message.Status.Should().Be("sent");
        result.Message.ExternalMessageId.Should().Be("zalo-message-42");
        (await fx.Db.Messages.CountAsync()).Should().Be(1);
        await adapter.Received(1).SendAsync(
            TenantId, conversation.ExternalThreadId, "Nội dung đã lưu", Arg.Any<CancellationToken>());
        fx.Db.AuditLogs.Should().ContainSingle(a =>
            a.Action == "message:retry" && a.ResourceId == originalId && a.UserId == ActorId
            && !(a.DiffJson ?? string.Empty).Contains("Nội dung đã lưu"));
        await notifier.Received(1).NotifyMessageStatusAsync(
            TenantId, Arg.Is<InboxMessageStatusEvent>(e => e.MessageId == originalId && e.Status == "pending_send"),
            Arg.Any<CancellationToken>());
        await notifier.Received(1).NotifyMessageStatusAsync(
            TenantId, Arg.Is<InboxMessageStatusEvent>(e => e.MessageId == originalId && e.Status == "sent"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryAsync_restores_send_failed_when_channel_fails_without_automatic_retry()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:thread-2", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            "out", "agent", "Gửi lại tôi", "text", Now.AddMinutes(-4), status: "send_failed");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var adapter = Substitute.For<IChannelAdapter>();
        adapter.SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new ChannelSendRejectedException("provider_rejected"));
        var notifier = Substitute.For<IInboxNotifier>();
        var service = CreateService(fx.Db, adapter, notifier);

        var result = await service.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        result.Outcome.Should().Be(FailedMessageRetryOutcome.ChannelFailed);
        fx.Db.Messages.Single().Status.Should().Be("send_failed");
        await adapter.Received(1).SendAsync(
            TenantId, conversation.ExternalThreadId, "Gửi lại tôi", Arg.Any<CancellationToken>());
        await notifier.Received(1).NotifyMessageStatusAsync(
            TenantId, Arg.Is<InboxMessageStatusEvent>(e => e.MessageId == message.Id && e.Status == "send_failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryAsync_leaves_pending_send_when_channel_result_is_ambiguous()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:thread-ambiguous", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            "out", "agent", "Có thể đã gửi", "text", Now.AddMinutes(-4), status: "send_failed");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var adapter = Substitute.For<IChannelAdapter>();
        adapter.SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new HttpRequestException("connection closed after upload"));
        var notifier = Substitute.For<IInboxNotifier>();
        var service = CreateService(fx.Db, adapter, notifier);

        var result = await service.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        result.Outcome.Should().Be(FailedMessageRetryOutcome.DeliveryAmbiguous);
        fx.Db.Messages.Single().Status.Should().Be("pending_send");
        await adapter.Received(1).SendAsync(
            TenantId, conversation.ExternalThreadId, "Có thể đã gửi", Arg.Any<CancellationToken>());
        await notifier.DidNotReceive().NotifyMessageStatusAsync(
            TenantId, Arg.Is<InboxMessageStatusEvent>(e => e.Status == "send_failed"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryAsync_ignores_cancelled_realtime_notifications()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:thread-notify", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            "out", "agent", "Vẫn phải gửi", "text", Now.AddMinutes(-4), status: "send_failed");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var adapter = Substitute.For<IChannelAdapter>();
        adapter.SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("confirmed-id");
        var notifier = Substitute.For<IInboxNotifier>();
        notifier.NotifyMessageStatusAsync(Arg.Any<Guid>(), Arg.Any<InboxMessageStatusEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new TaskCanceledException("hub shutting down"));
        var service = CreateService(fx.Db, adapter, notifier);

        var result = await service.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        result.Outcome.Should().Be(FailedMessageRetryOutcome.Sent);
        fx.Db.Messages.Single().Status.Should().Be("sent");
        await adapter.Received(1).SendAsync(
            TenantId, conversation.ExternalThreadId, "Vẫn phải gửi", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RetryAsync_restores_send_failed_when_safety_rejects_content()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:thread-safety", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            "out", "agent", "Nội dung bị chặn", "text", Now.AddMinutes(-4), status: "send_failed");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var toxicity = Substitute.For<IToxicityFilter>();
        toxicity.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var adapter = Substitute.For<IChannelAdapter>();
        var notifier = Substitute.For<IInboxNotifier>();
        var service = new FailedMessageRetryService(
            fx.Db,
            adapter,
            new OutboundMessageSafetyService(toxicity, Options.Create(new ToxicityOptions())),
            notifier,
            new FixedClock(Now),
            NullLogger<FailedMessageRetryService>.Instance);

        var result = await service.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        result.Outcome.Should().Be(FailedMessageRetryOutcome.SafetyRejected);
        fx.Db.Messages.Single().Status.Should().Be("send_failed");
        await adapter.DidNotReceive().SendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await notifier.Received(1).NotifyMessageStatusAsync(
            TenantId, Arg.Is<InboxMessageStatusEvent>(e => e.MessageId == message.Id && e.Status == "send_failed"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("in", "contact", "send_failed", null)]
    [InlineData("out", "user", "send_failed", null)]
    [InlineData("out", "agent", "sent", null)]
    [InlineData("out", "agent", "pending_send", null)]
    [InlineData("out", "agent", "blocked", null)]
    [InlineData("out", "agent", "send_failed", "already-sent-id")]
    public async Task RetryAsync_rejects_ineligible_message_without_calling_channel(
        string direction,
        string senderType,
        string status,
        string? externalMessageId)
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page-1:thread-3", Now.AddMinutes(-5));
        var message = conversation.AppendMessage(
            direction, senderType, "Không được gửi", "text", Now.AddMinutes(-4),
            externalMessageId: externalMessageId, status: status);
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var adapter = Substitute.For<IChannelAdapter>();
        var service = CreateService(fx.Db, adapter, Substitute.For<IInboxNotifier>());

        var result = await service.RetryAsync(
            TenantId, conversation.Id, message.Id, ActorId,
            conversation.ExternalThreadId, conversation.AssignedTo, conversation.InboxId);

        result.Outcome.Should().Be(FailedMessageRetryOutcome.NotAvailable);
        await adapter.DidNotReceive().SendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static FailedMessageRetryService CreateService(
        AppDbContext db,
        IChannelAdapter adapter,
        IInboxNotifier notifier)
    {
        var toxicity = Substitute.For<IToxicityFilter>();
        toxicity.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(false);
        var safety = new OutboundMessageSafetyService(
            toxicity,
            Options.Create(new ToxicityOptions()));
        return new FailedMessageRetryService(
            db,
            adapter,
            safety,
            notifier,
            new FixedClock(Now),
            NullLogger<FailedMessageRetryService>.Instance);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
