using Clawbot.Api.Services;
using Clawbot.Domain.Conversations;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ConversationExportServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExportCsvAsync_returns_time_ordered_redacted_csv_with_escaped_fields()
    {
        using var fx = new TestApiAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "thread,csv", Now.AddMinutes(-10));
        conversation.AppendMessage(
            "in",
            "contact",
            "raw phone 0912345678",
            "text",
            Now.AddMinutes(-5),
            externalMessageId: "msg-2",
            originalContent: "raw phone 0912345678",
            redactedContent: "raw phone [PHONE]");
        conversation.AppendMessage(
            "out",
            "user",
            "Hello \"Lan\"\nline 2",
            "text",
            Now.AddMinutes(-3),
            externalMessageId: "msg-3");
        fx.Db.Conversations.Add(conversation);
        await fx.Db.SaveChangesAsync();
        var sut = new ConversationExportService(fx.Db);

        var result = await sut.ExportCsvAsync(TenantId, conversation.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.FileName.Should().Be($"conversation-{conversation.Id:N}.csv");
        result.Content.Should().StartWith("sent_at,direction,sender_type,content_type,message_type,parent_post_id,external_message_id,content");
        result.Content.Should().Contain("2026-06-15T17:55:00.0000000+00:00,in,contact,text,text,,msg-2,raw phone [PHONE]");
        result.Content.Should().Contain("\"Hello \"\"Lan\"\"\nline 2\"");
        result.Content.Should().NotContain("0912345678");
    }

    [Fact]
    public async Task ExportCsvAsync_returns_null_for_conversation_outside_tenant()
    {
        using var fx = new TestApiAppDb(TenantId);
        var otherTenantConversation = Conversation.Open(Guid.NewGuid(), "zalo", "thread-other", Now);
        fx.Db.Conversations.Add(otherTenantConversation);
        await fx.Db.SaveChangesAsync();
        var sut = new ConversationExportService(fx.Db);

        var result = await sut.ExportCsvAsync(TenantId, otherTenantConversation.Id, CancellationToken.None);

        result.Should().BeNull();
    }
}
