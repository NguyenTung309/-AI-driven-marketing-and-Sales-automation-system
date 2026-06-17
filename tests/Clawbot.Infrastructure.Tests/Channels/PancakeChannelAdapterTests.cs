using System.Security.Cryptography;
using System.Text;
using Clawbot.Infrastructure.Channels.Pancake;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests.Channels;

public sealed class PancakeChannelAdapterTests
{
    [Fact]
    public async Task ParseAsync_maps_comment_events_to_comment_message_with_parent_post_id()
    {
        const string payload = """
        {
          "events": [
            {
              "platform": "facebook",
              "page_id": "page-1",
              "thread_id": "comment-conv-1",
              "message_id": "comment-1",
              "sender_id": "user-1",
              "sender_name": "Nguyen Lan",
              "text": "Gia HSK4 bao nhieu?",
              "type": "COMMENT",
              "post_id": "post-99",
              "sent_at": "2026-06-15T03:00:00Z"
            }
          ]
        }
        """;
        var sut = new PancakeChannelAdapter(new HttpClient(), new NullPancakeConfigResolver(), TenantAccessor());

        var messages = await sut.ParseAsync(payload);

        var message = messages.Should().ContainSingle().Subject;
        message.Channel.Should().Be("facebook");
        message.ExternalThreadId.Should().Be("page-1:comment-conv-1");
        message.ExternalUserId.Should().Be("user-1");
        message.Text.Should().Be("Gia HSK4 bao nhieu?");
        message.MessageType.Should().Be("comment");
        message.ParentPostId.Should().Be("post-99");
        message.Metadata.Should().Contain("external_message_id", "comment-1");
        message.Metadata.Should().Contain("display_name", "Nguyen Lan");
        message.Metadata.Should().Contain("page_id", "page-1");
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_accepts_configured_hex_hmac_header()
    {
        const string body = """{"events":[]}""";
        const string secret = "webhook-secret";
        var signature = "sha256=" + HmacHex(body, secret);
        var sut = new PancakeChannelAdapter(
            new HttpClient(),
            new FixedPancakeConfigResolver(new PancakeRuntimeConfig(
                "https://pancake.vn/api/v1",
                AccessToken: "",
                WebhookSecret: secret,
                SignatureHeader: "x-pancake-signature",
                SignatureAlgo: "hmac-sha256",
                SignatureEncoding: "hex",
                SendPathTemplate: "/send",
                AuthMode: "query",
                PageId: "")),
            TenantAccessor());

        var ok = await sut.VerifyWebhookSignatureAsync(
            body,
            new Dictionary<string, string> { ["X-Pancake-Signature"] = signature });

        ok.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_rejects_invalid_signature()
    {
        var sut = new PancakeChannelAdapter(
            new HttpClient(),
            new FixedPancakeConfigResolver(new PancakeRuntimeConfig(
                "https://pancake.vn/api/v1",
                AccessToken: "",
                WebhookSecret: "webhook-secret",
                SignatureHeader: "x-pancake-signature",
                SignatureAlgo: "hmac-sha256",
                SignatureEncoding: "hex",
                SendPathTemplate: "/send",
                AuthMode: "query",
                PageId: "")),
            TenantAccessor());

        var ok = await sut.VerifyWebhookSignatureAsync(
            """{"events":[]}""",
            new Dictionary<string, string> { ["x-pancake-signature"] = "sha256=deadbeef" });

        ok.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyWebhookSignatureAsync_accepts_configured_base64_hmac_header()
    {
        const string body = """{"events":[{"thread_id":"t","text":"hi"}]}""";
        const string secret = "base64-secret";
        var sut = new PancakeChannelAdapter(
            new HttpClient(),
            new FixedPancakeConfigResolver(new PancakeRuntimeConfig(
                "https://pancake.vn/api/v1",
                AccessToken: "",
                WebhookSecret: secret,
                SignatureHeader: "x-pk-signature",
                SignatureAlgo: "hmac-sha256",
                SignatureEncoding: "base64",
                SendPathTemplate: "/send",
                AuthMode: "query",
                PageId: "")),
            TenantAccessor());

        var ok = await sut.VerifyWebhookSignatureAsync(
            body,
            new Dictionary<string, string> { ["x-pk-signature"] = HmacBase64(body, secret) });

        ok.Should().BeTrue();
    }

    private static string HmacHex(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    private static string HmacBase64(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static ITenantAccessor TenantAccessor()
    {
        var tenants = Substitute.For<ITenantAccessor>();
        var context = new TenantContext(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "test");
        tenants.Current.Returns(context);
        tenants.Require().Returns(context);
        return tenants;
    }

    private sealed class NullPancakeConfigResolver : IPancakeConfigResolver
    {
        public Task<PancakeRuntimeConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<PancakeRuntimeConfig?>(null);
    }

    private sealed class FixedPancakeConfigResolver(PancakeRuntimeConfig config) : IPancakeConfigResolver
    {
        public Task<PancakeRuntimeConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<PancakeRuntimeConfig?>(config);
    }
}
