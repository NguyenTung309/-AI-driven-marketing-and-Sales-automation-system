using Clawbot.Domain.Common;
using MassTransit;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Clawbot.Infrastructure.Messaging;

// Publishes aggregate domain events to MassTransit after a successful SaveChanges.
// INTERIM: publish-after-commit. The chosen MassTransit EF transactional outbox
// (AddEntityFrameworkOutbox + InboxState/OutboxState/OutboxMessage tables + migration + UseBusOutbox)
// is the reliability upgrade — deferred until validated against a real SQL Server + RabbitMQ (M21),
// because its migration DDL cannot be runtime-verified in this environment.
public sealed class DomainEventDispatchInterceptor(IPublishEndpoint publish) : SaveChangesInterceptor
{
    private readonly IPublishEndpoint _publish = publish;

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
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
                    await _publish.Publish(domainEvent, domainEvent.GetType(), cancellationToken).ConfigureAwait(false);
                aggregate.ClearDomainEvents();
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }
}
