using Clawbot.AgentService.Services;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentScheduleRunnerInitiatorAuthorizationTests
{
    [Fact]
    public async Task ContinueRunAsync_FailsPersistedPendingApprovalSession_WhenInitiatorMissing()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        var seeded = await fixture.SeedAsync(initiatorUserId: null, requiresApproval: true);

        await fixture.ContinueAsync(seeded.Run.Id);
        var persisted = await fixture.LoadAsync(seeded.Run.Id, seeded.Session.Id);

        persisted.Run.Status.Should().Be("failed");
        persisted.Run.Error.Should().Be("schedule_initiator_missing");
        persisted.Run.FinishedAt.Should().NotBeNull();
        persisted.Session.Status.Should().Be(AgentSessionStatuses.Failed);
        persisted.Session.FinishedAt.Should().NotBeNull();
        fixture.Authorizer.ReceivedCalls().Should().BeEmpty();
        await fixture.Orchestrator.DidNotReceive().RunAsync(
            Arg.Any<AutonomousRunRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueRunAsync_FailsPersistedSession_WhenInitiatorIsInactive()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        var seeded = await fixture.SeedAsync(Guid.NewGuid(), requiresApproval: false);
        fixture.Authorizer.ResolvePermissionsAsync(
                seeded.Run.TenantId,
                seeded.Run.InitiatorUserId!.Value,
                Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlySet<string>>>(_ => throw new RpcException(
                new Status(StatusCode.Unauthenticated, "orchestrator_caller_inactive")));

        await fixture.ContinueAsync(seeded.Run.Id);
        var persisted = await fixture.LoadAsync(seeded.Run.Id, seeded.Session.Id);

        persisted.Run.Error.Should().Be("schedule_initiator_invalid");
        persisted.Session.Status.Should().Be(AgentSessionStatuses.Failed);
        await fixture.Orchestrator.DidNotReceive().RunAsync(
            Arg.Any<AutonomousRunRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueRunAsync_FailsPersistedSession_WhenRunPermissionWasRevoked()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        var seeded = await fixture.SeedAsync(Guid.NewGuid(), requiresApproval: false);
        fixture.Authorizer.ResolvePermissionsAsync(
                seeded.Run.TenantId,
                seeded.Run.InitiatorUserId!.Value,
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal) { "content:write" });

        await fixture.ContinueAsync(seeded.Run.Id);
        var persisted = await fixture.LoadAsync(seeded.Run.Id, seeded.Session.Id);

        persisted.Run.Error.Should().Be("schedule_initiator_permission_denied");
        persisted.Session.Status.Should().Be(AgentSessionStatuses.Failed);
        await fixture.Orchestrator.DidNotReceive().RunAsync(
            Arg.Any<AutonomousRunRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueRunAsync_DoesNotScanTrends_WhenInitiatorIsMissing()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        var run = await fixture.SeedTrendRunAsync(initiatorUserId: null);

        await fixture.ContinueAsync(run.Id);
        var persisted = await fixture.LoadRunAsync(run.Id);

        persisted.Status.Should().Be("failed");
        persisted.Error.Should().Be("schedule_initiator_missing");
        await fixture.TrendScanner.DidNotReceive().ScanAndPersistAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueRunAsync_FailsWhenRunInitiatorDoesNotMatchSessionInitiator()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        var sessionInitiator = Guid.NewGuid();
        var runInitiator = Guid.NewGuid();
        var seeded = await fixture.SeedAsync(
            sessionInitiator,
            requiresApproval: false,
            runInitiatorUserId: runInitiator);
        fixture.Authorizer.ResolvePermissionsAsync(
                seeded.Run.TenantId,
                runInitiator,
                Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(StringComparer.Ordinal) { "orchestration:run" });

        await fixture.ContinueAsync(seeded.Run.Id);
        var persisted = await fixture.LoadAsync(seeded.Run.Id, seeded.Session.Id);

        persisted.Run.Error.Should().Be("schedule_initiator_mismatch");
        persisted.Session.Status.Should().Be(AgentSessionStatuses.Failed);
        await fixture.Orchestrator.DidNotReceive().RunAsync(
            Arg.Any<AutonomousRunRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContinueRunAsync_ResolvesAndForwardsCurrentPermissionsForEveryRun()
    {
        await using var fixture = await RunnerFixture.CreateAsync();
        var initiatorUserId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var first = await fixture.SeedAsync(initiatorUserId, requiresApproval: false, tenantId: tenantId);
        var second = await fixture.SeedAsync(initiatorUserId, requiresApproval: false, tenantId: tenantId);
        fixture.Authorizer.ResolvePermissionsAsync(first.Run.TenantId, initiatorUserId, Arg.Any<CancellationToken>())
            .Returns(
                new HashSet<string>(StringComparer.Ordinal) { "orchestration:run", "content:write" },
                new HashSet<string>(StringComparer.Ordinal) { "orchestration:run" });
        fixture.Orchestrator.RunAsync(Arg.Any<AutonomousRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AutonomousRunResult.Completed(0)));

        await fixture.ContinueAsync(first.Run.Id);
        await fixture.ContinueAsync(second.Run.Id);

        await fixture.Authorizer.Received(2).ResolvePermissionsAsync(
            tenantId,
            initiatorUserId,
            Arg.Any<CancellationToken>());
        await fixture.Orchestrator.Received(1).RunAsync(
            Arg.Is<AutonomousRunRequest>(request =>
                request.SessionId == first.Session.Id
                && request.ExecutionPermissions.Count == 2
                && request.ExecutionPermissions.Contains("orchestration:run")
                && request.ExecutionPermissions.Contains("content:write")),
            Arg.Any<CancellationToken>());
        await fixture.Orchestrator.Received(1).RunAsync(
            Arg.Is<AutonomousRunRequest>(request =>
                request.SessionId == second.Session.Id
                && request.ExecutionPermissions.Count == 1
                && request.ExecutionPermissions.Contains("orchestration:run")),
            Arg.Any<CancellationToken>());

        var persistedFirst = await fixture.LoadAsync(first.Run.Id, first.Session.Id);
        var persistedSecond = await fixture.LoadAsync(second.Run.Id, second.Session.Id);
        persistedFirst.Run.Status.Should().Be("completed");
        persistedSecond.Run.Status.Should().Be("completed");
    }

    private sealed class RunnerFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _dbOptions;
        private readonly AppDbContext _db;
        private readonly AgentScheduleRunner _runner;

        private RunnerFixture(
            SqliteConnection connection,
            DbContextOptions<AppDbContext> dbOptions,
            AppDbContext db,
            IOrchestratorCallerAuthorizer authorizer,
            IAutonomousOrchestrator orchestrator,
            ITenantTrendScanner trendScanner)
        {
            _connection = connection;
            _dbOptions = dbOptions;
            _db = db;
            Authorizer = authorizer;
            Orchestrator = orchestrator;
            TrendScanner = trendScanner;
            var leaseProvider = Substitute.For<IAgentScheduleLeaseProvider>();
            leaseProvider.TryAcquireAsync(db, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IAsyncDisposable?>(new NoOpLease()));
            _runner = new AgentScheduleRunner(
                db,
                leaseProvider,
                orchestrator,
                trendScanner,
                Substitute.For<IAutonomousRunSink>(),
                authorizer,
                new MutableClock(DateTimeOffset.UtcNow),
                Substitute.For<IServiceScopeFactory>(),
                NullLogger<AgentScheduleRunner>.Instance);
        }

        public IOrchestratorCallerAuthorizer Authorizer { get; }
        public IAutonomousOrchestrator Orchestrator { get; }
        public ITenantTrendScanner TrendScanner { get; }

        public static async Task<RunnerFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(dbOptions, new NullTenantAccessor());
            var createScript = db.Database.GenerateCreateScript()
                .Replace("nvarchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varchar(max)", "TEXT", StringComparison.OrdinalIgnoreCase)
                .Replace("varbinary(max)", "BLOB", StringComparison.OrdinalIgnoreCase)
                .Replace("N'", "'", StringComparison.Ordinal);
            await db.Database.ExecuteSqlRawAsync(createScript);
            return new RunnerFixture(
                connection,
                dbOptions,
                db,
                Substitute.For<IOrchestratorCallerAuthorizer>(),
                Substitute.For<IAutonomousOrchestrator>(),
                Substitute.For<ITenantTrendScanner>());
        }

        public async Task<(AgentScheduleRun Run, AgentSession Session)> SeedAsync(
            Guid? initiatorUserId,
            bool requiresApproval,
            Guid? tenantId = null,
            Guid? runInitiatorUserId = null)
        {
            var now = DateTimeOffset.UtcNow;
            var session = AgentSession.CreatePlan(
                tenantId ?? Guid.NewGuid(),
                "Create scheduled content",
                "{}",
                requiresApproval,
                now,
                initiatorUserId);
            var schedule = AgentSchedule.Create(
                session.TenantId,
                "Scheduled content",
                session.Goal!,
                "daily",
                null,
                "UTC",
                now,
                requiresApproval,
                now,
                initiatorUserId: initiatorUserId);
            var run = AgentScheduleRun.Start(
                session.TenantId,
                schedule.Id,
                Guid.NewGuid().ToString("N"),
                now,
                runInitiatorUserId ?? initiatorUserId);
            run.LinkSession(session.Id);
            _db.AgentSchedules.Add(schedule);
            _db.AgentSessions.Add(session);
            _db.AgentScheduleRuns.Add(run);
            await _db.SaveChangesAsync();
            return (run, session);
        }

        public async Task<AgentScheduleRun> SeedTrendRunAsync(Guid? initiatorUserId)
        {
            var now = DateTimeOffset.UtcNow;
            var tenantId = Guid.NewGuid();
            var schedule = AgentSchedule.Create(
                tenantId,
                "Trend scan",
                ContentTrendSettings.ScheduleGoalMarker,
                "weekly",
                null,
                "UTC",
                now,
                requiresApproval: false,
                now,
                initiatorUserId: initiatorUserId);
            var run = AgentScheduleRun.Start(
                tenantId,
                schedule.Id,
                Guid.NewGuid().ToString("N"),
                now,
                initiatorUserId);
            _db.AgentSchedules.Add(schedule);
            _db.AgentScheduleRuns.Add(run);
            await _db.SaveChangesAsync();
            return run;
        }

        public Task ContinueAsync(Guid runId) => _runner.ContinueRunAsync(runId);

        public async Task<(AgentScheduleRun Run, AgentSession Session)> LoadAsync(
            Guid runId,
            Guid sessionId)
        {
            await using var verificationDb = new AppDbContext(_dbOptions, new NullTenantAccessor());
            var run = await verificationDb.AgentScheduleRuns.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == runId);
            var session = await verificationDb.AgentSessions.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == sessionId);
            return (run, session);
        }

        public async Task<AgentScheduleRun> LoadRunAsync(Guid runId)
        {
            await using var verificationDb = new AppDbContext(_dbOptions, new NullTenantAccessor());
            return await verificationDb.AgentScheduleRuns.IgnoreQueryFilters()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == runId);
        }

        public async ValueTask DisposeAsync()
        {
            await _db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class NoOpLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() =>
            throw new InvalidOperationException("No tenant in test scope.");
    }
}
