using Clawbot.Domain.Common;
using Clawbot.Infrastructure.Messaging;
using FluentAssertions;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Messaging;

// Phase 2.13: outbox enlistment failure must propagate and retain domain events (no swallow-and-clear).
public sealed class DomainEventDispatchInterceptorTests
{
    [Fact]
    public async Task SavingChanges_enlists_and_clears_events_on_success()
    {
        var publisher = Substitute.For<IPublishEndpoint>();
        publisher.Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await using var db = EventDb.Create(BuildInterceptor(publisher));
        var aggregate = new TestAggregate();
        aggregate.RaiseSample("ok");
        db.Aggregates.Add(aggregate);

        await db.SaveChangesAsync();

        aggregate.DomainEvents.Should().BeEmpty();
        await publisher.Received(1).Publish(
            Arg.Any<object>(),
            Arg.Is<Type>(t => t == typeof(SampleDomainEvent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SavingChanges_propagates_enlistment_failure_and_retains_events()
    {
        var publisher = Substitute.For<IPublishEndpoint>();
        publisher.Publish(Arg.Any<object>(), Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("outbox_enlist_failed"));

        await using var db = EventDb.Create(BuildInterceptor(publisher));
        var aggregate = new TestAggregate();
        aggregate.RaiseSample("keep-me");
        db.Aggregates.Add(aggregate);

        var act = async () => await db.SaveChangesAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("outbox_enlist_failed");

        aggregate.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<SampleDomainEvent>()
            .Which.Name.Should().Be("keep-me");
    }

    private static DomainEventDispatchInterceptor BuildInterceptor(IPublishEndpoint publisher)
    {
        var services = new ServiceCollection();
        services.AddSingleton(publisher);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        return new DomainEventDispatchInterceptor(
            scopeFactory,
            NullLogger<DomainEventDispatchInterceptor>.Instance);
    }

    private sealed class EventDb : DbContext
    {
        private readonly SqliteConnection _connection;

        private EventDb(DbContextOptions<EventDb> options, SqliteConnection connection)
            : base(options)
        {
            _connection = connection;
            Database.EnsureCreated();
        }

        public static EventDb Create(params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<EventDb>()
                .UseSqlite(connection)
                .AddInterceptors(interceptors)
                .Options;
            return new EventDb(options, connection);
        }

        public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestAggregate>(e =>
            {
                e.HasKey(x => x.Id);
                e.Ignore(x => x.DomainEvents);
            });
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    private sealed class TestAggregate : AggregateRoot<Guid>
    {
        public TestAggregate()
        {
            Id = Guid.NewGuid();
        }

        public void RaiseSample(string name) => Raise(new SampleDomainEvent(name));
    }

    private sealed record SampleDomainEvent(string Name) : IDomainEvent
    {
        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
    }
}
