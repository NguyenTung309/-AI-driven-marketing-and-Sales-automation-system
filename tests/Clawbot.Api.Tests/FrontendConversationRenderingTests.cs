using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class FrontendConversationRenderingTests
{
    [Fact]
    public void Conversation_bubble_treats_agent_sender_as_ai_and_renders_delivery_failures()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "features", "conversations", "ConversationsPage.tsx"));

        source.Should().Contain("const byAi = message.senderType === \"ai\" || message.senderType === \"bot\" || message.senderType === \"agent\";");
        source.Should().Contain("message.status === \"pending_send\"");
        source.Should().Contain("message.status === \"send_failed\"");
        source.Should().Contain("Gửi thất bại");
    }

    [Fact]
    public void Agent_dashboard_shows_redacted_input_and_reply_without_operational_fallback()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "features", "agents", "AgentDashboardPage.tsx"));

        source.Should().Contain("formatOperationalTraceMessage(trace.phase, trace.message)");
        source.Should().Contain("text: response.reply");
        source.Should().NotContain("text: toSafeOperationalText(response.reply");
    }

    [Fact]
    public void Run_detail_uses_phase_aware_trace_text_and_formula_safe_csv()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "features", "agents", "AgentRunDetailPage.tsx"));

        source.Should().Contain("formatOperationalTraceMessage(trace.phase, trace.message)");
        source.Should().Contain("row.map(toSafeCsvCell)");
        source.Should().NotContain("toSafeOperationalText(item.message)");
    }

    [Fact]
    public void Realtime_events_refetch_authoritative_conversation_detail()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "features", "conversations", "useInboxRealtime.ts"));

        source.Should().Contain("queryKey: [\"inbox\", \"conversation\", evt.conversationId]");
        source.Should().Contain("exact: true");
        source.Should().Contain("lastMessagePreview: evt.content");
    }

    [Fact]
    public void Inbox_message_contract_includes_delivery_states()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "shared", "api", "inbox.ts"));

        source.Should().Contain("export type InboxMessageStatus");
        source.Should().Contain("\"pending_send\"");
        source.Should().Contain("\"send_failed\"");
        source.Should().Contain("readonly status?: InboxMessageStatus");
    }

    [Fact]
    public void Failed_ai_message_exposes_per_message_retry_without_automatic_mutation_retry()
    {
        var page = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "features", "conversations", "ConversationsPage.tsx"));
        var api = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "shared", "api", "inbox.ts"));

        page.Should().Contain("message.status === \"send_failed\"");
        page.Should().Contain("outbound && byAi");
        page.Should().Contain("Gửi lại");
        page.Should().Contain("Đang gửi lại");
        page.Should().Contain("retryingMessageIds");
        page.Should().Contain("retryErrors");
        page.Should().Contain("retry: false");
        page.Should().Contain("conversationId, messageId");
        api.Should().Contain("retryConversationMessage");
        api.Should().Contain("/messages/${messageId}/retry");
    }

    [Fact]
    public void Realtime_message_status_patches_only_matching_message()
    {
        var source = File.ReadAllText(FindRepoFile(
            "src", "frontend", "clawbot-web", "src", "features", "conversations", "useInboxRealtime.ts"));

        source.Should().Contain("connection.on(\"messageStatus\"");
        source.Should().Contain("message.id === evt.messageId");
        source.Should().Contain("status: evt.status");
        source.Should().NotContain("patchConversationListCache(old, evt.conversationId, {\n          lastMessageAt: evt.sentAt");
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
