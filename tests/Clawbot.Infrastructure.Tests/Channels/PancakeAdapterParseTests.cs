using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Channels;

// Fix comment-auto-reply: webhook Pancake THẬT gửi {page_id, event_type:"messaging", data:{...}} —
// parser cũ chỉ hiểu {events:[...]} tự chế nên trả rỗng và job comment không bao giờ chạy.
public sealed class PancakeAdapterParseTests : IDisposable
{
    private readonly HttpClient _http = new(new PancakeSendTestHandler("unused"));
    private readonly IPancakeConfigResolver _resolver = Substitute.For<IPancakeConfigResolver>();
    private readonly ITenantAccessor _tenants = Substitute.For<ITenantAccessor>();

    public void Dispose() => _http.Dispose();

    private PancakeChannelAdapter BuildAdapter() => new(_http, _resolver, _tenants);

    [Fact]
    public async Task ParseAsync_parses_real_messaging_webhook_inbox_message()
    {
        var body = """
        {
          "page_id": "123456789",
          "event_type": "messaging",
          "data": {
            "conversation": { "id": "conv-abc", "type": "INBOX" },
            "message": {
              "id": "msg-1",
              "conversation_id": "conv-abc",
              "page_id": "123456789",
              "message": "xin chao",
              "type": "INBOX",
              "inserted_at": "2026-07-10T03:00:00.000000",
              "from": { "id": "user-9", "name": "Khach A" }
            }
          }
        }
        """;

        var messages = await BuildAdapter().ParseAsync(body);

        var msg = messages.Should().ContainSingle().Subject;
        msg.Channel.Should().Be("facebook");
        msg.ExternalThreadId.Should().Be("123456789:conv-abc");
        msg.ExternalUserId.Should().Be("user-9");
        msg.Text.Should().Be("xin chao");
        msg.MessageType.Should().Be("text");
        msg.Metadata["external_message_id"].Should().Be("msg-1");
        msg.Metadata["sender_name"].Should().Be("Khach A");
        msg.Metadata.ContainsKey("is_owner").Should().BeFalse();
    }

    [Fact]
    public async Task ParseAsync_parses_comment_webhook_with_post_id_and_marks_message_type()
    {
        var body = """
        {
          "page_id": "123456789",
          "event_type": "messaging",
          "data": {
            "conversation": { "id": "post-7_cmt-5", "type": "COMMENT", "post_id": "post-7" },
            "message": {
              "id": "cmt-5",
              "conversation_id": "post-7_cmt-5",
              "message": "gia bao nhieu?",
              "type": "COMMENT",
              "inserted_at": "2026-07-10T03:05:00.000000",
              "from": { "id": "user-9", "name": "Khach A" }
            },
            "post": { "id": "post-7" }
          }
        }
        """;

        var messages = await BuildAdapter().ParseAsync(body);

        var msg = messages.Should().ContainSingle().Subject;
        msg.MessageType.Should().Be("comment");
        msg.ParentPostId.Should().Be("post-7");
        msg.Metadata["external_message_id"].Should().Be("cmt-5");
    }

    [Fact]
    public async Task ParseAsync_marks_owner_echo_when_sender_is_page()
    {
        var body = """
        {
          "page_id": "pzl_page_1",
          "event_type": "messaging",
          "data": {
            "conversation": { "id": "conv-z", "type": "INBOX" },
            "message": {
              "id": "msg-2",
              "message": "page tu rep",
              "type": "INBOX",
              "from": { "id": "pzl_page_1", "name": "Page" }
            }
          }
        }
        """;

        var messages = await BuildAdapter().ParseAsync(body);

        var msg = messages.Should().ContainSingle().Subject;
        msg.Channel.Should().Be("zalo"); // prefix pzl_
        msg.Metadata["is_owner"].Should().Be("true");
    }

    [Fact]
    public async Task ParseAsync_returns_empty_for_unknown_payload()
    {
        (await BuildAdapter().ParseAsync("""{"event_type":"subscription","data":{}}""")).Should().BeEmpty();
        (await BuildAdapter().ParseAsync("not json")).Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_still_parses_legacy_events_format()
    {
        var body = """
        {"events":[{"platform":"facebook","page_id":"p1","thread_id":"t1","message_id":"m1","sender_id":"u1","text":"hi","type":"DM","sent_at":"2026-07-10T03:00:00Z"}]}
        """;

        var messages = await BuildAdapter().ParseAsync(body);

        messages.Should().ContainSingle().Which.ExternalThreadId.Should().Be("p1:t1");
    }
}
