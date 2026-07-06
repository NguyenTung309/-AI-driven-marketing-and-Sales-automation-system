using Clawbot.Infrastructure.Channels;
using Clawbot.SharedKernel.Channels;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Ingests channel messages published by PancakePollingService (and any future adapter path).
// At-least-once delivery is safe: the ingestor dedups on external_message_id.
public sealed partial class ChannelInboundMessageConsumer(
    IChannelMessageIngestor ingestor,
    ILogger<ChannelInboundMessageConsumer> logger)
    : IConsumer<ChannelInboundMessageReceived>
{
    private readonly IChannelMessageIngestor _ingestor = ingestor;
    private readonly ILogger<ChannelInboundMessageConsumer> _logger = logger;

    public async Task Consume(ConsumeContext<ChannelInboundMessageReceived> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var msg = context.Message;
        var result = await _ingestor.IngestAsync(msg.TenantId, msg.Message, context.CancellationToken).ConfigureAwait(false);
        LogIngested(_logger, msg.Message.ExternalThreadId, result.ConversationId, result.Deduplicated);
    }

    [LoggerMessage(EventId = 9110, Level = LogLevel.Information,
        Message = "Ingested inbound channel message for thread {ThreadId} into conversation {ConversationId} (deduplicated: {Deduplicated})")]
    private static partial void LogIngested(ILogger logger, string threadId, Guid conversationId, bool deduplicated);
}

// ponytail: ConcurrentMessageLimit=1 keeps per-conversation ordering with today's traffic;
// partition by conversation id if throughput ever matters.
public sealed class ChannelInboundMessageConsumerDefinition : ConsumerDefinition<ChannelInboundMessageConsumer>
{
    public ChannelInboundMessageConsumerDefinition()
    {
        ConcurrentMessageLimit = 1;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<ChannelInboundMessageConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(endpointConfigurator);
        // Transient DB/embedding hiccups: brief in-place retries before the message goes to the error queue.
        endpointConfigurator.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)));
    }
}
