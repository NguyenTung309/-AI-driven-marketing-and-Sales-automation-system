using System.Data.Common;
using Clawbot.AgentService.Services;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

internal sealed record ContentReviewCoordinatorProbe(
    AgentServiceTestAppDb Database,
    AppDbContext CoordinatorDb,
    Guid ContentItemId,
    Guid ReviewTaskId,
    Guid ReviewerAgentId);

internal sealed class ContentReviewCoordinatorHarness : IAsyncDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 7, 21, 8, 0, 0, TimeSpan.Zero);

    private ContentReviewCoordinatorHarness(
        AgentServiceTestAppDb database,
        AppDbContext coordinatorDb,
        ContentReviewCoordinator coordinator,
        RecordingContentReviewExecutor executor,
        RecordingContentPublishingPolicyResolver policyResolver,
        Guid tenantId,
        Guid contentItemId,
        Guid reviewTaskId,
        Guid reviewerAgentId,
        Guid leaseToken)
    {
        Database = database;
        CoordinatorDb = coordinatorDb;
        Coordinator = coordinator;
        Executor = executor;
        PolicyResolver = policyResolver;
        TenantId = tenantId;
        ContentItemId = contentItemId;
        ReviewTaskId = reviewTaskId;
        ReviewerAgentId = reviewerAgentId;
        LeaseToken = leaseToken;
    }

    public AgentServiceTestAppDb Database { get; }
    public AppDbContext CoordinatorDb { get; }
    public ContentReviewCoordinator Coordinator { get; }
    public RecordingContentReviewExecutor Executor { get; }
    public RecordingContentPublishingPolicyResolver PolicyResolver { get; }
    public Guid TenantId { get; }
    public Guid ContentItemId { get; }
    public Guid ReviewTaskId { get; }
    public Guid ReviewerAgentId { get; }
    public Guid LeaseToken { get; }

    public static async Task<ContentReviewCoordinatorHarness> CreateAsync(
        string initialPolicy = Tenant.ContentPublishingPolicyHumanRequired,
        bool reviewerIsGenerator = false,
        Func<ContentReviewCoordinatorProbe, ContentReviewExecutionRequest, CancellationToken,
            Task<ContentReviewExecutionResult>>? reviewHandler = null,
        Func<ContentReviewCoordinatorProbe, Guid, CancellationToken,
            Task<ContentPublishingPolicySnapshot>>? policyHandler = null,
        DateTimeOffset? processingAt = null,
        DateTimeOffset? leaseExpiresAt = null,
        DateTimeOffset? leaseStartedAt = null,
        IClock? coordinatorClock = null,
        params IInterceptor[] interceptors)
    {
        var tenant = Tenant.Create("coordinator-test", "Coordinator Test", "free", Now.AddHours(-2));
        if (initialPolicy != Tenant.ContentPublishingPolicyHumanRequired)
            tenant.SetContentPublishingApprovalPolicy(initialPolicy, Now.AddHours(-1));

        var database = new AgentServiceTestAppDb(tenant.Id);
        var reviewer = AgentDefinition.Create(
            tenant.Id,
            "reviewer-agent",
            "Reviewer Agent",
            "reviewer",
            "Review content",
            Now.AddHours(-2));
        var generator = reviewerIsGenerator
            ? null
            : AgentDefinition.Create(
                tenant.Id,
                "generator-agent",
                "Generator Agent",
                "generator",
                "Generate content",
                Now.AddHours(-2));
        var item = ContentItem.Create(
            tenant.Id,
            "facebook",
            "Nội dung cần duyệt",
            createdBy: null,
            Now.AddHours(-1),
            createdByAgentId: reviewerIsGenerator ? reviewer.Id : generator!.Id);
        var reviewTask = ContentReviewTask.CreatePending(
            tenant.Id,
            item.Id,
            item.ContentRevision,
            leaseStartedAt ?? Now,
            Now.AddMinutes(-5));
        var leaseToken = Guid.NewGuid();
        reviewTask.Lease(
            leaseToken,
            leaseExpiresAt ?? Now.AddHours(1),
            leaseStartedAt ?? Now);

        database.Db.Tenants.Add(tenant);
        database.Db.AgentDefinitions.Add(reviewer);
        if (generator is not null)
            database.Db.AgentDefinitions.Add(generator);
        database.Db.ContentItems.Add(item);
        database.Db.ContentReviewTasks.Add(reviewTask);
        await database.Db.SaveChangesAsync();

        var coordinatorDb = database.CreateDbContext(interceptors);
        var probe = new ContentReviewCoordinatorProbe(
            database,
            coordinatorDb,
            item.Id,
            reviewTask.Id,
            reviewer.Id);
        var executor = new RecordingContentReviewExecutor(
            (request, cancellationToken) => reviewHandler is null
                ? Task.FromResult(ContentReviewResults.Passed)
                : reviewHandler(probe, request, cancellationToken));
        var policyResolver = new RecordingContentPublishingPolicyResolver(
            async (tenantId, cancellationToken) =>
            {
                if (policyHandler is not null)
                    return await policyHandler(probe, tenantId, cancellationToken);

                var currentTenant = await coordinatorDb.Tenants.SingleAsync(
                    candidate => candidate.Id == tenantId,
                    cancellationToken);
                return new ContentPublishingPolicySnapshot(
                    currentTenant.ContentPublishingApprovalPolicy,
                    currentTenant.ContentPublishingPolicyVersion);
            });
        var clock = coordinatorClock ?? Substitute.For<IClock>();
        if (coordinatorClock is null)
            clock.UtcNow.Returns(processingAt ?? Now);
        var autoScheduler = new ContentAutoScheduler(
            coordinatorDb,
            new DefaultGoldenHourResolver());
        var coordinator = new ContentReviewCoordinator(
            coordinatorDb,
            executor,
            policyResolver,
            clock,
            autoScheduler);

        return new ContentReviewCoordinatorHarness(
            database,
            coordinatorDb,
            coordinator,
            executor,
            policyResolver,
            tenant.Id,
            item.Id,
            reviewTask.Id,
            reviewer.Id,
            leaseToken);
    }

    public async ValueTask DisposeAsync()
    {
        await CoordinatorDb.DisposeAsync();
        Database.Dispose();
    }
}

