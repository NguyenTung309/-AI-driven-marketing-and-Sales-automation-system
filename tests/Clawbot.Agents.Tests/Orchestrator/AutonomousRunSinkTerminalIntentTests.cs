using System.Reflection;
using Clawbot.AgentService.Services;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AutonomousRunSinkTerminalIntentTests
{
    [Fact]
    public async Task CancelAsync_FinalizesOnlyAfterActivePublicationClears()
    {
        await using var fixture = await SinkFixture.CreateAsync();
        var requestedAt = fixture.Clock.UtcNow;
        var seeded = await fixture.SeedActivePublicationAsync();

        await fixture.Sink.CancelAsync(
            fixture.TenantId,
            seeded.Session.Id,
            expectedGeneration: 0,
            expectedRowVersion: null,
            requestedAt);

        fixture.Db.ChangeTracker.Clear();
        var pending = await fixture.LoadSessionAsync(seeded.Session.Id);
        pending.Status.Should().Be(AgentSessionStatuses.Cancelling);
        pending.PendingTerminalGeneration.Should().Be(0);
        pending.PendingTerminalRequestedAt.Should().Be(requestedAt);
        pending.FinishedAt.Should().BeNull();

        fixture.Clock.UtcNow = requestedAt.AddMinutes(1);
        var finalizedWhilePublishing = await TryFinalizeDeferredTerminalAsync(fixture.Sink, fixture.TenantId, seeded.Session.Id);

        finalizedWhilePublishing.Should().BeFalse();
        fixture.Db.ChangeTracker.Clear();
        (await fixture.LoadSessionAsync(seeded.Session.Id)).Status
            .Should().Be(AgentSessionStatuses.Cancelling);

        var item = await fixture.LoadContentItemAsync(seeded.Item.Id);
        item.ReleasePublishAttempt(seeded.PublishAttemptId, fixture.Clock.UtcNow);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        fixture.Clock.UtcNow = requestedAt.AddMinutes(2);

        var finalizedAfterSettlement = await TryFinalizeDeferredTerminalAsync(fixture.Sink, fixture.TenantId, seeded.Session.Id);

        finalizedAfterSettlement.Should().BeTrue();
        fixture.Db.ChangeTracker.Clear();
        var cancelled = await fixture.LoadSessionAsync(seeded.Session.Id);
        cancelled.Status.Should().Be(AgentSessionStatuses.Cancelled);
        cancelled.FinishedAt.Should().Be(fixture.Clock.UtcNow);
        cancelled.PendingTerminalGeneration.Should().BeNull();
        cancelled.PendingTerminalRequestedAt.Should().BeNull();
        (await fixture.LoadContentItemAsync(seeded.Item.Id)).Status.Should().Be("rejected");
    }

    [Fact]
    public async Task FailAndRejectOrphanedContentAsync_FinalizesOnlyAfterActivePublicationClears()
    {
        await using var fixture = await SinkFixture.CreateAsync();
        var requestedAt = fixture.Clock.UtcNow;
        var seeded = await fixture.SeedActivePublicationAsync();

        var rejectedCount = await fixture.Sink.FailAndRejectOrphanedContentAsync(
            fixture.TenantId,
            seeded.Session.Id,
            "provider timeout",
            expectedGeneration: 0,
            requestedAt);

        rejectedCount.Should().Be(0);
        fixture.Db.ChangeTracker.Clear();
        var pending = await fixture.LoadSessionAsync(seeded.Session.Id);
        pending.Status.Should().Be(AgentSessionStatuses.Failing);
        pending.PendingTerminalGeneration.Should().Be(0);
        pending.PendingTerminalReason.Should().Be("provider timeout");
        pending.FinishedAt.Should().BeNull();
        var pendingTrace = await fixture.Db.AgentTraces.IgnoreQueryFilters()
            .SingleAsync(trace => trace.SessionId == seeded.Session.Id);
        pendingTrace.Phase.Should().Be("failure_pending_publication_settlement");

        fixture.Clock.UtcNow = requestedAt.AddMinutes(1);
        (await TryFinalizeDeferredTerminalAsync(fixture.Sink, fixture.TenantId, seeded.Session.Id)).Should().BeFalse();

        fixture.Db.ChangeTracker.Clear();
        var item = await fixture.LoadContentItemAsync(seeded.Item.Id);
        item.ReleasePublishAttempt(seeded.PublishAttemptId, fixture.Clock.UtcNow);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();
        fixture.Clock.UtcNow = requestedAt.AddMinutes(2);

        (await TryFinalizeDeferredTerminalAsync(fixture.Sink, fixture.TenantId, seeded.Session.Id)).Should().BeTrue();

        fixture.Db.ChangeTracker.Clear();
        var failed = await fixture.LoadSessionAsync(seeded.Session.Id);
        failed.Status.Should().Be(AgentSessionStatuses.Failed);
        failed.FinishedAt.Should().Be(fixture.Clock.UtcNow);
        failed.PendingTerminalGeneration.Should().BeNull();
        failed.PendingTerminalReason.Should().BeNull();
        (await fixture.LoadContentItemAsync(seeded.Item.Id)).Status.Should().Be("rejected");
    }

    [Fact]
    public async Task FinalizeDeferredTerminalsAsync_LeavesIntentPending_WhenGenerationIsStale()
    {
        await using var fixture = await SinkFixture.CreateAsync();
        var session = AgentSession.Start(
            fixture.TenantId,
            agentId: null,
            conversationId: null,
            "Create content",
            fixture.Clock.UtcNow);
        session.DeferCancellation(expectedGeneration: 0, fixture.Clock.UtcNow);
        fixture.Db.AgentSessions.Add(session);
        await fixture.Db.SaveChangesAsync();
        await fixture.Db.AgentSessions.IgnoreQueryFilters()
            .Where(candidate => candidate.Id == session.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.ReplanCount, 1));
        fixture.Db.ChangeTracker.Clear();

        var finalized = await TryFinalizeDeferredTerminalAsync(
            fixture.Sink,
            fixture.TenantId,
            session.Id);

        finalized.Should().BeFalse();
        fixture.Db.ChangeTracker.Clear();
        var stale = await fixture.LoadSessionAsync(session.Id);
        stale.Status.Should().Be(AgentSessionStatuses.Cancelling);
        stale.ReplanCount.Should().Be(1);
        stale.PendingTerminalGeneration.Should().Be(0);
        stale.PendingTerminalRequestedAt.Should().NotBeNull();
        stale.FinishedAt.Should().BeNull();
    }

    [Fact]
    public async Task CancelAsync_RejectsStaleGeneration()
    {
        await using var fixture = await SinkFixture.CreateAsync();
        var session = AgentSession.Start(
            fixture.TenantId,
            agentId: null,
            conversationId: null,
            "Create content",
            fixture.Clock.UtcNow);
        session.ApplyReplan("{\"tasks\":[]}", expectedGeneration: 0);
        fixture.Db.AgentSessions.Add(session);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var action = () => fixture.Sink.CancelAsync(
            fixture.TenantId,
            session.Id,
            expectedGeneration: 0,
            expectedRowVersion: null,
            fixture.Clock.UtcNow);

        await action.Should().ThrowAsync<OrchestrationPlanGenerationMismatchException>();
        fixture.Db.ChangeTracker.Clear();
        var current = await fixture.LoadSessionAsync(session.Id);
        current.Status.Should().Be(AgentSessionStatuses.Running);
        current.ReplanCount.Should().Be(1);
        current.PendingTerminalGeneration.Should().BeNull();
    }

    [Fact]
    public async Task CancelAsync_RejectsStaleEtag()
    {
        await using var fixture = await SinkFixture.CreateAsync();
        var session = AgentSession.Start(
            fixture.TenantId,
            agentId: null,
            conversationId: null,
            "Create content",
            fixture.Clock.UtcNow);
        var currentEtag = Guid.NewGuid().ToByteArray();
        fixture.Db.AgentSessions.Add(session);
        fixture.Db.Entry(session).Property(candidate => candidate.RowVersion).CurrentValue = currentEtag;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var action = () => fixture.Sink.CancelAsync(
            fixture.TenantId,
            session.Id,
            expectedGeneration: 0,
            expectedRowVersion: Guid.NewGuid().ToByteArray(),
            fixture.Clock.UtcNow);

        await action.Should().ThrowAsync<OrchestrationSessionEtagMismatchException>();
        fixture.Db.ChangeTracker.Clear();
        var current = await fixture.LoadSessionAsync(session.Id);
        current.Status.Should().Be(AgentSessionStatuses.Running);
        current.RowVersion.Should().Equal(currentEtag);
        current.PendingTerminalGeneration.Should().BeNull();
    }

    private static async Task<bool> TryFinalizeDeferredTerminalAsync(
        AutonomousRunSink sink,
        Guid tenantId,
        Guid sessionId)
    {
        var sinkType = typeof(AutonomousRunSink);
        var candidateType = sinkType.GetNestedType(
            "PendingTerminalCandidate",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Pending terminal candidate type was not found.");
        var candidate = Activator.CreateInstance(
            candidateType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [tenantId, sessionId],
            culture: null)
            ?? throw new InvalidOperationException("Pending terminal candidate could not be created.");
        var method = sinkType.GetMethod(
            "TryFinalizeDeferredTerminalAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Deferred terminal finalizer was not found.");
        var task = method.Invoke(sink, [candidate, CancellationToken.None]) as Task<bool>
            ?? throw new InvalidOperationException("Deferred terminal finalizer returned an unexpected result.");
        return await task;
    }

    private sealed class SinkFixture(
        SqliteConnection connection,
        AppDbContext db,
        MutableClock clock) : IAsyncDisposable
    {
        public SqliteConnection Connection { get; } = connection;
        public AppDbContext Db { get; } = db;
        public MutableClock Clock { get; } = clock;
        public Guid TenantId { get; } = Guid.NewGuid();
        public AutonomousRunSink Sink { get; } = new(
            db,
            new RegexPiiRedactor(),
            clock);

        public static async Task<SinkFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            var createScript = db.Database.GenerateCreateScript()
                .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
                .Replace("N'", "'", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(createScript);
            return new SinkFixture(
                connection,
                db,
                new MutableClock(new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero)));
        }

        public async Task<ActivePublicationSeed> SeedActivePublicationAsync()
        {
            var session = AgentSession.Start(
                TenantId,
                agentId: null,
                conversationId: null,
                "Create content",
                Clock.UtcNow);
            var item = ContentItem.Create(
                TenantId,
                "facebook",
                "Draft body",
                createdBy: null,
                Clock.UtcNow,
                orchestrationSessionId: session.Id,
                orchestrationPlanGeneration: 0);
            item.BeginAgentReview(item.ContentRevision, Clock.UtcNow);
            item.RecordAgentReview(
                item.ContentRevision,
                ContentItem.ReviewStatusPassed,
                ContentItem.ImageReviewStatusNotApplicable,
                reviewedImageCount: 0,
                Guid.NewGuid(),
                reason: null,
                Clock.UtcNow);
            item.RecordReviewPolicySnapshot(
                item.ContentRevision,
                ContentItem.PublishingPolicyAutomatic,
                appliedPolicyVersion: 1,
                Clock.UtcNow);
            item.ApproveAutomatically(
                item.ContentRevision,
                ContentItem.PublishingPolicyAutomatic,
                appliedPolicyVersion: 1,
                Clock.UtcNow);
            item.MarkScheduled(Clock.UtcNow);
            var publishAttemptId = Guid.NewGuid();
            item.ClaimPublishAttempt(item.ContentRevision, publishAttemptId, Clock.UtcNow);

            Db.AgentSessions.Add(session);
            Db.ContentItems.Add(item);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            return new ActivePublicationSeed(session, item, publishAttemptId);
        }

        public Task<AgentSession> LoadSessionAsync(Guid sessionId) =>
            Db.AgentSessions.IgnoreQueryFilters().SingleAsync(session => session.Id == sessionId);

        public Task<ContentItem> LoadContentItemAsync(Guid itemId) =>
            Db.ContentItems.IgnoreQueryFilters().SingleAsync(item => item.Id == itemId);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
        }
    }

    private sealed record ActivePublicationSeed(
        AgentSession Session,
        ContentItem Item,
        Guid PublishAttemptId);

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in unit test scope.");
    }
}
