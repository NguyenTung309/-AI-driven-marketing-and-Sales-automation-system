using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class DripSequenceJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_sends_due_step_to_lead_conversation_and_persists_outbound_message()
    {
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "Nguyen Lan", Now.AddDays(-2));
        var lead = Lead.Create(fx.TenantId, contact.Id, "pancake", Now.AddDays(-2));
        var conversation = Conversation.Open(fx.TenantId, "facebook", "thread-lan", Now.AddDays(-2), contact.Id);
        conversation.AppendMessage("in", "customer", "Em quan tam HSK3", "text", Now.AddHours(-3));
        var sequence = DripSequence.Create(fx.TenantId, "Warm drip", "warm_lead", Now.AddDays(-1));
        sequence.AddStep(1, 0, "pancake", "Chao {lead_name}, ban can tu van them khong?");
        var enrollment = DripEnrollment.Enroll(fx.TenantId, sequence.Id, lead.Id, Now.AddMinutes(-1), Now.AddDays(-1));
        fx.Db.AddRange(contact, lead, conversation, sequence, enrollment);
        await fx.Db.SaveChangesAsync();
        var adapter = new CapturingChannelAdapter();
        var sut = new DripSequenceJob(
            fx.Db,
            adapter,
            new FixedClock(Now),
            NullLogger<DripSequenceJob>.Instance);

        await sut.RunAsync(CancellationToken.None);

        adapter.Sends.Should().ContainSingle().Which.Should().Be((
            "thread-lan",
            "Chao Nguyen Lan, ban can tu van them khong?"));

        fx.Db.ChangeTracker.Clear();
        var savedEnrollment = await fx.Db.DripEnrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == enrollment.Id);
        savedEnrollment.Status.Should().Be("completed");
        savedEnrollment.CompletedAt.Should().Be(Now);

        var outbound = await fx.Db.Messages.IgnoreQueryFilters()
            .SingleAsync(m => m.ConversationId == conversation.Id && m.Direction == "out");
        outbound.SenderType.Should().Be("agent");
        outbound.Content.Should().Be("Chao Nguyen Lan, ban can tu van them khong?");
        outbound.SentAt.Should().Be(Now);
    }

    [Fact]
    public async Task RunAsync_holds_enrollment_when_conversation_in_manual_mode()
    {
        // Review-gate P3: AI off (sale cầm hội thoại) → drip đứng yên, không gửi, không cancel/advance —
        // enrollment còn nguyên để lần chạy sau thử lại khi AI bật lại.
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "Nguyen Lan", Now.AddDays(-2));
        var lead = Lead.Create(fx.TenantId, contact.Id, "pancake", Now.AddDays(-2));
        var conversation = Conversation.Open(fx.TenantId, "facebook", "thread-manual", Now.AddDays(-2), contact.Id);
        conversation.SetAiAutoReply(false);
        var sequence = DripSequence.Create(fx.TenantId, "Warm drip", "warm_lead", Now.AddDays(-1));
        sequence.AddStep(1, 0, "pancake", "Chao {lead_name}!");
        var enrollment = DripEnrollment.Enroll(fx.TenantId, sequence.Id, lead.Id, Now.AddMinutes(-1), Now.AddDays(-1));
        fx.Db.AddRange(contact, lead, conversation, sequence, enrollment);
        await fx.Db.SaveChangesAsync();
        var adapter = new CapturingChannelAdapter();
        var sut = new DripSequenceJob(fx.Db, adapter, new FixedClock(Now), NullLogger<DripSequenceJob>.Instance);

        await sut.RunAsync(CancellationToken.None);

        adapter.Sends.Should().BeEmpty();
        fx.Db.ChangeTracker.Clear();
        var savedEnrollment = await fx.Db.DripEnrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == enrollment.Id);
        savedEnrollment.Status.Should().Be("active"); // giữ nguyên, thử lại lần sau
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.ConversationId == conversation.Id && m.Direction == "out"))
            .Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_cancels_enrollment_when_rendered_message_is_toxic()
    {
        // Review-gate P5: template + tên khách render ra nội dung toxic → chặn gửi + cancel (retry vô ích).
        using var fx = new TestAppDb();
        var contact = Contact.Create(fx.TenantId, "Nguyen Lan", Now.AddDays(-2));
        var lead = Lead.Create(fx.TenantId, contact.Id, "pancake", Now.AddDays(-2));
        var conversation = Conversation.Open(fx.TenantId, "facebook", "thread-toxic", Now.AddDays(-2), contact.Id);
        var sequence = DripSequence.Create(fx.TenantId, "Bad drip", "warm_lead", Now.AddDays(-1));
        sequence.AddStep(1, 0, "pancake", "Noi dung xau {lead_name}");
        var enrollment = DripEnrollment.Enroll(fx.TenantId, sequence.Id, lead.Id, Now.AddMinutes(-1), Now.AddDays(-1));
        fx.Db.AddRange(contact, lead, conversation, sequence, enrollment);
        await fx.Db.SaveChangesAsync();
        var adapter = new CapturingChannelAdapter();
        var toxicity = Substitute.For<Clawbot.Agents.Core.Skills.Nlp.IToxicityFilter>();
        toxicity.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(true);
        var sut = new DripSequenceJob(fx.Db, adapter, new FixedClock(Now), NullLogger<DripSequenceJob>.Instance, toxicity);

        await sut.RunAsync(CancellationToken.None);

        adapter.Sends.Should().BeEmpty();
        fx.Db.ChangeTracker.Clear();
        (await fx.Db.DripEnrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == enrollment.Id))
            .Status.Should().Be("cancelled");
    }

    private sealed class CapturingChannelAdapter : IChannelAdapter
    {
        public string Name => "pancake";
        public List<(string ExternalThreadId, string Text)> Sends { get; } = [];

        public Task<bool> VerifyWebhookSignatureAsync(string rawBody, IReadOnlyDictionary<string, string> headers, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<ChannelMessage>> ParseAsync(string rawBody, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChannelMessage>>([]);

        public Task<string?> SendAsync(string externalThreadId, string text, CancellationToken ct = default)
        {
            Sends.Add((externalThreadId, text));
            return Task.FromResult<string?>(null);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
