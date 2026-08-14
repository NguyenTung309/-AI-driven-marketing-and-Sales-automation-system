using System.Reflection;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Hangfire;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Infrastructure.Tests;

public sealed class RecurringJobDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RunScheduledAsync_CorrelatesHangfireJobAndCompletesExecution()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);

        // Act
        await dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 0, WorkerId: "server-1"));
        fixture.Db.ChangeTracker.Clear();
        var execution = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var attempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();

        // Assert
        execution.Source.Should().Be(RecurringJobExecutionSources.Scheduled);
        execution.HangfireBackgroundJobId.Should().Be("123");
        execution.Status.Should().Be(RecurringJobExecutionStatuses.Succeeded);
        execution.ProgressPercent.Should().Be(100);
        execution.ResultSummary.Should().Be("safe:Database responsive.");
        attempt.AttemptNumber.Should().Be(1);
        attempt.Status.Should().Be(RecurringJobExecutionAttemptStatuses.Succeeded);
        executor.Contexts.Should().ContainSingle().Which.ExecutionId.Should().Be(execution.Id);
    }

    [Fact]
    public async Task RunManualAsync_RejectsDifferentPerformTimeHangfireJobId()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);
        var execution = await fixture.Tracking.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            RecurringJobDefinitions.HealthCheck,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));
        await fixture.Tracking.AttachEnqueueAsync(execution.Id, "expected");

        // Act
        var run = () => dispatcher.RunManualAsync(
            RecurringJobDefinitions.HealthCheck,
            execution.Id,
            new RecurringJobHangfireContext("different", RetryCount: 0, WorkerId: "server-1"));

        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_hangfire_job_id_conflict");
        executor.Contexts.Should().BeEmpty();
        (await fixture.Db.RecurringJobExecutionAttempts.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RunManualAsync_RepairsMissingEnqueueCorrelationBeforeRunningBusinessWork()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);
        var execution = await fixture.Tracking.CreateOrReuseManualAsync(new RecurringJobExecutionRequest(
            RecurringJobDefinitions.HealthCheck,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString("D")));

        // Act
        await dispatcher.RunManualAsync(
            RecurringJobDefinitions.HealthCheck,
            execution.Id,
            new RecurringJobHangfireContext("123", RetryCount: 0, WorkerId: "server-1"));
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();

        // Assert
        persisted.HangfireBackgroundJobId.Should().Be("123");
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Succeeded);
        executor.Contexts.Should().ContainSingle().Which.ExecutionId.Should().Be(execution.Id);
    }

    [Fact]
    public async Task RunScheduledAsync_RecordsSafeRetryableFailureAndRethrowsOriginalException()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var expected = new InvalidOperationException("customer@example.test token=secret");
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromException<RecurringJobExecutionResult>(expected));
        var dispatcher = fixture.CreateDispatcher(executor);

        // Act
        var run = () => dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 1, WorkerId: "server-1"));

        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage(expected.Message);
        fixture.Db.ChangeTracker.Clear();
        var execution = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var attempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();
        execution.Status.Should().Be(RecurringJobExecutionStatuses.Retrying);
        execution.Error.Should().BeNull();
        attempt.Status.Should().Be(RecurringJobExecutionAttemptStatuses.Failed);
        attempt.RetryCount.Should().Be(1);
        attempt.AttemptNumber.Should().Be(2);
        attempt.Error.Should().Be("safe:Tác vụ thực thi không thành công.");
        attempt.Error.Should().NotContain("customer@example.test");
    }

    [Fact]
    public async Task RunScheduledAsync_RecordsInterruptedAttemptWithoutPersistingCancellationExceptionText()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromException<RecurringJobExecutionResult>(
                new OperationCanceledException("customer@example.test token=secret")));
        var dispatcher = fixture.CreateDispatcher(executor);

        // Act
        var run = () => dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 0, WorkerId: "server-1"));

        // Assert
        await run.Should().ThrowAsync<OperationCanceledException>();
        fixture.Db.ChangeTracker.Clear();
        var execution = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var attempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();
        execution.Status.Should().Be(RecurringJobExecutionStatuses.Retrying);
        execution.Error.Should().BeNull();
        attempt.Status.Should().Be(RecurringJobExecutionAttemptStatuses.Failed);
        attempt.Error.Should().Be("safe:Tác vụ bị gián đoạn trước khi hoàn tất.");
        attempt.Error.Should().NotContain("customer@example.test");
    }

    [Fact]
    public async Task RunScheduledAsync_DuplicateRunningDeliveryRethrowsInsteadOfAcknowledgingSuccess()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        _ = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");

        // Act
        var run = () => dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 0, WorkerId: "server-2"));

        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_attempt_already_running");
        executor.Contexts.Should().BeEmpty();
    }

    [Fact]
    public async Task RunScheduledAsync_FailedRetrySlotRedeliveryRethrowsInsteadOfAcknowledgingSuccess()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        var attempt = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        await fixture.Tracking.RecordRetryableFailureAsync(
            execution.Id,
            attempt!.Id,
            "Tác vụ thực thi không thành công.");

        // Act
        var run = () => dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 0, WorkerId: "server-2"));

        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_attempt_retry_slot_already_failed");
        executor.Contexts.Should().BeEmpty();
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Retrying);
        (await fixture.Db.RecurringJobExecutionAttempts.SingleAsync()).Status
            .Should().Be(RecurringJobExecutionAttemptStatuses.Failed);
    }

    [Fact]
    public async Task RunScheduledAsync_RecoversInterruptedPreviousAttemptBeforeRunningNextRetry()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        _ = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");

        // Act
        await dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 1, WorkerId: "server-2"));
        fixture.Db.ChangeTracker.Clear();
        var attempts = await fixture.Db.RecurringJobExecutionAttempts
            .OrderBy(attempt => attempt.AttemptNumber)
            .ToListAsync();

        // Assert
        attempts.Should().HaveCount(2);
        attempts[0].Status.Should().Be(RecurringJobExecutionAttemptStatuses.Failed);
        attempts[0].Error.Should().Be("Tác vụ bị gián đoạn trước khi hoàn tất.");
        attempts[1].Status.Should().Be(RecurringJobExecutionAttemptStatuses.Succeeded);
    }

    [Fact]
    public async Task FailureFinalizer_FinalizesOnlyOnceUsingPersistedSafeAttemptError()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        var attempt = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        await fixture.Tracking.RecordRetryableFailureAsync(
            execution.Id,
            attempt!.Id,
            "customer@example.test token=secret");
        var notifier = new RecordingFailureNotifier();
        var finalizer = new RecurringJobExecutionFailureFinalizer(fixture.Tracking, notifier);

        // Act
        var first = await finalizer.FinalizeAsync(
            RecurringJobDefinitions.HealthCheck,
            "123",
            retryCount: 0);
        var second = await finalizer.FinalizeAsync(
            RecurringJobDefinitions.HealthCheck,
            "123",
            retryCount: 0);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();

        // Assert
        first.Should().BeTrue();
        second.Should().BeFalse();
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Failed);
        persisted.Error.Should().Be("safe:customer@example.test token=secret");
        notifier.Notifications.Should().ContainSingle().Which.Error.Should().Be(persisted.Error);
    }

    [Fact]
    public async Task FailureFinalizer_RecoversFinalRunningAttemptWithApprovedSafeError()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        _ = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        var notifier = new RecordingFailureNotifier();
        var finalizer = new RecurringJobExecutionFailureFinalizer(fixture.Tracking, notifier);

        // Act
        var finalized = await finalizer.FinalizeAsync(
            RecurringJobDefinitions.HealthCheck,
            "123",
            retryCount: 0);
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var attempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();

        // Assert
        finalized.Should().BeTrue();
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Failed);
        persisted.Error.Should().Be("safe:Tác vụ thực thi không thành công.");
        attempt.Status.Should().Be(RecurringJobExecutionAttemptStatuses.Failed);
        attempt.Error.Should().Be("safe:Tác vụ thực thi không thành công.");
        notifier.Notifications.Should().ContainSingle().Which.Error.Should().Be(persisted.Error);
    }

    [Fact]
    public async Task RunScheduledAsync_RejectsTerminalExecutionRequeueWithoutRunningBusinessWork()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        var attempt = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        await fixture.Tracking.RecordRetryableFailureAsync(
            execution.Id,
            attempt!.Id,
            "Tác vụ thực thi không thành công.");
        _ = await fixture.Tracking.FinalizeFailureAsync(execution.Id, "123", retryCount: 0);
        var executor = new RecordingExecutor(
            RecurringJobDefinitions.HealthCheck,
            (_, _) => Task.FromResult(new RecurringJobExecutionResult(null, "Database responsive.")));
        var dispatcher = fixture.CreateDispatcher(executor);

        // Act
        var run = () => dispatcher.RunScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            new RecurringJobHangfireContext("123", RetryCount: 0, WorkerId: "server-2"));

        // Assert
        await run.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("recurring_execution_terminal_requeue_not_supported");
        executor.Contexts.Should().BeEmpty();
    }

    [Fact]
    public async Task FailureReconciliation_FinalizesDurablyFailedExecutionAndNotifiesOnlyOnce()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        var attempt = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        await fixture.Tracking.RecordRetryableFailureAsync(
            execution.Id,
            attempt!.Id,
            "customer@example.test token=secret");
        fixture.HangfireStates.SetFailed("123", retryCount: 0);

        // Act
        await fixture.Reconciler.RunAsync();
        await fixture.Reconciler.RunAsync();
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();

        // Assert
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Failed);
        persisted.Error.Should().Be("safe:customer@example.test token=secret");
        fixture.Notifier.Notifications.Should().ContainSingle()
            .Which.Error.Should().Be(persisted.Error);
    }

    [Fact]
    public async Task FailureReconciliation_RecoversFinalRunningAttemptWithoutHangfireFailureText()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        _ = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        fixture.HangfireStates.SetFailed("123", retryCount: 0);

        // Act
        await fixture.Reconciler.RunAsync();
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var persistedAttempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();

        // Assert
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Failed);
        persisted.Error.Should().Be("safe:Tác vụ thực thi không thành công.");
        persistedAttempt.Error.Should().Be("safe:Tác vụ thực thi không thành công.");
        fixture.Notifier.Notifications.Should().ContainSingle();
    }

    [Fact]
    public async Task FailureReconciliation_DoesNothingWhenHangfireStateIsNotFailed()
    {
        // Arrange
        await using var fixture = await DispatcherFixture.CreateAsync();
        var execution = await fixture.Tracking.CreateOrGetScheduledAsync(
            RecurringJobDefinitions.HealthCheck,
            "123");
        _ = await fixture.Tracking.StartAttemptAsync(execution.Id, "123", 0, "server-1");
        fixture.HangfireStates.SetActive("123");

        // Act
        await fixture.Reconciler.RunAsync();
        fixture.Db.ChangeTracker.Clear();
        var persisted = await fixture.Db.RecurringJobExecutions.SingleAsync();
        var persistedAttempt = await fixture.Db.RecurringJobExecutionAttempts.SingleAsync();

        // Assert
        persisted.Status.Should().Be(RecurringJobExecutionStatuses.Running);
        persistedAttempt.Status.Should().Be(RecurringJobExecutionAttemptStatuses.Running);
        fixture.Notifier.Notifications.Should().BeEmpty();
    }

    [Fact]
    public void TrackedFailureFilter_ChangesTerminalRequeueToDeletedState()
    {
        // Arrange
        var method = typeof(HealthCheckRecurringJob).GetMethod(
            nameof(HealthCheckRecurringJob.RunScheduledAsync),
            BindingFlags.Instance | BindingFlags.Public);
        var backgroundJob = new Hangfire.BackgroundJob(
            "123",
            new Hangfire.Common.Job(
                typeof(HealthCheckRecurringJob),
                method!,
                [null!, CancellationToken.None]),
            Now.UtcDateTime);
        var applied = new ApplyStateContext(
            new TestJobStorage(),
            Substitute.For<IStorageConnection>(),
            Substitute.For<IWriteOnlyTransaction>(),
            backgroundJob,
            new EnqueuedState(),
            "Failed");
        var election = new ElectStateContext(applied);
        var filter = new RecurringJobExecutionFailureFilter(
            NullLogger<RecurringJobExecutionFailureFilter>.Instance);

        // Act
        filter.OnStateElection(election);

        // Assert
        election.CandidateState.Should().BeOfType<DeletedState>()
            .Which.Reason.Should().Be("tracked_recurring_execution_terminal_requeue_not_supported");
    }

    [Fact]
    public void TrackedFailureFilter_ImplementsHangfireApplyAndElectionFilters()
    {
        typeof(RecurringJobExecutionFailureFilter).Should().Implement<IApplyStateFilter>();
        typeof(RecurringJobExecutionFailureFilter).Should().Implement<IElectStateFilter>();
    }

    [Fact]
    public void LegacyFailureFilter_ExcludesTrackedWrapperJobs()
    {
        JobFailureNotificationFilter.ShouldHandle(typeof(HealthCheckRecurringJob)).Should().BeFalse();
    }

    [Fact]
    public void HealthCheckTrackedWrapper_RetainsSixtySecondConcurrencyLock()
    {
        var scheduledMethod = typeof(HealthCheckRecurringJob).GetMethod(
            nameof(HealthCheckRecurringJob.RunScheduledAsync),
            BindingFlags.Instance | BindingFlags.Public);
        var manualMethod = typeof(HealthCheckRecurringJob).GetMethod(
            nameof(HealthCheckRecurringJob.RunManualAsync),
            BindingFlags.Instance | BindingFlags.Public);
        var scheduledAttribute = scheduledMethod!.GetCustomAttribute<DisableConcurrentExecutionAttribute>();
        var manualAttribute = manualMethod!.GetCustomAttribute<DisableConcurrentExecutionAttribute>();

        HealthCheckRecurringJob.LegacyConcurrencyResource
            .Should().Be("Clawbot.Infrastructure.Jobs.HealthCheckJob.RunAsync");
        scheduledAttribute.Should().NotBeNull();
        manualAttribute.Should().NotBeNull();
        scheduledAttribute!.TimeoutSec.Should().Be(60);
        manualAttribute!.TimeoutSec.Should().Be(60);
        scheduledAttribute.Resource.Should().Be(HealthCheckRecurringJob.LegacyConcurrencyResource);
        manualAttribute.Resource.Should().Be(HealthCheckRecurringJob.LegacyConcurrencyResource);
    }

    private sealed class DispatcherFixture(SqliteConnection connection, AppDbContext db) : IAsyncDisposable
    {
        public AppDbContext Db { get; } = db;

        public RecurringJobExecutionService Tracking { get; } = new(
            db,
            new RecordingRedactor(),
            new FixedClock(Now),
            NullLogger<RecurringJobExecutionService>.Instance);

        public RecordingFailureNotifier Notifier { get; } = new();

        public RecordingHangfireStateReader HangfireStates { get; } = new();

        public RecurringJobExecutionFailureReconciliationJob Reconciler => new(
            Tracking,
            new RecurringJobExecutionFailureFinalizer(Tracking, Notifier),
            HangfireStates,
            NullLogger<RecurringJobExecutionFailureReconciliationJob>.Instance);

        public RecurringJobDispatcher CreateDispatcher(IRecurringJobExecutor executor) => new(
            new RecurringJobDefinitionRegistry([executor]),
            Tracking,
            NullLogger<RecurringJobDispatcher>.Instance);

        public static async Task<DispatcherFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options, new NullTenantAccessor());
            await db.Database.EnsureCreatedAsync();
            return new DispatcherFixture(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class RecordingExecutor(
        string definitionId,
        Func<RecurringJobExecutionContext, CancellationToken, Task<RecurringJobExecutionResult>> execute)
        : IRecurringJobExecutor
    {
        public string DefinitionId { get; } = definitionId;

        public List<RecurringJobExecutionContext> Contexts { get; } = [];

        public async Task<RecurringJobExecutionResult> ExecuteAsync(
            RecurringJobExecutionContext context,
            CancellationToken ct)
        {
            Contexts.Add(context);
            return await execute(context, ct);
        }
    }

    private sealed class TestJobStorage : JobStorage
    {
        public override IStorageConnection GetConnection() => throw new NotSupportedException();

        public override IMonitoringApi GetMonitoringApi() => throw new NotSupportedException();
    }

    private sealed class RecordingFailureNotifier : IRecurringJobExecutionFailureNotifier
    {
        public List<(string DefinitionId, string Error)> Notifications { get; } = [];

        public Task NotifyAsync(string definitionId, string safeError, CancellationToken ct)
        {
            Notifications.Add((definitionId, safeError));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHangfireStateReader : IRecurringJobExecutionHangfireStateReader
    {
        private readonly Dictionary<string, RecurringJobExecutionHangfireState> _states =
            new(StringComparer.Ordinal);

        public RecurringJobExecutionHangfireState? Find(string hangfireJobId) =>
            _states.GetValueOrDefault(hangfireJobId);

        public void SetFailed(string hangfireJobId, int retryCount) =>
            _states[hangfireJobId] = new RecurringJobExecutionHangfireState("Failed", retryCount);

        public void SetActive(string hangfireJobId) =>
            _states[hangfireJobId] = new RecurringJobExecutionHangfireState("Processing", 0);
    }

    private sealed class RecordingRedactor : IPiiRedactor
    {
        public string Name => "test";

        public Task<RedactionResult> RedactAsync(string text, CancellationToken ct) =>
            Task.FromResult(new RedactionResult($"safe:{text}", Array.Empty<PiiSpan>()));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;

        public TenantContext Require() => throw new InvalidOperationException("No tenant in test scope.");
    }
}
