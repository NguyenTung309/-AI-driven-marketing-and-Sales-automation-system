using System.Data;
using System.Data.Common;
using Clawbot.AgentService.Services;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Clawbot.Integration.Tests;

public sealed class ContentPublishingPolicyLockTests : IClassFixture<SqlServerFixture>
{
    private const string CompletedAction = "content.agent_review.completed";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan AsyncTestTimeout = TimeSpan.FromSeconds(15);
    private readonly SqlServerFixture _sql;

    public ContentPublishingPolicyLockTests(SqlServerFixture sql) => _sql = sql;

    [Theory]
    [InlineData(
        false,
        Tenant.ContentPublishingPolicyHumanRequired,
        1L)]
    [InlineData(
        true,
        Tenant.ContentPublishingPolicyAutomatic,
        2L)]
    public async Task ResolveAsync_HoldsTenantPolicyLock_UntilCallerTransactionCommits(
        bool useAutomaticPolicy,
        string expectedPolicy,
        long expectedVersion)
    {
        var tenant = Tenant.Create(
            $"policy-lock-{Guid.NewGuid():N}",
            "Policy Lock Test",
            "free",
            Now);
        if (useAutomaticPolicy)
        {
            tenant.SetContentPublishingApprovalPolicy(
                Tenant.ContentPublishingPolicyAutomatic,
                Now);
        }
        await using (var setup = CreateDb(tenant.Id))
        {
            setup.Tenants.Add(tenant);
            await setup.SaveChangesAsync();
        }

        await using var decisionDb = CreateDb(tenant.Id);
        await using var transaction = await decisionDb.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted);
        var resolver = new LockedContentPublishingApprovalPolicyResolver(decisionDb);

        var snapshot = await resolver.ResolveAsync(tenant.Id, CancellationToken.None);

        snapshot.Value.Should().Be(expectedPolicy);
        snapshot.Version.Should().Be(expectedVersion);
        await using var competingConnection = await _sql.OpenConnectionAsync();
        var blockedUpdate = () => UpdatePolicyAsync(
            competingConnection,
            tenant.Id,
            lockTimeoutMilliseconds: 500);
        var lockFailure = await blockedUpdate.Should().ThrowAsync<SqlException>();
        lockFailure.Which.Number.Should().Be(1222);

        await transaction.CommitAsync();

