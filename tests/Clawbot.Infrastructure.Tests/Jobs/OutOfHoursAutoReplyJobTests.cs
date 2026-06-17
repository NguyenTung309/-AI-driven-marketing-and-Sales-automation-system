using Clawbot.Domain.Agents;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Jobs;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Clawbot.Infrastructure.Tests.Jobs;

public sealed class OutOfHoursAutoReplyJobTests
{
    private static readonly DateTimeOffset UtcNow = new(2026, 6, 15, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunAsync_uses_tenant_out_of_hours_window_from_chat_agent_config()
    {
        using var fx = new TestAppDb();
        var conversation = Conversation.Open(
            fx.TenantId,
            "zalo",
            "thread-tenant-window",
            UtcNow.AddMinutes(-30),
            contactId: null);
        conversation.AppendMessage("in", "contact", "Con muon hoi hoc phi HSK4", "text", UtcNow.AddMinutes(-30));
        var config = AgentConfig.Create(fx.TenantId, "chat", "Agent Chat", "chat", "claude-3-5-sonnet", UtcNow.AddDays(-1));
        config.Start();
        config.UpdateSettings(
            "Agent Chat",
            "claude-3-5-sonnet",
            "[]",
            "[]",
            """
            {
              "outOfHours": {
                "workStart": "08:00",
                "workEnd": "20:00",
                "timezoneOffsetHours": 7,
                "replyText": "Hoc Ba hien da ngoai gio lam viec rieng cua tenant."
              }
            }
            """,
            UtcNow.AddDays(-1));
        fx.Db.Conversations.Add(conversation);
        fx.Db.AgentConfigs.Add(config);
        await fx.Db.SaveChangesAsync();
        var sut = new OutOfHoursAutoReplyJob(
            fx.Db,
            new FixedClock(UtcNow),
            NullLogger<OutOfHoursAutoReplyJob>.Instance);

        await sut.RunAsync(CancellationToken.None);

        fx.Db.ChangeTracker.Clear();
        var systemReply = await fx.Db.Messages.IgnoreQueryFilters()
            .Where(m => m.ConversationId == conversation.Id && m.SenderType == "system")
            .SingleAsync();
        systemReply.Content.Should().Be("Hoc Ba hien da ngoai gio lam viec rieng cua tenant.");
        systemReply.SentAt.Should().Be(UtcNow);
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
