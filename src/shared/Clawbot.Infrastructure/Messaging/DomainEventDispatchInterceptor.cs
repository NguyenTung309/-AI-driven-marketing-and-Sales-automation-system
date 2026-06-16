using Clawbot.Domain.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Messaging;

// Publishes aggregate domain events through the MassTransit transactional outbox (WS1).
// Publishing happens in SavingChangesAsync (BEFORE the write completes) so that, with
// UseBusOutbox() configured, each event is buffered and persisted to OutboxMessage in the
// SAME SaveChanges transaction as the aggregate change — exactly-once, durable across a
// broker outage. MassTransit's delivery service relays to RabbitMQ after commit.
// NOTE: the outbox tables (migration 0019) + end-to-end relay require a real SQL Server +
// RabbitMQ to verify (Docker / M21); compilation + SQLite model are covered by unit tests.
public sealed partial class DomainEventDispatchInterceptor(
    IPublishEndpoint publish,
    ILogger<DomainEventDispatchInterceptor> logger) : SaveChangesInterceptor
{
    private readonly IPublishEndpoint _publish = publish;
    private readonly ILogger<DomainEventDispatchInterceptor> _logger = logger;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            var aggregates = eventData.Context.ChangeTracker.Entries()
                .Select(e => e.Entity)
                .OfType<IHasDomainEvents>()
                .Where(a => a.DomainEvents.Count > 0)
                .ToList();

            foreach (var aggregate in aggregates)
            {
                foreach (var domainEvent in aggregate.DomainEvents.ToList())
                {
                    try
                    {
                        // With UseBusOutbox() this enlists into the outbox (no direct broker call).
                        await _publish.Publish(domainEvent, domainEvent.GetType(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        LogPublishFailed(_logger, ex, domainEvent.GetType().Name);
                    }
                }
                aggregate.ClearDomainEvents();
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 9120, Level = LogLevel.Error,
        Message = "Failed to enlist domain event {EventType} into outbox")]
    private static partial void LogPublishFailed(ILogger logger, Exception ex, string eventType);
}
