using Clawbot.Domain.Conversations.Events;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Sample consumer proving the domain-event → RabbitMQ flow end-to-end.
// Real fan-out (notify assigned sale, metrics) layers on top of this.
public sealed partial class ConversationEscalatedConsumer(ILogger<ConversationEscalatedConsumer> logger)
    : IConsumer<ConversationEscalated>
{
    private readonly ILogger<ConversationEscalatedConsumer> _logger = logger;

    public Task Consume(ConsumeContext<ConversationEscalated> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        LogEscalated(_logger, context.Message.ConversationId, context.Message.TenantId);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 9101, Level = LogLevel.Information,
        Message = "Consumed ConversationEscalated {ConversationId} (tenant {TenantId})")]
    private static partial void LogEscalated(ILogger logger, Guid conversationId, Guid tenantId);
}
