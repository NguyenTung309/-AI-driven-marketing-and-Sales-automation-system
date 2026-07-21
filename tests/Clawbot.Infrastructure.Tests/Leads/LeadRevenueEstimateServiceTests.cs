using System.Reflection;
using System.Runtime.CompilerServices;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Common;
using Clawbot.Domain.Contacts;
using Clawbot.Domain.Conversations;
using Clawbot.Domain.Leads;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Notifications;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Leads;

public sealed class LeadRevenueEstimateServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Amount_null_does_not_create_row()
    {
        using var h = new Harness("""{"amount":null,"currency":"VND","evidence":"không rõ"}""");
        var leadId = await h.SeedCustomerWithTranscriptAsync();

        var result = await h.Service.EstimateAndPersistAsync(h.TenantId, leadId);

        result.Should().Be("skipped_no_amount");
        (await h.Db.LeadRevenues.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        await h.Publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Auto_approve_flag_approves_when_evidence_grounds_amount()
    {
        using var h = new Harness("""{"amount":5000000,"currency":"VND","evidence":"sale xác nhận chốt 5000000đ"}""");
        h.EnableAutoApprove();
        var leadId = await h.SeedCustomerWithTranscriptAsync();

        var result = await h.Service.EstimateAndPersistAsync(h.TenantId, leadId);

        result.Should().Be("approved");
        var row = await h.Db.LeadRevenues.IgnoreQueryFilters().SingleAsync();
        row.Amount.Should().Be(5_000_000m);
        row.Status.Should().Be(LeadRevenue.StatusApproved);
        row.Source.Should().Be(LeadRevenue.SourceAi);
        row.DecidedBy.Should().BeNull();
        await h.Publisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default);
    }

    [Fact]
    public async Task Auto_approve_stays_pending_without_grounded_evidence()
    {
        using var h = new Harness("""{"amount":5000000,"currency":"VND","evidence":"khách nói muốn mua"}""");
        h.EnableAutoApprove();
        var leadId = await h.SeedCustomerWithTranscriptAsync();

        var result = await h.Service.EstimateAndPersistAsync(h.TenantId, leadId);

        result.Should().Be("pending");
        var row = await h.Db.LeadRevenues.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(LeadRevenue.StatusPending);
        await h.Publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(n =>
                n.Type == "lead_revenue_pending"
                && !n.Body!.Contains("5000000", StringComparison.Ordinal)
                && n.Link!.Contains(leadId.ToString())),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evidence_is_pii_redacted_before_persist()
    {
        using var h = new Harness("""{"amount":1200000,"currency":"VND","evidence":"gọi 0901234567 chốt 1tr2"}""");
        h.Pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new RedactionResult(call.Arg<string>().Replace("0901234567", "[PHONE]", StringComparison.Ordinal), []));
        var leadId = await h.SeedCustomerWithTranscriptAsync();

        await h.Service.EstimateAndPersistAsync(h.TenantId, leadId);

        var row = await h.Db.LeadRevenues.IgnoreQueryFilters().SingleAsync();
        row.Status.Should().Be(LeadRevenue.StatusPending);
        row.Evidence.Should().Contain("[PHONE]");
        row.Evidence.Should().NotContain("0901234567");
        await h.Publisher.Received(1).PublishAsync(
            Arg.Is<NotificationRequest>(n => n.Type == "lead_revenue_pending" && n.Link!.Contains(leadId.ToString())),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Existing_revenue_skips_without_llm()
    {
        using var h = new Harness("""{"amount":999,"currency":"VND","evidence":"x"}""");
        var leadId = await h.SeedCustomerWithTranscriptAsync();
        h.Db.LeadRevenues.Add(LeadRevenue.CreateManual(h.TenantId, leadId, 1_000_000m, "VND", Guid.NewGuid(), Now));
        await h.Db.SaveChangesAsync();
        h.Db.ChangeTracker.Clear();

        var result = await h.Service.EstimateAndPersistAsync(h.TenantId, leadId);

        result.Should().Be("skipped_existing_revenue");
        h.Chat.Calls.Should().Be(0);
        (await h.Db.LeadRevenues.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TestAppDb _fx;
        private readonly Tenant _tenant;

        public Harness(params string[] chatResponses)
        {
            _fx = new TestAppDb();
            Chat = new ScriptedChatClient();
            Chat.Script(chatResponses);
            Pii = Substitute.For<IPiiRedactor>();
            Pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => new RedactionResult(call.Arg<string>(), []));
            Publisher = Substitute.For<INotificationPublisher>();
            var clock = Substitute.For<IClock>();
            clock.UtcNow.Returns(Now);

            _tenant = Tenant.Create("rev-est", "Rev Est", "pro", Now);
            typeof(Entity<Guid>).GetProperty(nameof(Tenant.Id))!
                .SetValue(_tenant, TenantId);
            // Tenant.Id is on Entity base — use EF entry if reflection path fails.
            Db.Tenants.Add(_tenant);
            Db.Entry(_tenant).Property(x => x.Id).CurrentValue = TenantId;
            Db.SaveChanges();

            var recipients = Substitute.For<ILeadNotificationRecipientResolver>();
            recipients.ResolveAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
                .Returns(call => call.ArgAt<Guid?>(1) ?? Guid.NewGuid());
            Service = new LeadRevenueEstimateService(
                Db,
                new LeadRevenueEstimator(Chat, new NoopLlmScope()),
                Pii,
                Publisher,
                recipients,
                clock,
                NullLogger<LeadRevenueEstimateService>.Instance);
        }

        public AppDbContext Db => _fx.Db;
        public Guid TenantId => _fx.TenantId;
        public LeadRevenueEstimateService Service { get; }
        public ScriptedChatClient Chat { get; }
        public IPiiRedactor Pii { get; }
        public INotificationPublisher Publisher { get; }

        public void EnableAutoApprove()
        {
            var tenant = Db.Tenants.IgnoreQueryFilters().First(t => t.Id == TenantId);
            tenant.SetAutoApproveLeadRevenue(true);
            Db.SaveChanges();
        }

        public async Task<Guid> SeedCustomerWithTranscriptAsync()
        {
            var contact = Contact.Create(TenantId, "Khách chốt", Now.AddDays(-3));
            Db.Set<Contact>().Add(contact);

            var lead = Lead.Create(TenantId, contact.Id, "facebook", Now.AddDays(-2));
            lead.MarkCustomer("paid", Now.AddHours(-1));
            Db.Leads.Add(lead);

            var conv = Conversation.Open(TenantId, "facebook", $"t-{Guid.NewGuid():N}", Now.AddDays(-1), contact.Id);
            conv.AppendMessage("in", "contact", "em chốt gói 5 triệu nhé", "text", Now.AddHours(-2));
            conv.AppendMessage("out", "user", "ok, em chuyển khoản giúp chị", "text", Now.AddHours(-1));
            Db.Conversations.Add(conv);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return lead.Id;
        }

        public void Dispose() => _fx.Dispose();
    }

    public sealed class ScriptedChatClient : IClaudeChatClient
    {
        private readonly Queue<string> _responses = new();
        public int Calls { get; private set; }

        public void Script(params string[] responses)
        {
            foreach (var r in responses) _responses.Enqueue(r);
        }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new ClaudeReply(
                _responses.Count > 0 ? _responses.Dequeue() : """{"amount":null}""",
                1, 1, 0.01m, "test"));
        }

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            var reply = await CompleteAsync(systemPrompt, history, userMessage, ct);
            yield return new ClaudeStreamChunk(reply.Text, Final: true, 1, 1, 0.01m, "test");
        }
    }

    private sealed class NoopLlmScope : ILlmCallScope
    {
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
        public LlmCallContext? Current => null;
        public IDisposable Begin(Guid tenantId, string agentCode, DateTimeOffset? costAt = null, Guid? reservationId = null, Guid? sessionId = null) =>
            new NoopDisposable();
    }
}
