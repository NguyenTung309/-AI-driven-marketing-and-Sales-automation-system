using Clawbot.AgentService.Services;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ReviewTenantWorkerTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);

    private readonly SqliteConnection _connection;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();

    public ReviewTenantWorkerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var setup = CreateDb(_tenantA);
        setup.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    [Fact]
    public async Task RunTenantAsync_RejectsEmptyTenantId()
    {
        var worker = CreateWorker(
            CreateDb(tenantId: null),
            new RecordingContentReviewCoordinator());

        var act = () => worker.RunTenantAsync(Guid.Empty);

        await act.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("tenantId");
    }

    [Fact]
    public async Task RunTenantAsync_LeasesOnlyTasksOwnedByRequestedTenant()
    {
        var itemA = CreateItem(_tenantA, "Tenant A body");
        var itemB = CreateItem(_tenantB, "Tenant B body");
        var taskA = ContentReviewTask.CreatePending(
            _tenantA,
            itemA.Id,
            itemA.ContentRevision,
            Now.AddMinutes(-2),
            Now.AddMinutes(-10));
        var taskB = ContentReviewTask.CreatePending(
            _tenantB,
            itemB.Id,
            itemB.ContentRevision,
            Now.AddMinutes(-5),
            Now.AddMinutes(-20));
        await SeedAsync(
            tenantA: true,
            tenantB: true,
            items: [itemA, itemB],
            tasks: [taskA, taskB]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().ContainSingle();
        coordinator.Calls[0].TenantId.Should().Be(_tenantA);
        coordinator.Calls[0].TaskId.Should().Be(taskA.Id);

        await using var verification = CreateDb(tenantId: null);
        var savedA = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(task => task.Id == taskA.Id);
        var savedB = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(task => task.Id == taskB.Id);
        savedA.Status.Should().Be(ContentReviewTask.StatusLeased);
        savedA.LeaseToken.Should().Be(coordinator.Calls[0].LeaseToken);
        savedB.Status.Should().Be(ContentReviewTask.StatusPending);
        savedB.LeaseToken.Should().BeNull();
        savedB.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task RunTenantAsync_WorksWhenTenantAccessorHasNoCurrentTenant()
    {
        var item = CreateItem(_tenantA, "No ambient tenant");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().ContainSingle();
        coordinator.Calls[0].TenantId.Should().Be(_tenantA);
        await using var verification = CreateDb(tenantId: null);
        (await verification.ContentReviewTasks
                .IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == task.Id))
            .Status.Should().Be(ContentReviewTask.StatusLeased);
    }

    [Fact]
    public async Task RunTenantAsync_LeasesPendingTask_WhenNextAttemptEqualsNow()
    {
        var item = CreateItem(_tenantA, "Due exactly now");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().ContainSingle();
        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        saved.Status.Should().Be(ContentReviewTask.StatusLeased);
        saved.AttemptCount.Should().Be(1);
        saved.LeaseExpiresAt.Should().Be(Now.Add(TestOptions().LeaseDuration));
    }

    [Fact]
    public async Task RunTenantAsync_SkipsPendingTask_WhenNextAttemptIsAfterNow()
    {
        var item = CreateItem(_tenantA, "Future due");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now.AddMinutes(5),
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().BeEmpty();
        await using var verification = CreateDb(tenantId: null);
        (await verification.ContentReviewTasks
                .IgnoreQueryFilters()
                .SingleAsync(candidate => candidate.Id == task.Id))
            .Status.Should().Be(ContentReviewTask.StatusPending);
    }

    [Fact]
    public async Task RunTenantAsync_SkipsUnexpiredLease()
    {
        var item = CreateItem(_tenantA, "Still leased");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now.AddMinutes(-10),
            Now.AddMinutes(-10));
        var originalToken = Guid.NewGuid();
        task.Lease(originalToken, Now.AddMinutes(5), Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().BeEmpty();
        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        saved.Status.Should().Be(ContentReviewTask.StatusLeased);
        saved.LeaseToken.Should().Be(originalToken);
    }

    [Fact]
    public async Task RunTenantAsync_ReclaimsLease_WhenLeaseExpiresExactlyAtNow()
    {
        var item = CreateItem(_tenantA, "Exact expiry reclaim");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now.AddMinutes(-10),
            Now.AddMinutes(-10));
        var originalToken = Guid.NewGuid();
        task.Lease(originalToken, Now, Now.AddMinutes(-5));
        task.TryClaimDelivery(originalToken, Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().ContainSingle();
        coordinator.Calls[0].LeaseToken.Should().NotBe(originalToken);
        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        saved.Status.Should().Be(ContentReviewTask.StatusLeased);
        saved.LeaseToken.Should().Be(coordinator.Calls[0].LeaseToken);
        saved.ClaimedLeaseToken.Should().BeNull();
        saved.AttemptCount.Should().Be(2);
        saved.LastErrorCode.Should().Be("lease_expired");
        saved.LeaseExpiresAt.Should().Be(Now.Add(TestOptions().LeaseDuration));
    }

    [Fact]
    public async Task RunTenantAsync_CommitsLeaseBeforeCallingCoordinator()
    {
        var item = CreateItem(_tenantA, "Persist before dispatch");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        Guid? observedToken = null;
        string? observedStatus = null;
        var coordinator = new RecordingContentReviewCoordinator(
            async (tenantId, taskId, leaseToken, _) =>
            {
                await using var probe = CreateDb(tenantId: null);
                var saved = await probe.ContentReviewTasks
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleAsync(candidate => candidate.Id == taskId, CancellationToken.None);
                observedToken = saved.LeaseToken;
                observedStatus = saved.Status;
                tenantId.Should().Be(_tenantA);
                saved.LeaseToken.Should().Be(leaseToken);
            });
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().ContainSingle();
        observedStatus.Should().Be(ContentReviewTask.StatusLeased);
        observedToken.Should().NotBeNull();
        observedToken!.Value.Should().Be(coordinator.Calls[0].LeaseToken);
    }

    [Fact]
    public async Task RunTenantAsync_PassesExactTenantTaskLeaseAndCancellationToken()
    {
        var item = CreateItem(_tenantA, "Token forwarding");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        using var cts = new CancellationTokenSource();
        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA, cts.Token);

        coordinator.Calls.Should().ContainSingle();
        coordinator.Calls[0].TenantId.Should().Be(_tenantA);
        coordinator.Calls[0].TaskId.Should().Be(task.Id);
        coordinator.Calls[0].LeaseToken.Should().NotBe(Guid.Empty);
        coordinator.Calls[0].CancellationToken.Should().Be(cts.Token);
    }

    [Fact]
    public async Task RunTenantAsync_StopsAfterConfiguredBatchSize()
    {
        var items = Enumerable.Range(0, 3)
            .Select(index => CreateItem(_tenantA, $"Batch {index}"))
            .ToArray();
        var tasks = items
            .Select((item, index) => ContentReviewTask.CreatePending(
                _tenantA,
                item.Id,
                item.ContentRevision,
                Now.AddMinutes(-index),
                Now.AddMinutes(-10 + index)))
            .ToArray();
        await SeedAsync(tenantA: true, tenantB: false, items: items, tasks: tasks);

        var options = TestOptions() with { BatchSize = 2 };
        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator, options);

        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().HaveCount(2);
        await using var verification = CreateDb(tenantId: null);
        var leased = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .CountAsync(candidate => candidate.Status == ContentReviewTask.StatusLeased);
        leased.Should().Be(2);
    }

    [Fact]
    public async Task RunTenantAsync_ReleasesLeaseForRetry_WhenCoordinatorThrowsOperationalFailure()
    {
        var item = CreateItem(_tenantA, "Retry after operational failure");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var options = TestOptions() with
        {
            InitialRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromMinutes(5)
        };
        var coordinator = new RecordingContentReviewCoordinator(
            (_, _, _, _) => throw new InvalidOperationException("provider_timeout"));
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator, options);

        await worker.RunTenantAsync(_tenantA);

        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        saved.Status.Should().Be(ContentReviewTask.StatusPending);
        saved.LeaseToken.Should().BeNull();
        saved.ClaimedLeaseToken.Should().BeNull();
        saved.LeaseExpiresAt.Should().BeNull();
        saved.NextAttemptAt.Should().Be(Now.AddSeconds(10));
        saved.LastErrorCode.Should().NotBeNullOrWhiteSpace();
        saved.LastErrorCode.Should().NotContain("provider_timeout");
        saved.LastErrorCode.Should().NotContain("Exception");
    }

    [Fact]
    public async Task RunTenantAsync_AppliesExponentialBackoff_FromAttemptCount()
    {
        var item = CreateItem(_tenantA, "Backoff");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now.AddMinutes(-2),
            Now.AddMinutes(-3));
        // Seed already has one completed attempt via lease/release.
        var priorToken = Guid.NewGuid();
        task.Lease(priorToken, Now.AddMinutes(3), Now.AddMinutes(-2));
        task.ReleaseForRetry(
            priorToken,
            Now.AddMinutes(-1),
            "reviewer_error",
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var options = TestOptions() with
        {
            InitialRetryDelay = TimeSpan.FromSeconds(10),
            MaxRetryDelay = TimeSpan.FromMinutes(5)
        };
        var coordinator = new RecordingContentReviewCoordinator(
            (_, _, _, _) => throw new InvalidOperationException("transient"));
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator, options);

        await worker.RunTenantAsync(_tenantA);

        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        // AttemptCount becomes 2 after re-lease; backoff = 10s * 2^(2-1) = 20s
        saved.NextAttemptAt.Should().Be(Now.AddSeconds(20));
    }

    [Fact]
    public async Task RunTenantAsync_FailsTask_WhenFinalAttemptThrows()
    {
        var item = CreateItem(_tenantA, "Final attempt");
        var cursor = Now.AddMinutes(-10);
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            cursor,
            cursor.AddMinutes(-1));
        // Leave exactly one attempt remaining before the worker leases the final try.
        for (var attempt = 0; attempt < ContentItem.MaxAgentReviewAttempts - 1; attempt++)
        {
            var token = Guid.NewGuid();
            task.Lease(token, cursor.AddMinutes(5), cursor);
            var releasedAt = cursor.AddMinutes(1);
            var nextAttemptAt = attempt == ContentItem.MaxAgentReviewAttempts - 2
                ? Now
                : releasedAt;
            task.ReleaseForRetry(
                token,
                nextAttemptAt,
                "reviewer_error",
                releasedAt);
            cursor = releasedAt;
        }

        task.AttemptCount.Should().Be(ContentItem.MaxAgentReviewAttempts - 1);
        task.NextAttemptAt.Should().BeOnOrBefore(Now);
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator(
            (_, _, _, _) => throw new InvalidOperationException("still_failing"));
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);

        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        saved.Status.Should().Be(ContentReviewTask.StatusFailed);
        saved.LastErrorCode.Should().Be("content_review_attempt_limit_reached");
        saved.LeaseToken.Should().BeNull();
    }

    [Fact]
    public async Task RunTenantAsync_PropagatesCancellationFromCoordinator()
    {
        var item = CreateItem(_tenantA, "Cancel");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        using var cts = new CancellationTokenSource();
        var coordinator = new RecordingContentReviewCoordinator(
            (_, _, _, token) =>
            {
                cts.Cancel();
                throw new OperationCanceledException(cts.Token);
            });
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        var act = () => worker.RunTenantAsync(_tenantA, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await using var verification = CreateDb(tenantId: null);
        var saved = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == task.Id);
        saved.Status.Should().Be(ContentReviewTask.StatusLeased);
        saved.LeaseToken.Should().NotBeNull();
    }

    [Fact]
    public async Task RunTenantAsync_DoesNotDispatchSameLeaseTwice()
    {
        var item = CreateItem(_tenantA, "Active lease no redispatch");
        var task = ContentReviewTask.CreatePending(
            _tenantA,
            item.Id,
            item.ContentRevision,
            Now,
            Now.AddMinutes(-1));
        await SeedAsync(tenantA: true, tenantB: false, items: [item], tasks: [task]);

        var coordinator = new RecordingContentReviewCoordinator();
        var worker = CreateWorker(CreateDb(tenantId: null), coordinator);

        await worker.RunTenantAsync(_tenantA);
        await worker.RunTenantAsync(_tenantA);

        coordinator.Calls.Should().ContainSingle();
    }

    private static ReviewTenantWorker CreateWorker(
        AppDbContext db,
        IContentReviewCoordinator coordinator,
        ContentReviewWorkerOptions? options = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ReviewTenantWorker(
            db,
            coordinator,
            clock,
            Options.Create(options ?? TestOptions()));
    }

    private static ContentReviewWorkerOptions TestOptions() => new()
    {
        PollInterval = TimeSpan.FromSeconds(1),
        LeaseDuration = TimeSpan.FromMinutes(5),
        BatchSize = 10,
        InitialRetryDelay = TimeSpan.FromSeconds(30),
        MaxRetryDelay = TimeSpan.FromMinutes(15),
        MaxAttempts = ContentItem.MaxAgentReviewAttempts
    };

    private async Task SeedAsync(
        bool tenantA,
        bool tenantB,
        IReadOnlyList<ContentItem> items,
        IReadOnlyList<ContentReviewTask> tasks)
    {
        await using var setup = CreateDb(_tenantA);
        if (tenantA)
        {
            setup.Tenants.Add(Tenant.Create(
                $"tenant-a-{_tenantA:N}"[..32],
                "Tenant A",
                "free",
                Now.AddHours(-1)));
        }

        if (tenantB)
        {
            setup.Tenants.Add(Tenant.Create(
                $"tenant-b-{_tenantB:N}"[..32],
                "Tenant B",
                "free",
                Now.AddHours(-1)));
        }

        await setup.SaveChangesAsync();
        setup.ContentItems.AddRange(items);
        await setup.SaveChangesAsync();
        setup.ContentReviewTasks.AddRange(tasks);
        await setup.SaveChangesAsync();
    }

    private static ContentItem CreateItem(Guid tenantId, string body) =>
        ContentItem.Create(
            tenantId,
            "facebook",
            body,
            createdBy: null,
            Now.AddMinutes(-30),
            createdByAgentId: Guid.NewGuid());

    private AppDbContext CreateDb(Guid? tenantId)
    {
        var tenants = Substitute.For<ITenantAccessor>();
        if (tenantId is { } id)
        {
            var context = new TenantContext(id, "review-worker-test");
            tenants.Current.Returns(context);
            tenants.Require().Returns(context);
        }
        else
        {
            tenants.Current.Returns((TenantContext?)null);
            tenants.Require().Returns(_ => throw new InvalidOperationException("tenant_required"));
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .ReplaceService<IModelCustomizer, SqliteFriendlyModelCustomizer>()
            .Options;
        return new AppDbContext(options, tenants);
    }

    private sealed class RecordingContentReviewCoordinator : IContentReviewCoordinator
    {
        private readonly Func<Guid, Guid, Guid, CancellationToken, Task>? _handler;

        public RecordingContentReviewCoordinator(
            Func<Guid, Guid, Guid, CancellationToken, Task>? handler = null) =>
            _handler = handler;

        public List<CoordinatorCall> Calls { get; } = [];

        public async Task ProcessAsync(
            Guid tenantId,
            Guid taskId,
            Guid leaseToken,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new CoordinatorCall(tenantId, taskId, leaseToken, cancellationToken));
            if (_handler is not null)
                await _handler(tenantId, taskId, leaseToken, cancellationToken);
        }
    }

    private sealed record CoordinatorCall(
        Guid TenantId,
        Guid TaskId,
        Guid LeaseToken,
        CancellationToken CancellationToken);
}
