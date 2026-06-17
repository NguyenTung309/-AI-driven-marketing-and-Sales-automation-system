using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Services;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Clawbot.Api.Tests;

public sealed class SaleAssistDraftFeedbackServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RecordAsync_persists_redacted_draft_feedback_trace_for_conversation()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "thread-feedback-1", Now.AddMinutes(-10));
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var sut = new SaleAssistDraftFeedbackService(fx.Db, new FakePiiRedactor(), new FixedClock(Now));

        var response = await sut.RecordAsync(
            TenantId,
            new SaleAssistDraftFeedbackRequest(
                conversation.Id,
                "Em de lai so 0912345678 de tu van HSK4",
                "Da gui lo trinh HSK4, so [PHONE]",
                "edited"),
            CancellationToken.None);

        response.Edited.Should().BeTrue();
        response.RecordedAt.Should().Be(Now);
        fx.Db.ChangeTracker.Clear();
        var session = await fx.Db.AgentSessions.IgnoreQueryFilters()
            .Include(s => s.Traces)
            .SingleAsync(s => s.Id == response.SessionId);
        session.TenantId.Should().Be(TenantId);
        session.ConversationId.Should().Be(conversation.Id);
        session.Goal.Should().Be("sale-assist-draft-feedback");
        session.Status.Should().Be("completed");
        var trace = session.Traces.Should().ContainSingle().Subject;
        trace.Phase.Should().Be("recorded");
        trace.Message.Should().NotContain("0912345678");
        using var payload = JsonDocument.Parse(trace.Message!);
        payload.RootElement.GetProperty("outcome").GetString().Should().Be("edited");
        payload.RootElement.GetProperty("draftText").GetString().Should().Contain("[PHONE]");
        payload.RootElement.GetProperty("finalText").GetString().Should().Contain("[PHONE]");
    }

    private sealed class FakePiiRedactor : IPiiRedactor
    {
        public string Name => "fake-pii";

        public Task<RedactionResult> RedactAsync(string text, CancellationToken ct) =>
            Task.FromResult(new RedactionResult(
                text.Replace("0912345678", "[PHONE]", StringComparison.Ordinal),
                Array.Empty<PiiSpan>()));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
