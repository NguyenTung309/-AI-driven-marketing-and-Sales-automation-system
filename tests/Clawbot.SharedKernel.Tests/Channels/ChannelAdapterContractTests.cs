using Clawbot.SharedKernel.Channels;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Channels;

public sealed class ChannelSendRejectedExceptionTests
{
    [Fact]
    public void Constructor_SetsCodeAsMessage()
    {
        var ex = new ChannelSendRejectedException("token_expired", 401);

        ex.Code.Should().Be("token_expired");
        ex.Message.Should().Be("token_expired");
        ex.StatusCode.Should().Be(401);
    }

    [Fact]
    public void StatusCode_DefaultsToNull()
    {
        new ChannelSendRejectedException("rejected").StatusCode.Should().BeNull();
    }
}

public sealed class ChannelDeliveryAmbiguousExceptionTests
{
    [Fact]
    public void Constructor_SetsCodeAndInnerException()
    {
        var inner = new TimeoutException("upstream timeout");

        var ex = new ChannelDeliveryAmbiguousException("send_timeout", inner);

        ex.Code.Should().Be("send_timeout");
        ex.Message.Should().Be("send_timeout");
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void InnerException_DefaultsToNull()
    {
        new ChannelDeliveryAmbiguousException("ambiguous").InnerException.Should().BeNull();
    }
}

public sealed class ChannelAdapterDefaultSendTests
{
    [Fact]
    public async Task SendAsync_WithAccessToken_DefaultsToTokenlessOverload()
    {
        // Adapter cũ chưa override overload có accessToken — default interface method phải rơi về
        // overload 5 tham số, nếu không tin nhắn sẽ im lặng không gửi.
        IChannelAdapter adapter = new TokenlessAdapter();

        var id = await adapter.SendAsync(
            Guid.NewGuid(), "facebook", "thread-1", "xin chào", "an-access-token");

        id.Should().Be("sent:thread-1");
    }

    private sealed class TokenlessAdapter : IChannelAdapter
    {
        public string Name => "tokenless";

        public Task<bool> VerifyWebhookSignatureAsync(
            Guid tenantId,
            string rawBody,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken ct = default) => Task.FromResult(true);

        public Task<IReadOnlyList<ChannelMessage>> ParseAsync(
            string rawBody,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ChannelMessage>>([]);

        public Task<string?> SendAsync(
            Guid tenantId,
            string platform,
            string externalThreadId,
            string text,
            CancellationToken ct = default) =>
            Task.FromResult<string?>($"sent:{externalThreadId}");
    }
}