internal sealed class SequencedClock(params DateTimeOffset[] timestamps) : IClock
{
    private readonly DateTimeOffset[] _timestamps = timestamps.Length > 0
        ? timestamps.ToArray()
        : throw new ArgumentException("clock_timestamp_required", nameof(timestamps));
    private int _readIndex = -1;

    public DateTimeOffset UtcNow
    {
        get
        {
            var index = Interlocked.Increment(ref _readIndex);
            return _timestamps[Math.Min(index, _timestamps.Length - 1)];
        }
    }
}

internal static class ContentReviewResults
{
    public static readonly ContentReviewExecutionResult Passed = new(
        ContentItem.ReviewStatusPassed,
        ContentItem.ImageReviewStatusNotApplicable,
        0,
        "passed");

    public static readonly ContentReviewExecutionResult NeedsHuman = new(
        ContentItem.ReviewStatusNeedsHuman,
        ContentItem.ImageReviewStatusNotApplicable,
        0,
        "agent_non_pass");
}

internal sealed class RecordingContentReviewExecutor(
    Func<ContentReviewExecutionRequest, CancellationToken, Task<ContentReviewExecutionResult>> handler)
    : IContentReviewExecutor
{
    private int _invocationCount;

    public string AgentCode => "reviewer-agent";
    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public Task<ContentReviewExecutionResult> ReviewAsync(
        ContentReviewExecutionRequest request,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);
        return handler(request, cancellationToken);
    }
}

internal sealed class RecordingContentPublishingPolicyResolver(
    Func<Guid, CancellationToken, Task<ContentPublishingPolicySnapshot>> handler)
    : IContentPublishingApprovalPolicyResolver
{
    private int _invocationCount;

    public int InvocationCount => Volatile.Read(ref _invocationCount);

    public Task<ContentPublishingPolicySnapshot> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _invocationCount);
        return handler(tenantId, cancellationToken);
    }
}

internal sealed class DbContentPublishingPolicyResolver(AppDbContext db)
    : IContentPublishingApprovalPolicyResolver
{
    public async Task<ContentPublishingPolicySnapshot> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleAsync(
            candidate => candidate.Id == tenantId,
            cancellationToken);
        return new ContentPublishingPolicySnapshot(
            tenant.ContentPublishingApprovalPolicy,
            tenant.ContentPublishingPolicyVersion);
    }
}

internal sealed class FailReviewTaskCompletionInterceptor : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ThrowForTaskCompletion(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowForTaskCompletion(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        ThrowForTaskCompletion(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowForTaskCompletion(command);
        return ValueTask.FromResult(result);
    }

    private static void ThrowForTaskCompletion(DbCommand command)
    {
        if (!command.CommandText.Contains(
                "content_review_tasks",
                StringComparison.OrdinalIgnoreCase)
            || !command.Parameters.Cast<DbParameter>().Any(parameter =>
                string.Equals(
                    parameter.Value?.ToString(),
                    ContentReviewTask.StatusCompleted,
                    StringComparison.Ordinal)))
        {
            return;
        }

        throw new DbUpdateConcurrencyException("review_task_completion_failed");
    }
}

internal sealed class FailReviewAuditInterceptor(string blockedAction) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        ThrowForBlockedAudit(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        ThrowForBlockedAudit(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        ThrowForBlockedAudit(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ThrowForBlockedAudit(command);
        return ValueTask.FromResult(result);
    }

    private void ThrowForBlockedAudit(DbCommand command)
    {
        if (!command.CommandText.Contains("audit_logs", StringComparison.OrdinalIgnoreCase))
            return;
        if (!command.Parameters.Cast<DbParameter>().Any(parameter =>
                string.Equals(parameter.Value?.ToString(), blockedAction, StringComparison.Ordinal)))
        {
            return;
        }

        throw new InvalidOperationException($"audit_insert_failed:{blockedAction}");
    }
}