        var updated = await UpdatePolicyAsync(
            competingConnection,
            tenant.Id,
            lockTimeoutMilliseconds: 5_000);
        updated.Should().Be(1);
    }

    [Theory]
    [InlineData(ContentItem.ReviewStatusPassed, "passed")]
    [InlineData(ContentItem.ReviewStatusRejected, "agent_non_pass")]
    [InlineData(ContentItem.ReviewStatusNeedsHuman, "agent_non_pass")]
    [InlineData(ContentItem.ReviewStatusFailed, "reviewer_error")]
    public async Task ProcessAsync_HoldsPolicyLock_ThroughCompletionStateAndAuditCommit(
        string reviewStatus,
        string reasonCode)
    {
        var tenant = Tenant.Create(
            $"coordinator-policy-lock-{Guid.NewGuid():N}",
            "Coordinator Policy Lock Test",
            "free",
            Now);
        tenant.SetContentPublishingApprovalPolicy(
            Tenant.ContentPublishingPolicyAutomatic,
            Now);
        var reviewer = AgentDefinition.Create(
            tenant.Id,
            "reviewer-agent",
            "Reviewer Agent",
            "reviewer",
            "Review content",
            Now);
        var generator = AgentDefinition.Create(
            tenant.Id,
            "generator-agent",
            "Generator Agent",
            "generator",
            "Generate content",
            Now);
        var item = ContentItem.Create(
            tenant.Id,
            "facebook",
            "Nội dung cần duyệt",
            createdBy: null,
            Now,
            createdByAgentId: generator.Id);
        var reviewTask = ContentReviewTask.CreatePending(
            tenant.Id,
            item.Id,
            item.ContentRevision,
            Now,
            Now);
        var leaseToken = Guid.NewGuid();
        reviewTask.Lease(leaseToken, Now.AddHours(1), Now);
        await using (var setup = CreateDb(tenant.Id))
        {
            setup.Tenants.Add(tenant);
            await setup.SaveChangesAsync();

            setup.AgentDefinitions.AddRange(reviewer, generator);
            await setup.SaveChangesAsync();

            setup.ContentItems.Add(item);
            await setup.SaveChangesAsync();

            setup.ContentReviewTasks.Add(reviewTask);
            await setup.SaveChangesAsync();
        }

        var pause = new PauseCompletedAuditCommandInterceptor();
        await using var coordinatorDb = CreateDb(tenant.Id, pause);
        var executor = Substitute.For<IContentReviewExecutor>();
        executor.AgentCode.Returns("reviewer-agent");
        executor.ReviewAsync(
                Arg.Any<ContentReviewExecutionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContentReviewExecutionResult(
                reviewStatus,
                ContentItem.ImageReviewStatusNotApplicable,
                0,
                reasonCode)));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var coordinator = new ContentReviewCoordinator(
            coordinatorDb,
            executor,
            new LockedContentPublishingApprovalPolicyResolver(coordinatorDb),
            clock);

        var processing = coordinator.ProcessAsync(reviewTask.Id, leaseToken);
        var reachedCommitBoundary = pause.BeforeCompletionCommand.Task;
        var firstCompleted = await Task.WhenAny(processing, reachedCommitBoundary)
            .WaitAsync(AsyncTestTimeout);
        if (firstCompleted == processing)
            await processing;
        await reachedCommitBoundary.WaitAsync(AsyncTestTimeout);

        SqlException? lockException = null;
        await using var competingConnection = await _sql.OpenConnectionAsync();
        try
        {
            await UpdatePolicyAsync(
                competingConnection,
                tenant.Id,
                lockTimeoutMilliseconds: 500);
        }
        catch (SqlException ex)
        {
            lockException = ex;
        }
        finally
        {
            pause.ReleaseCompletionCommand.TrySetResult();
        }

        await processing.WaitAsync(AsyncTestTimeout);
        lockException.Should().NotBeNull();
        lockException!.Number.Should().Be(1222);
        (await UpdatePolicyAsync(
            competingConnection,
            tenant.Id,
            lockTimeoutMilliseconds: 5_000)).Should().Be(1);

        await using var verification = CreateDb(tenant.Id);
        var savedItem = await verification.ContentItems.SingleAsync();
        savedItem.AgentReviewStatus.Should().Be(reviewStatus);
        savedItem.AgentReviewReason.Should().Be(reasonCode);
        savedItem.PublishingPolicyApplied.Should().Be(Tenant.ContentPublishingPolicyAutomatic);
        savedItem.PublishingPolicyVersionApplied.Should().Be(2);
        savedItem.Status.Should().Be("draft");
        savedItem.ApprovedRevision.Should().BeNull();
        savedItem.ApprovalMode.Should().BeNull();
        savedItem.ApprovalReason.Should().BeNull();
        savedItem.ApprovedBy.Should().BeNull();
        savedItem.ApprovedByAgentId.Should().BeNull();
        savedItem.ApprovedAt.Should().BeNull();
        (await verification.ContentSchedules.CountAsync()).Should().Be(0);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_ClaimsInitialDeliveryOnce_WhenTwoWorkersStartTogether()
    {
        var seeded = await SeedReviewAsync("coordinator-claim-race");
        var claimBarrier = new PauseInitialReviewClaimCommandInterceptor(2);
        await using var firstDb = CreateDb(seeded.TenantId, claimBarrier);
        await using var secondDb = CreateDb(seeded.TenantId, claimBarrier);
        var reviewCallCount = 0;
        var first = CreateCoordinator(firstDb, () => Interlocked.Increment(ref reviewCallCount));
        var second = CreateCoordinator(secondDb, () => Interlocked.Increment(ref reviewCallCount));

        var firstProcessing = first.ProcessAsync(seeded.TaskId, seeded.LeaseToken);
        var secondProcessing = second.ProcessAsync(seeded.TaskId, seeded.LeaseToken);
        var processing = Task.WhenAll(firstProcessing, secondProcessing);
        var firstCompleted = await Task.WhenAny(processing, claimBarrier.AllClaimsReady.Task)
            .WaitAsync(AsyncTestTimeout);
        if (firstCompleted == processing)
            await processing;
        await claimBarrier.AllClaimsReady.Task.WaitAsync(AsyncTestTimeout);
        claimBarrier.ReleaseClaims.TrySetResult();
        await processing.WaitAsync(AsyncTestTimeout);

        reviewCallCount.Should().Be(1);
        await using var verification = CreateDb(seeded.TenantId);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_DiscardsCompletion_WhenDatabaseLeaseExpiresAfterReview()
    {
        var seeded = await SeedReviewAsync("coordinator-database-expiry");
        await using (var setupConnection = await _sql.OpenConnectionAsync())
        {
            await SetLeaseExpiryAsync(
                setupConnection,
                seeded.TaskId,
                isExpired: false);
        }

        await using var coordinatorDb = CreateDb(seeded.TenantId);
        var executor = Substitute.For<IContentReviewExecutor>();
        executor.AgentCode.Returns("reviewer-agent");
        executor.ReviewAsync(
                Arg.Any<ContentReviewExecutionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await using var connection = await _sql.OpenConnectionAsync();
                await SetLeaseExpiryAsync(
                    connection,
                    seeded.TaskId,
                    isExpired: true);
                return new ContentReviewExecutionResult(
                    ContentItem.ReviewStatusPassed,
                    ContentItem.ImageReviewStatusNotApplicable,
                    0,
                    "passed");
            });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var coordinator = new ContentReviewCoordinator(
            coordinatorDb,
            executor,
            new LockedContentPublishingApprovalPolicyResolver(coordinatorDb),
            clock);

        await coordinator.ProcessAsync(seeded.TaskId, seeded.LeaseToken);

        await using var verification = CreateDb(seeded.TenantId);
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        item.PublishingPolicyApplied.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.ClaimedLeaseToken.Should().Be(seeded.LeaseToken);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_Completes_WhenDatabaseLeaseIsValidAndApplicationClockIsAhead()
    {
        var seeded = await SeedReviewAsync("coordinator-db-clock");
        await using (var setupConnection = await _sql.OpenConnectionAsync())
        {
            await SetLeaseExpiryAsync(
                setupConnection,
                seeded.TaskId,
                isExpired: false);
        }

        await using var coordinatorDb = CreateDb(seeded.TenantId);
        var executor = Substitute.For<IContentReviewExecutor>();
        executor.AgentCode.Returns("reviewer-agent");
        executor.ReviewAsync(
                Arg.Any<ContentReviewExecutionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ContentReviewExecutionResult(
                ContentItem.ReviewStatusPassed,
                ContentItem.ImageReviewStatusNotApplicable,
                0,
                "passed")));
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now.AddYears(10));
        var coordinator = new ContentReviewCoordinator(
            coordinatorDb,
            executor,
            new LockedContentPublishingApprovalPolicyResolver(coordinatorDb),
            clock);

        await coordinator.ProcessAsync(seeded.TaskId, seeded.LeaseToken);

        await using var verification = CreateDb(seeded.TenantId);
        (await verification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPassed);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_DiscardsOldOwner_WhenLeaseChangesAfterFinalValidation()
    {
        var seeded = await SeedReviewAsync("coordinator-final-fence");
        var pause = new PauseCompletedAuditCommandInterceptor();
        await using var coordinatorDb = CreateDb(seeded.TenantId, pause);
        var coordinator = CreateCoordinator(coordinatorDb);

        var processing = coordinator.ProcessAsync(seeded.TaskId, seeded.LeaseToken);
        var firstCompleted = await Task.WhenAny(
                processing,
                pause.BeforeCompletionCommand.Task)
            .WaitAsync(AsyncTestTimeout);
        if (firstCompleted == processing)
            await processing;
        await pause.BeforeCompletionCommand.Task.WaitAsync(AsyncTestTimeout);

        var replacementToken = Guid.NewGuid();
        await using var competingConnection = await _sql.OpenConnectionAsync();
        (await ReplaceLeaseAsync(
            competingConnection,
            seeded.TaskId,
            seeded.LeaseToken,
            replacementToken)).Should().Be(1);
        pause.ReleaseCompletionCommand.TrySetResult();
        await processing.WaitAsync(AsyncTestTimeout);

        await using var verification = CreateDb(seeded.TenantId);
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        item.PublishingPolicyApplied.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(replacementToken);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);

        await using var replacementDb = CreateDb(seeded.TenantId);
        await CreateCoordinator(replacementDb)
            .ProcessAsync(seeded.TaskId, replacementToken);

        await using var completedVerification = CreateDb(seeded.TenantId);
        (await completedVerification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPassed);
        (await completedVerification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        (await completedVerification.AuditLogs.CountAsync(
                audit => audit.Action == CompletedAction))
            .Should().Be(1);
    }

    private async Task<SeededReview> SeedReviewAsync(string slugPrefix)
    {
        var tenant = Tenant.Create(
            $"{slugPrefix}-{Guid.NewGuid():N}",
            "Coordinator Review Test",
            "free",
            Now);
        var reviewer = AgentDefinition.Create(
            tenant.Id,
            "reviewer-agent",
            "Reviewer Agent",
            "reviewer",
            "Review content",
            Now);
        var generator = AgentDefinition.Create(
            tenant.Id,
            "generator-agent",
            "Generator Agent",
            "generator",
            "Generate content",
            Now);
        var item = ContentItem.Create(
            tenant.Id,
            "facebook",
            "Nội dung cần duyệt",
            createdBy: null,
            Now,
            createdByAgentId: generator.Id);
        var reviewTask = ContentReviewTask.CreatePending(
            tenant.Id,
            item.Id,
            item.ContentRevision,
            Now,
            Now);
        var leaseToken = Guid.NewGuid();
        reviewTask.Lease(leaseToken, Now.AddHours(1), Now);

        await using var setup = CreateDb(tenant.Id);
        setup.Tenants.Add(tenant);
        await setup.SaveChangesAsync();
        setup.AgentDefinitions.AddRange(reviewer, generator);
        await setup.SaveChangesAsync();
        setup.ContentItems.Add(item);
        await setup.SaveChangesAsync();
        setup.ContentReviewTasks.Add(reviewTask);
        await setup.SaveChangesAsync();
        await setup.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE dbo.content_review_tasks
            SET lease_expires_at = DATEADD(hour, 1, SYSDATETIMEOFFSET())
            WHERE id = {reviewTask.Id};
            """);
        return new SeededReview(tenant.Id, reviewTask.Id, leaseToken);
    }

    private static ContentReviewCoordinator CreateCoordinator(
        AppDbContext db,
        Action? onReview = null)
    {
        var executor = Substitute.For<IContentReviewExecutor>();
        executor.AgentCode.Returns("reviewer-agent");
        executor.ReviewAsync(
                Arg.Any<ContentReviewExecutionRequest>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                onReview?.Invoke();
                return Task.FromResult(new ContentReviewExecutionResult(
                    ContentItem.ReviewStatusPassed,
                    ContentItem.ImageReviewStatusNotApplicable,
                    0,
                    "passed"));
            });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new ContentReviewCoordinator(
            db,
            executor,
            new LockedContentPublishingApprovalPolicyResolver(db),
            clock);
    }

    private AppDbContext CreateDb(Guid tenantId, params IInterceptor[] interceptors)
    {
        var tenants = Substitute.For<ITenantAccessor>();
        var context = new TenantContext(tenantId, "policy-lock-test");
        tenants.Current.Returns(context);
        tenants.Require().Returns(context);
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.ConnectionString);
        if (interceptors.Length > 0)
            optionsBuilder.AddInterceptors(interceptors);

        return new AppDbContext(optionsBuilder.Options, tenants);
    }

    private static async Task<int> SetLeaseExpiryAsync(
        SqlConnection connection,
        Guid taskId,
        bool isExpired)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = isExpired
            ? """
                UPDATE dbo.content_review_tasks
                SET lease_expires_at = DATEADD(second, -1, SYSDATETIMEOFFSET())
                WHERE id = @taskId;
                """
            : """
                UPDATE dbo.content_review_tasks
                SET lease_expires_at = DATEADD(hour, 1, SYSDATETIMEOFFSET())
                WHERE id = @taskId;
                """;
        command.Parameters.AddWithValue("@taskId", taskId);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ReplaceLeaseAsync(
        SqlConnection connection,
        Guid taskId,
        Guid currentLeaseToken,
        Guid replacementLeaseToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.content_review_tasks
            SET lease_token = @replacementLeaseToken,
                claimed_lease_token = NULL,
                lease_expires_at = DATEADD(hour, 1, SYSDATETIMEOFFSET()),
                attempt_count = attempt_count + 1
            WHERE id = @taskId
              AND lease_token = @currentLeaseToken;
            """;
        command.Parameters.AddWithValue("@replacementLeaseToken", replacementLeaseToken);
        command.Parameters.AddWithValue("@taskId", taskId);
        command.Parameters.AddWithValue("@currentLeaseToken", currentLeaseToken);
        return await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> UpdatePolicyAsync(
        SqlConnection connection,
        Guid tenantId,
        int lockTimeoutMilliseconds)
    {
        await using var command = connection.CreateCommand();
        var lockTimeoutStatement = lockTimeoutMilliseconds switch
        {
            500 => "SET LOCK_TIMEOUT 500;",
            5_000 => "SET LOCK_TIMEOUT 5000;",
            _ => throw new ArgumentOutOfRangeException(
                nameof(lockTimeoutMilliseconds))
        };
        command.CommandText = $"""
            {lockTimeoutStatement}
            UPDATE dbo.tenants
            SET content_publishing_approval_policy = 'automatic',
                content_publishing_policy_version = content_publishing_policy_version + 1,
                content_publishing_policy_updated_at = SYSDATETIMEOFFSET(),
                updated_at = SYSDATETIMEOFFSET()
            WHERE id = @tenantId;
            """;
        command.Parameters.AddWithValue("@tenantId", tenantId);
        return await command.ExecuteNonQueryAsync();
    }

    private sealed record SeededReview(
        Guid TenantId,
        Guid TaskId,
        Guid LeaseToken);
}

internal sealed class PauseInitialReviewClaimCommandInterceptor(int expectedClaims)
    : DbCommandInterceptor
{
    private int _claimCount;

    public TaskCompletionSource AllClaimsReady { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseClaims { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsInitialClaimCommand(command))
        {
            if (Interlocked.Increment(ref _claimCount) == expectedClaims)
                AllClaimsReady.TrySetResult();
            await ReleaseClaims.Task.WaitAsync(cancellationToken);
        }

        return result;
    }

    private static bool IsInitialClaimCommand(DbCommand command) =>
        command.CommandText.Contains(
            "content-review-delivery-claim",
            StringComparison.OrdinalIgnoreCase);
}

internal sealed class PauseCompletedAuditCommandInterceptor : DbCommandInterceptor
{
    public TaskCompletionSource BeforeCompletionCommand { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource ReleaseCompletionCommand { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (IsCompletionFenceCommand(command))
        {
            BeforeCompletionCommand.TrySetResult();
            await ReleaseCompletionCommand.Task.WaitAsync(cancellationToken);
        }

        return result;
    }

    private static bool IsCompletionFenceCommand(DbCommand command) =>
        command.CommandText.Contains(
            "content-review-completion-fence",
            StringComparison.OrdinalIgnoreCase);
}
