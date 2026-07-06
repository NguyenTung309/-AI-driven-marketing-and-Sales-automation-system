using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Messaging;
using Clawbot.SharedKernel.Channels;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Messaging;

public sealed class ChannelInboundMessageConsumerTests
{
    [Fact]
    public async Task Consume_ForwardsMessageToIngestor()
    {
        var tenantId = Guid.NewGuid();
        var channelMsg = new ChannelMessage(
            "zalo", "page1:conv1", "user1", "hello",
            new DateTimeOffset(2026, 7, 6, 0, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string> { ["external_message_id"] = "m1" });

        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(tenantId, channelMsg, Arg.Any<CancellationToken>())
            .Returns(new IngestResult(Guid.NewGuid(), Guid.NewGuid(), false));
        var context = Substitute.For<ConsumeContext<ChannelInboundMessageReceived>>();
        context.Message.Returns(new ChannelInboundMessageReceived(tenantId, channelMsg));

        var sut = new ChannelInboundMessageConsumer(ingestor, NullLogger<ChannelInboundMessageConsumer>.Instance);
        await sut.Consume(context);

        await ingestor.Received(1).IngestAsync(tenantId, channelMsg, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_PropagatesIngestorFailure_ForRetry()
    {
        var ingestor = Substitute.For<IChannelMessageIngestor>();
        ingestor.IngestAsync(Arg.Any<Guid>(), Arg.Any<ChannelMessage>(), Arg.Any<CancellationToken>())
            .Returns<Task<IngestResult>>(_ => throw new InvalidOperationException("db down"));
        var context = Substitute.For<ConsumeContext<ChannelInboundMessageReceived>>();
        context.Message.Returns(new ChannelInboundMessageReceived(
            Guid.NewGuid(),
            new ChannelMessage("zalo", "t", "u", "x", DateTimeOffset.UtcNow, new Dictionary<string, string>())));

        var sut = new ChannelInboundMessageConsumer(ingestor, NullLogger<ChannelInboundMessageConsumer>.Instance);

        // Exception must bubble so MassTransit retry/error-queue policy applies
        var act = async () => await sut.Consume(context);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
