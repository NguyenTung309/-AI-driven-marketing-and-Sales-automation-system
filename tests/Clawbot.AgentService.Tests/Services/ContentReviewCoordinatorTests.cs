using System.Text.Json;
using System.Text.Json.Nodes;
using Clawbot.AgentService.Services;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Content;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ContentReviewCoordinatorTests
{
    private const string StartedAction = "content.agent_review.started";
    private const string CompletedAction = "content.agent_review.completed";
    private const string StaleAction = "content.agent_review.stale_result_discarded";
    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan AsyncTestTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task ProcessAsync_PersistsRunningBeforeCallingReviewer_WithoutHoldingTransaction()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (probe, request, cancellationToken) =>
            {
                request.TenantId.Should().Be(probe.Database.TenantId);
                request.ContentItemId.Should().Be(probe.ContentItemId);
                request.ExpectedRevision.Should().Be(1);
                request.Platform.Should().Be("facebook");
                request.Body.Should().Be("Nội dung cần duyệt");
                await using var fresh = probe.Database.CreateDbContext();
                var item = await fresh.ContentItems.SingleAsync(
                    candidate => candidate.Id == probe.ContentItemId,
                    cancellationToken);
                var task = await fresh.ContentReviewTasks.SingleAsync(
                    candidate => candidate.Id == probe.ReviewTaskId,
                    cancellationToken);

                item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
                item.AgentReviewStartedAt.Should().Be(ContentReviewCoordinatorHarness.Now);
                task.Status.Should().Be(ContentReviewTask.StatusLeased);
                task.LeaseToken.Should().NotBeNull();
                probe.CoordinatorDb.Database.CurrentTransaction.Should().BeNull();
                var startedAudit = await fresh.AuditLogs.SingleAsync(
                    audit => audit.Action == StartedAction,
                    cancellationToken);
                startedAudit.TenantId.Should().Be(probe.Database.TenantId);
                startedAudit.ResourceType.Should().Be("content_item");
                startedAudit.ResourceId.Should().Be(probe.ContentItemId);
                startedAudit.EventKey.Should().Be(EventKey(probe.ReviewTaskId, "started"));
                startedAudit.StateSequence.Should().Be(1);
                startedAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
                AssertAuditPayload(
                    startedAudit.DiffJson,
                    new
                    {
                        reviewTaskId = probe.ReviewTaskId,
                        expectedRevision = 1,
                        reviewerAgentId = probe.ReviewerAgentId,
                        reviewStatus = ContentItem.ReviewStatusRunning
                    });
                return ContentReviewResults.Passed;
            });

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        var completedItem = await verification.ContentItems.SingleAsync();
        completedItem.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPassed);
        completedItem.ReviewedByAgentId.Should().Be(harness.ReviewerAgentId);
        var completedAudit = await verification.AuditLogs.SingleAsync(
            audit => audit.Action == CompletedAction);
        completedAudit.TenantId.Should().Be(harness.TenantId);
        completedAudit.ResourceType.Should().Be("content_item");
        completedAudit.ResourceId.Should().Be(harness.ContentItemId);
        completedAudit.EventKey.Should().Be(EventKey(harness.ReviewTaskId, "completed"));
        completedAudit.StateSequence.Should().Be(2);
        completedAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        AssertAuditPayload(
            completedAudit.DiffJson,
            new
            {
                reviewTaskId = harness.ReviewTaskId,
                expectedRevision = 1,
                reviewerAgentId = harness.ReviewerAgentId,
                reviewStatus = ContentItem.ReviewStatusPassed,
                imageReviewStatus = ContentItem.ImageReviewStatusNotApplicable,
                reviewedImageCount = 0,
                reasonCode = "passed",
                publishingPolicy = Tenant.ContentPublishingPolicyHumanRequired,
                publishingPolicyVersion = 1L
            });
    }

    [Fact]
    public async Task ProcessAsync_DoesNotInvokeReviewerTwice_WhenSameLeaseIsDeliveredConcurrently()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (_, _, cancellationToken) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
                return ContentReviewResults.Passed;
            });

        var firstDelivery = harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);
        await entered.Task.WaitAsync(AsyncTestTimeout);

        await using var duplicateDb = harness.Database.CreateDbContext();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(ContentReviewCoordinatorHarness.Now);
        var duplicateCoordinator = new ContentReviewCoordinator(
            duplicateDb,
            harness.Executor,
            new DbContentPublishingPolicyResolver(duplicateDb),
            clock);

        try
        {
            await duplicateCoordinator.ProcessAsync(
                    harness.ReviewTaskId,
                    harness.LeaseToken)
                .WaitAsync(AsyncTestTimeout);
        }
        finally
        {
            release.TrySetResult();
        }

        await firstDelivery.WaitAsync(AsyncTestTimeout);
        harness.Executor.InvocationCount.Should().Be(1);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_NoOps_WhenCompletedTaskIsRedelivered()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var task = await harness.Database.Db.ContentReviewTasks.SingleAsync();
        task.Complete(harness.LeaseToken, ContentReviewCoordinatorHarness.Now);
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_CancelsTaskAndDiscardsResult_WhenRevisionChangesDuringReview()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (probe, _, cancellationToken) =>
            {
                await using var editing = probe.Database.CreateDbContext();
                var item = await editing.ContentItems.SingleAsync(
                    candidate => candidate.Id == probe.ContentItemId,
                    cancellationToken);
                item.ReviseBody("Nội dung đã sửa", ContentReviewCoordinatorHarness.Now.AddMinutes(1));
                await editing.SaveChangesAsync(cancellationToken);
                return ContentReviewResults.Passed;
            });

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var savedItem = await verification.ContentItems.SingleAsync();
        var savedTask = await verification.ContentReviewTasks.SingleAsync();
        savedItem.ContentRevision.Should().Be(2);
        savedItem.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPending);
        savedItem.AgentReviewedRevision.Should().BeNull();
        savedItem.ApprovedRevision.Should().BeNull();
        savedTask.Status.Should().Be(ContentReviewTask.StatusCanceledStale);
        var staleAudit = await verification.AuditLogs.SingleAsync(
            audit => audit.Action == StaleAction);
        staleAudit.EventKey.Should().Be(EventKey(harness.ReviewTaskId, "stale"));
        staleAudit.StateSequence.Should().Be(2);
        staleAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        AssertAuditPayload(
            staleAudit.DiffJson,
            new
            {
                reviewTaskId = harness.ReviewTaskId,
                expectedRevision = 1,
                currentRevision = 2,
                disposition = "stale_revision"
            });
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RejectsSameRevisionConcurrentWrite_WithoutPartialCompletion()
    {
        var desiredPublishAt = ContentReviewCoordinatorHarness.Now.AddDays(1);
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (probe, _, cancellationToken) =>
            {
                await using var editing = probe.Database.CreateDbContext();
                var item = await editing.ContentItems.SingleAsync(cancellationToken);
                item.SetDesiredPublishAt(
                    desiredPublishAt,
                    ContentReviewCoordinatorHarness.Now.AddMinutes(1));
                var rowVersion = editing.Entry(item).Property(candidate => candidate.RowVersion);
                rowVersion.CurrentValue = [1];
                rowVersion.IsModified = true;
                await editing.SaveChangesAsync(cancellationToken);
                return ContentReviewResults.Passed;
            });

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verification = harness.Database.CreateDbContext();
        var savedItem = await verification.ContentItems.SingleAsync();
        var savedTask = await verification.ContentReviewTasks.SingleAsync();
        savedItem.DesiredPublishAt.Should().Be(desiredPublishAt);
        savedItem.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        savedItem.AgentReviewedRevision.Should().BeNull();
        savedItem.PublishingPolicyApplied.Should().BeNull();
        savedTask.Status.Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_NoOps_WhenTaskBelongsToAnotherTenant()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var otherTenant = Tenant.Create(
            "other-tenant",
            "Other Tenant",
            "free",
            ContentReviewCoordinatorHarness.Now);
        var otherItem = ContentItem.Create(
            otherTenant.Id,
            "facebook",
            "Nội dung tenant khác",
            createdBy: null,
            ContentReviewCoordinatorHarness.Now);
        var otherTask = ContentReviewTask.CreatePending(
            otherTenant.Id,
            otherItem.Id,
            otherItem.ContentRevision,
            ContentReviewCoordinatorHarness.Now,
            ContentReviewCoordinatorHarness.Now);
        var otherLeaseToken = Guid.NewGuid();
        otherTask.Lease(
            otherLeaseToken,
            ContentReviewCoordinatorHarness.Now.AddHours(1),
            ContentReviewCoordinatorHarness.Now);
        harness.Database.Db.Tenants.Add(otherTenant);
        harness.Database.Db.ContentItems.Add(otherItem);
        harness.Database.Db.ContentReviewTasks.Add(otherTask);
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(otherTask.Id, otherLeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        var savedItem = await verification.ContentItems
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == otherItem.Id);
        savedItem.Status.Should().Be("draft");
        savedItem.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPending);
        savedItem.AgentReviewedRevision.Should().BeNull();
        savedItem.ReviewedByAgentId.Should().BeNull();
        savedItem.AgentReviewStartedAt.Should().BeNull();
        savedItem.AgentReviewedAt.Should().BeNull();
        savedItem.AgentReviewReason.Should().BeNull();
        savedItem.ImageReviewStatus.Should().Be(ContentItem.ImageReviewStatusPending);
        savedItem.ReviewedImageCount.Should().Be(0);
        savedItem.AgentReviewAttemptCount.Should().Be(0);
        savedItem.PublishingPolicyApplied.Should().BeNull();
        savedItem.PublishingPolicyVersionApplied.Should().BeNull();
        savedItem.HumanApprovalRequirementReason.Should().BeNull();
        savedItem.ApprovedRevision.Should().BeNull();
        savedItem.ApprovalMode.Should().BeNull();
        savedItem.ApprovalReason.Should().BeNull();
        savedItem.ApprovedBy.Should().BeNull();
        savedItem.ApprovedByAgentId.Should().BeNull();
        savedItem.ApprovedAt.Should().BeNull();
        savedItem.UpdatedAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        var savedTask = await verification.ContentReviewTasks
            .IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Id == otherTask.Id);
        savedTask.Status.Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_NoOpsBeforeReviewer_WhenLeaseTokenIsWrong()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            Guid.NewGuid());

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPending);
        (await verification.ContentReviewTasks.SingleAsync()).LeaseToken
            .Should().Be(harness.LeaseToken);
    }

    [Fact]
    public async Task ProcessAsync_JointlyMatchesTaskIdAndLeaseToken()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var secondItem = ContentItem.Create(
            harness.TenantId,
            "facebook",
            "Nội dung thứ hai",
            createdBy: null,
            ContentReviewCoordinatorHarness.Now,
            createdByAgentId: Guid.NewGuid());
        var secondTask = ContentReviewTask.CreatePending(
            harness.TenantId,
            secondItem.Id,
            secondItem.ContentRevision,
            ContentReviewCoordinatorHarness.Now,
            ContentReviewCoordinatorHarness.Now);
        var secondLeaseToken = Guid.NewGuid();
        secondTask.Lease(
            secondLeaseToken,
            ContentReviewCoordinatorHarness.Now.AddHours(1),
            ContentReviewCoordinatorHarness.Now);
        harness.Database.Db.ContentItems.Add(secondItem);
        harness.Database.Db.ContentReviewTasks.Add(secondTask);
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            secondLeaseToken);
        await harness.Coordinator.ProcessAsync(
            Guid.NewGuid(),
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentItems.CountAsync(
                item => item.AgentReviewStatus == ContentItem.ReviewStatusPending))
            .Should().Be(2);
        (await verification.ContentReviewTasks.CountAsync(
                task => task.Status == ContentReviewTask.StatusLeased))
            .Should().Be(2);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_FailsTask_WhenItemCannotStartAnotherReviewAttempt()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var item = await harness.Database.Db.ContentItems.SingleAsync();
        for (var attempt = 0; attempt < ContentItem.MaxAgentReviewAttempts; attempt++)
        {
            item.BeginAgentReview(
                item.ContentRevision,
                ContentReviewCoordinatorHarness.Now.AddMinutes(attempt + 1));
            item.RecordAgentReview(
                item.ContentRevision,
                ContentItem.ReviewStatusPassed,
                ContentItem.ImageReviewStatusNotApplicable,
                reviewedImageCount: 0,
                harness.ReviewerAgentId,
                reason: "passed",
                ContentReviewCoordinatorHarness.Now.AddMinutes(attempt + 1));
        }
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        var savedTask = await verification.ContentReviewTasks.SingleAsync();
        savedTask.Status.Should().Be(ContentReviewTask.StatusFailed);
        savedTask.LastErrorCode.Should().Be("content_review_attempt_limit_reached");
        savedTask.LeaseToken.Should().BeNull();
        savedTask.ClaimedLeaseToken.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_NoOpsBeforeReviewer_WhenLeaseIsExpired()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            processingAt: ContentReviewCoordinatorHarness.Now.AddMinutes(2),
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now.AddMinutes(1));

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPending);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
    }

    [Fact]
    public async Task ProcessAsync_TreatsLeaseAsExpired_AtExactExpiryBoundary()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            processingAt: ContentReviewCoordinatorHarness.Now,
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now,
            leaseStartedAt: ContentReviewCoordinatorHarness.Now.AddMinutes(-1));

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPending);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_DiscardsResult_WhenLeaseExpiresDuringReview()
    {
        var clock = new SequencedClock(
            ContentReviewCoordinatorHarness.Now,
            ContentReviewCoordinatorHarness.Now.AddMinutes(2));
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now.AddMinutes(1),
            coordinatorClock: clock);

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(harness.LeaseToken);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_DiscardsResult_WhenLeaseExpiresWhilePolicyIsResolved()
    {
        var clock = new SequencedClock(
            ContentReviewCoordinatorHarness.Now,
            ContentReviewCoordinatorHarness.Now.AddSeconds(30),
            ContentReviewCoordinatorHarness.Now.AddMinutes(2));
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now.AddMinutes(1),
            coordinatorClock: clock);

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        item.PublishingPolicyApplied.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(harness.LeaseToken);
        harness.PolicyResolver.InvocationCount.Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_DiscardsResult_WhenLeaseIsReclaimedDuringReview()
    {
        var replacementToken = Guid.NewGuid();
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now.AddMinutes(1),
            reviewHandler: async (probe, _, cancellationToken) =>
            {
                await using var reclaiming = probe.Database.CreateDbContext();
                var task = await reclaiming.ContentReviewTasks.SingleAsync(cancellationToken);
                task.ReclaimExpiredLease(
                    replacementToken,
                    ContentReviewCoordinatorHarness.Now.AddHours(1),
                    ContentReviewCoordinatorHarness.Now.AddMinutes(2));
                await reclaiming.SaveChangesAsync(cancellationToken);
                return ContentReviewResults.Passed;
            });

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.LeaseToken.Should().Be(replacementToken);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessAsync_OldOwnerCannotOverwriteCompletedReplacement(
        bool oldReviewerThrows)
    {
        var oldReviewerEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOldReviewer = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerInvocation = 0;
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now.AddMinutes(1),
            reviewHandler: async (_, _, cancellationToken) =>
            {
                if (Interlocked.Increment(ref handlerInvocation) != 1)
                    return ContentReviewResults.NeedsHuman;

                oldReviewerEntered.TrySetResult();
                await releaseOldReviewer.Task.WaitAsync(cancellationToken);
                if (oldReviewerThrows)
                {
                    throw new InvalidOperationException(
                        "Bearer old-owner-secret; customer=Nội dung cần duyệt");
                }

                return ContentReviewResults.Passed;
            });

        var oldOwner = harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);
        await oldReviewerEntered.Task.WaitAsync(AsyncTestTimeout);

        var replacementToken = Guid.NewGuid();
        await using (var reclaiming = harness.Database.CreateDbContext())
        {
            var reclaimedTask = await reclaiming.ContentReviewTasks.SingleAsync();
            reclaimedTask.ReclaimExpiredLease(
                replacementToken,
                ContentReviewCoordinatorHarness.Now.AddHours(1),
                ContentReviewCoordinatorHarness.Now.AddMinutes(2));
            await reclaiming.SaveChangesAsync();
        }

        await using (var replacementDb = harness.Database.CreateDbContext())
        {
            var replacementClock = Substitute.For<IClock>();
            replacementClock.UtcNow.Returns(
                ContentReviewCoordinatorHarness.Now.AddMinutes(2));
            var replacementCoordinator = new ContentReviewCoordinator(
                replacementDb,
                harness.Executor,
                new DbContentPublishingPolicyResolver(replacementDb),
                replacementClock);
            await replacementCoordinator.ProcessAsync(
                    harness.ReviewTaskId,
                    replacementToken)
                .WaitAsync(AsyncTestTimeout);
        }

        releaseOldReviewer.TrySetResult();
        await oldOwner.WaitAsync(AsyncTestTimeout);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusNeedsHuman);
        item.AgentReviewedRevision.Should().Be(item.ContentRevision);
        item.AgentReviewReason.Should().Be("agent_non_pass");
        item.PublishingPolicyApplied.Should().Be(Tenant.ContentPublishingPolicyHumanRequired);
        item.PublishingPolicyVersionApplied.Should().Be(1);
        task.Status.Should().Be(ContentReviewTask.StatusCompleted);
        task.LeaseToken.Should().BeNull();
        harness.Executor.InvocationCount.Should().Be(2);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(1);
        var auditPayloads = string.Join(
            '|',
            await verification.AuditLogs.Select(audit => audit.DiffJson).ToListAsync());
        auditPayloads.Should().NotContain("old-owner-secret");
        auditPayloads.Should().NotContain("Nội dung cần duyệt");
    }

    [Fact]
    public async Task ProcessAsync_CancelsAlreadyStaleTask_WithoutCallingReviewer()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var item = await harness.Database.Db.ContentItems.SingleAsync();
        item.ReviseBody(
            "Nội dung đã sửa trước khi lease chạy",
            ContentReviewCoordinatorHarness.Now.AddMinutes(1));
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCanceledStale);
        var staleAudit = await verification.AuditLogs.SingleAsync(
            audit => audit.Action == StaleAction);
        staleAudit.EventKey.Should().Be(EventKey(harness.ReviewTaskId, "stale"));
        staleAudit.StateSequence.Should().Be(1);
        staleAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        AssertAuditPayload(
            staleAudit.DiffJson,
            new
            {
                reviewTaskId = harness.ReviewTaskId,
                expectedRevision = 1,
                currentRevision = 2,
                disposition = "stale_revision"
            });
    }

    [Theory]
    [InlineData("wrong_token")]
    [InlineData("expired")]
    [InlineData("reclaimed")]
    public async Task ProcessAsync_DoesNotCancelStaleTask_WhenLeaseIsNotAuthoritative(
        string leaseScenario)
    {
        var shortLease = leaseScenario is "expired" or "reclaimed";
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            processingAt: leaseScenario == "expired"
                ? ContentReviewCoordinatorHarness.Now.AddMinutes(2)
                : ContentReviewCoordinatorHarness.Now,
            leaseExpiresAt: shortLease
                ? ContentReviewCoordinatorHarness.Now.AddMinutes(1)
                : ContentReviewCoordinatorHarness.Now.AddHours(1));
        var item = await harness.Database.Db.ContentItems.SingleAsync();
        item.ReviseBody(
            "Nội dung stale nhưng lease không hợp lệ",
            ContentReviewCoordinatorHarness.Now.AddMinutes(1));
        await harness.Database.Db.SaveChangesAsync();

        var suppliedToken = harness.LeaseToken;
        Guid? replacementToken = null;
        if (leaseScenario == "wrong_token")
        {
            suppliedToken = Guid.NewGuid();
        }
        else if (leaseScenario == "reclaimed")
        {
            replacementToken = Guid.NewGuid();
            var task = await harness.Database.Db.ContentReviewTasks.SingleAsync();
            task.ReclaimExpiredLease(
                replacementToken.Value,
                ContentReviewCoordinatorHarness.Now.AddHours(1),
                ContentReviewCoordinatorHarness.Now.AddMinutes(2));
            await harness.Database.Db.SaveChangesAsync();
        }

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            suppliedToken);

        harness.Executor.InvocationCount.Should().Be(0);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        var savedItem = await verification.ContentItems.SingleAsync();
        var savedTask = await verification.ContentReviewTasks.SingleAsync();
        savedItem.ContentRevision.Should().Be(2);
        savedItem.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPending);
        savedTask.Status.Should().Be(ContentReviewTask.StatusLeased);
        if (replacementToken.HasValue)
            savedTask.LeaseToken.Should().Be(replacementToken.Value);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_FallsBackWithoutCallingReviewer_WhenReviewerMatchesGenerator()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewerIsGenerator: true);

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusNeedsHuman);
        item.ReviewedByAgentId.Should().BeNull();
        item.AgentReviewReason.Should().Be(ContentItem.ReviewReasonReviewerIndependence);
        item.HumanApprovalRequirementReason.Should().Be(ContentItem.HumanApprovalReasonAgentNonPass);
        item.PublishingPolicyApplied.Should().Be(Tenant.ContentPublishingPolicyHumanRequired);
        item.PublishingPolicyVersionApplied.Should().Be(1);
        await AssertNoPublishingSideEffectsAsync(verification, item);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        var completedAudit = await verification.AuditLogs.SingleAsync(
            audit => audit.Action == CompletedAction);
        completedAudit.EventKey.Should().Be(EventKey(harness.ReviewTaskId, "completed"));
        completedAudit.StateSequence.Should().Be(1);
        completedAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        AssertAuditPayload(
            completedAudit.DiffJson,
            CompletedAuditPayload(
                harness,
                reviewerAgentId: null,
                ContentItem.ReviewStatusNeedsHuman,
                ContentItem.ImageReviewStatusNotApplicable,
                ContentItem.ReviewReasonReviewerIndependence));
    }

    [Fact]
    public async Task ProcessAsync_FallsBackWithoutCallingReviewer_WhenOnlyForeignReviewerExists()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var reviewer = await harness.Database.Db.AgentDefinitions.SingleAsync(
            candidate => candidate.Id == harness.ReviewerAgentId);
        harness.Database.Db.AgentDefinitions.Remove(reviewer);
        var foreignTenant = Tenant.Create(
            "foreign-reviewer-tenant",
            "Foreign Reviewer Tenant",
            "free",
            ContentReviewCoordinatorHarness.Now);
        var foreignReviewer = AgentDefinition.Create(
            foreignTenant.Id,
            "reviewer-agent",
            "Foreign Reviewer",
            "reviewer",
            "Must never review another tenant",
            ContentReviewCoordinatorHarness.Now);
        harness.Database.Db.Tenants.Add(foreignTenant);
        harness.Database.Db.AgentDefinitions.Add(foreignReviewer);
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusNeedsHuman);
        item.AgentReviewedRevision.Should().Be(item.ContentRevision);
        item.ReviewedByAgentId.Should().BeNull();
        item.AgentReviewReason.Should().Be(ContentItem.ReviewReasonReviewerUnavailable);
        item.HumanApprovalRequirementReason.Should().Be(ContentItem.HumanApprovalReasonAgentNonPass);
        item.PublishingPolicyApplied.Should().Be(Tenant.ContentPublishingPolicyHumanRequired);
        item.PublishingPolicyVersionApplied.Should().Be(1);
        await AssertNoPublishingSideEffectsAsync(verification, item);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        var completedAudit = await verification.AuditLogs.SingleAsync(
            audit => audit.Action == CompletedAction);
        completedAudit.EventKey.Should().Be(EventKey(harness.ReviewTaskId, "completed"));
        completedAudit.StateSequence.Should().Be(1);
        completedAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        AssertAuditPayload(
            completedAudit.DiffJson,
            CompletedAuditPayload(
                harness,
                reviewerAgentId: null,
                ContentItem.ReviewStatusNeedsHuman,
                ContentItem.ImageReviewStatusNotApplicable,
                ContentItem.ReviewReasonReviewerUnavailable));
    }

    [Fact]
    public async Task ProcessAsync_DoesNotCommitFallback_WhenLeaseExpiresDuringPolicyResolution()
    {
        var currentTime = ContentReviewCoordinatorHarness.Now;
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => currentTime);
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            leaseExpiresAt: ContentReviewCoordinatorHarness.Now.AddMinutes(1),
            coordinatorClock: clock,
            policyHandler: (_, _, _) =>
            {
                currentTime = ContentReviewCoordinatorHarness.Now.AddMinutes(2);
                return Task.FromResult(new ContentPublishingPolicySnapshot(
                    Tenant.ContentPublishingPolicyHumanRequired,
                    1));
            });
        var reviewer = await harness.Database.Db.AgentDefinitions.SingleAsync(
            candidate => candidate.Id == harness.ReviewerAgentId);
        harness.Database.Db.AgentDefinitions.Remove(reviewer);
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(0);
        harness.PolicyResolver.InvocationCount.Should().Be(1);
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPending);
        item.PublishingPolicyApplied.Should().BeNull();
        var task = await verification.ContentReviewTasks.SingleAsync();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        task.ClaimedLeaseToken.Should().BeNull();
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_UsesTenantReviewer_WhenForeignReviewerSharesCode()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync();
        var localReviewer = await harness.Database.Db.AgentDefinitions.SingleAsync(
            candidate => candidate.Id == harness.ReviewerAgentId);
        harness.Database.Db.AgentDefinitions.Remove(localReviewer);
        await harness.Database.Db.SaveChangesAsync();

        var foreignTenant = Tenant.Create(
            "foreign-reviewer-collision-tenant",
            "Foreign Reviewer Collision Tenant",
            "free",
            ContentReviewCoordinatorHarness.Now);
        var foreignReviewer = AgentDefinition.Create(
            foreignTenant.Id,
            "reviewer-agent",
            "Foreign Reviewer",
            "reviewer",
            "Must never review another tenant",
            ContentReviewCoordinatorHarness.Now);
        harness.Database.Db.Tenants.Add(foreignTenant);
        harness.Database.Db.AgentDefinitions.Add(foreignReviewer);
        await harness.Database.Db.SaveChangesAsync();
        harness.Database.Db.AgentDefinitions.Add(localReviewer);
        await harness.Database.Db.SaveChangesAsync();

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.Executor.InvocationCount.Should().Be(1);
        await using var verification = harness.Database.CreateDbContext();
        var savedItem = await verification.ContentItems.SingleAsync();
        savedItem.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPassed);
        savedItem.ReviewedByAgentId.Should().Be(harness.ReviewerAgentId);
        savedItem.ReviewedByAgentId.Should().NotBe(foreignReviewer.Id);
        var audit = await verification.AuditLogs.SingleAsync(
            candidate => candidate.Action == CompletedAction);
        AssertAuditPayload(
            audit.DiffJson,
            CompletedAuditPayload(
                harness,
                harness.ReviewerAgentId,
                ContentItem.ReviewStatusPassed,
                ContentItem.ImageReviewStatusNotApplicable,
                "passed"));
    }

    [Theory]
    [InlineData(Tenant.ContentPublishingPolicyAutomatic, ContentItem.ReviewStatusRejected, "agent_non_pass")]
    [InlineData(Tenant.ContentPublishingPolicyAutomatic, ContentItem.ReviewStatusNeedsHuman, "agent_non_pass")]
    [InlineData(Tenant.ContentPublishingPolicyAutomatic, ContentItem.ReviewStatusFailed, "agent_non_pass")]
    [InlineData(Tenant.ContentPublishingPolicyHumanRequired, ContentItem.ReviewStatusPassed, "tenant_policy")]
    public async Task ProcessAsync_AppliesPolicySnapshotAfterReview_LeavesHumanQueue(
        string policy,
        string reviewStatus,
        string? expectedHumanRequirement)
    {
        var reasonCode = reviewStatus switch
        {
            ContentItem.ReviewStatusPassed => "passed",
            ContentItem.ReviewStatusFailed => "reviewer_error",
            _ => "agent_non_pass"
        };
        var result = new ContentReviewExecutionResult(
            reviewStatus,
            ContentItem.ImageReviewStatusNotApplicable,
            0,
            reasonCode);
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            policy,
            reviewHandler: (_, _, _) => Task.FromResult(result));

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var tenant = await verification.Tenants.SingleAsync();
        var item = await verification.ContentItems.SingleAsync();
        item.PublishingPolicyApplied.Should().Be(tenant.ContentPublishingApprovalPolicy);
        item.PublishingPolicyVersionApplied.Should().Be(tenant.ContentPublishingPolicyVersion);
        item.HumanApprovalRequirementReason.Should().Be(expectedHumanRequirement);
        item.Status.Should().Be("draft");
        item.ApprovedRevision.Should().BeNull();
        item.ApprovalMode.Should().BeNull();
        item.ApprovalReason.Should().BeNull();
        item.ApprovedBy.Should().BeNull();
        item.ApprovedByAgentId.Should().BeNull();
        item.ApprovedAt.Should().BeNull();
        (await verification.ContentSchedules.CountAsync()).Should().Be(0);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        var completedAudit = await verification.AuditLogs.SingleAsync(
            audit => audit.Action == CompletedAction);
        AssertAuditPayload(
            completedAudit.DiffJson,
            CompletedAuditPayload(
                harness,
                harness.ReviewerAgentId,
                reviewStatus,
                ContentItem.ImageReviewStatusNotApplicable,
                reasonCode,
                tenant.ContentPublishingApprovalPolicy,
                tenant.ContentPublishingPolicyVersion));
    }

    [Fact]
    public async Task ProcessAsync_AutomaticPolicyAndPassed_ApprovesAndCreatesScheduleIntent()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            Tenant.ContentPublishingPolicyAutomatic,
            reviewHandler: (_, _, _) => Task.FromResult(ContentReviewResults.Passed));

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var tenant = await verification.Tenants.SingleAsync();
        var item = await verification.ContentItems.SingleAsync();
        item.PublishingPolicyApplied.Should().Be(Tenant.ContentPublishingPolicyAutomatic);
        item.PublishingPolicyVersionApplied.Should().Be(tenant.ContentPublishingPolicyVersion);
        item.HumanApprovalRequirementReason.Should().BeNull();
        item.Status.Should().Be("scheduled");
        item.ApprovedRevision.Should().Be(item.ContentRevision);
        item.ApprovalMode.Should().Be(ContentItem.ApprovalModeAutomatic);
        item.DesiredPublishAt.Should().NotBeNull();
        var schedule = await verification.ContentSchedules.SingleAsync();
        schedule.ContentRevision.Should().Be(item.ContentRevision);
        schedule.ScheduledAt.Should().Be(item.DesiredPublishAt);
        // Facebook without Meta page → held at golden time (target missing).
        schedule.Status.Should().Be(ContentSchedule.StatusHeld);
        schedule.LastErrorCode.Should().Be(ContentAutoScheduler.ErrorAutoScheduleTargetMissing);
        schedule.ApprovalMode.Should().Be(ContentItem.ApprovalModeAutomatic);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
    }

    [Fact]
    public async Task ProcessAsync_ResolvesPolicyInsideCompletionTransaction_AfterReview()
    {
        var hasReviewerReturned = 0;
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: (_, _, _) =>
            {
                Interlocked.Exchange(ref hasReviewerReturned, 1);
                return Task.FromResult(ContentReviewResults.Passed);
            },
            policyHandler: async (probe, tenantId, cancellationToken) =>
            {
                Volatile.Read(ref hasReviewerReturned).Should().Be(1);
                probe.CoordinatorDb.Database.CurrentTransaction.Should().NotBeNull();
                var tenant = await probe.CoordinatorDb.Tenants.SingleAsync(
                    candidate => candidate.Id == tenantId,
                    cancellationToken);
                return new ContentPublishingPolicySnapshot(
                    tenant.ContentPublishingApprovalPolicy,
                    tenant.ContentPublishingPolicyVersion);
            });

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        harness.PolicyResolver.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_UsesPolicyCommittedWhileExternalReviewRuns()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (probe, _, cancellationToken) =>
            {
                await using var policyUpdate = probe.Database.CreateDbContext();
                var tenant = await policyUpdate.Tenants.SingleAsync(cancellationToken);
                tenant.SetContentPublishingApprovalPolicy(
                    Tenant.ContentPublishingPolicyAutomatic,
                    ContentReviewCoordinatorHarness.Now.AddMinutes(1));
                await policyUpdate.SaveChangesAsync(cancellationToken);
                return ContentReviewResults.Passed;
            });

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.PublishingPolicyApplied.Should().Be(Tenant.ContentPublishingPolicyAutomatic);
        item.PublishingPolicyVersionApplied.Should().Be(2);
        item.HumanApprovalRequirementReason.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_PersistsOnlyMachineSafeFailure_WhenReviewerThrows()
    {
        const string sensitiveError =
            "Bearer fake-secret-token; customer=Nội dung cần duyệt; prompt=ignore policy";
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: (_, _, _) => throw new InvalidOperationException(sensitiveError));

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        var audits = await verification.AuditLogs.ToListAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusFailed);
        item.AgentReviewReason.Should().Be("reviewer_error");
        item.HumanApprovalRequirementReason.Should().Be(ContentItem.HumanApprovalReasonAgentNonPass);
        await AssertNoPublishingSideEffectsAsync(verification, item);
        task.Status.Should().Be(ContentReviewTask.StatusCompleted);
        var completedAudit = audits.Single(audit => audit.Action == CompletedAction);
        completedAudit.EventKey.Should().Be(EventKey(harness.ReviewTaskId, "completed"));
        completedAudit.StateSequence.Should().Be(2);
        completedAudit.OccurredAt.Should().Be(ContentReviewCoordinatorHarness.Now);
        AssertAuditPayload(
            completedAudit.DiffJson,
            CompletedAuditPayload(
                harness,
                harness.ReviewerAgentId,
                ContentItem.ReviewStatusFailed,
                ContentItem.ImageReviewStatusNotApplicable,
                "reviewer_error"));
        foreach (var persistedValue in new[]
                 {
                     item.AgentReviewReason,
                     task.LastErrorCode,
                     string.Join('|', audits.Select(audit => audit.DiffJson))
                 })
        {
            (persistedValue ?? string.Empty).Should().NotContain(sensitiveError);
            (persistedValue ?? string.Empty).Should().NotContain("fake-secret-token");
            (persistedValue ?? string.Empty).Should().NotContain("Nội dung cần duyệt");
            (persistedValue ?? string.Empty).Should().NotContain("ignore policy");
        }
    }

    [Fact]
    public async Task ProcessAsync_PropagatesCallerCancellation_WithoutCompletingReview()
    {
        using var cancellation = new CancellationTokenSource();
        var receivedToken = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (_, _, forwardedToken) =>
            {
                receivedToken.TrySetResult(forwardedToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, forwardedToken);
                return ContentReviewResults.Passed;
            });

        var processing = harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken,
            cancellation.Token);
        var firstCompleted = await Task.WhenAny(processing, receivedToken.Task)
            .WaitAsync(AsyncTestTimeout);
        if (firstCompleted == processing)
            await processing;
        var forwardedToken = await receivedToken.Task.WaitAsync(AsyncTestTimeout);
        forwardedToken.Should().Be(cancellation.Token);
        cancellation.Cancel();

        var act = async () => await processing;
        await act.Should().ThrowAsync<OperationCanceledException>();
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        harness.PolicyResolver.InvocationCount.Should().Be(0);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_TreatsProviderCancellationAsReviewerFailure()
    {
        using var providerCancellation = new CancellationTokenSource();
        providerCancellation.Cancel();
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: (_, _, _) =>
                Task.FromCanceled<ContentReviewExecutionResult>(providerCancellation.Token));

        await harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken,
            CancellationToken.None);

        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusFailed);
        item.AgentReviewReason.Should().Be("reviewer_error");
        item.HumanApprovalRequirementReason.Should().Be(ContentItem.HumanApprovalReasonAgentNonPass);
        await AssertNoPublishingSideEffectsAsync(verification, item);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusCompleted);
        harness.PolicyResolver.InvocationCount.Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(1);
    }

    public static IEnumerable<object[]> UnsafeReviewResults()
    {
        var statuses = new[]
        {
            ContentItem.ReviewStatusPassed,
            ContentItem.ReviewStatusRejected,
            ContentItem.ReviewStatusNeedsHuman,
            ContentItem.ReviewStatusFailed
        };
        var reasonCodes = new[]
        {
            "unknown_reason",
            "sk_live_abc123",
            "badcode",
            new string('a', 129),
            "Bearer fake-secret-token; customer body",
            "passed_Bearer_fake-secret",
            "xpassed",
            "agent_non_pass_customer-body",
            "reviewer_errorsecret"
        };

        foreach (var status in statuses)
            foreach (var reasonCode in reasonCodes)
                yield return [status, reasonCode];
    }

    [Theory]
    [MemberData(nameof(UnsafeReviewResults))]
    public void Constructor_RejectsUnsafeProviderDerivedReasonCode(
        string reviewStatus,
        string reasonCode)
    {
        var act = () => new ContentReviewExecutionResult(
            reviewStatus,
            ContentItem.ImageReviewStatusNotApplicable,
            0,
            reasonCode);

        act.Should().Throw<ArgumentException>()
            .WithMessage("content_review_reason_code_invalid*");
    }

    [Fact]
    public async Task ProcessAsync_RollsBackRunningTransition_WhenStartedAuditInsertFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            interceptors: new FailReviewAuditInterceptor(StartedAction));

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"audit_insert_failed:{StartedAction}");
        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPending);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RollsBackStaleCancellation_WhenStaleAuditInsertFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            interceptors: new FailReviewAuditInterceptor(StaleAction));
        var item = await harness.Database.Db.ContentItems.SingleAsync();
        item.ReviseBody(
            "Nội dung đã sửa trước khi lease chạy",
            ContentReviewCoordinatorHarness.Now.AddMinutes(1));
        await harness.Database.Db.SaveChangesAsync();

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"audit_insert_failed:{StaleAction}");
        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RollsBackStaleAfterReview_WhenStaleAuditInsertFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: async (probe, _, cancellationToken) =>
            {
                await using var editing = probe.Database.CreateDbContext();
                var item = await editing.ContentItems.SingleAsync(cancellationToken);
                item.ReviseBody(
                    "Nội dung đổi trong review",
                    ContentReviewCoordinatorHarness.Now.AddMinutes(1));
                await editing.SaveChangesAsync(cancellationToken);
                return ContentReviewResults.Passed;
            },
            interceptors: new FailReviewAuditInterceptor(StaleAction));

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"audit_insert_failed:{StaleAction}");
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.ContentRevision.Should().Be(2);
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusPending);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StaleAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RollsBackIndependenceFallback_WhenCompletedAuditInsertFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewerIsGenerator: true,
            interceptors: new FailReviewAuditInterceptor(CompletedAction));

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"audit_insert_failed:{CompletedAction}");
        harness.Executor.InvocationCount.Should().Be(0);
        await using var verification = harness.Database.CreateDbContext();
        (await verification.ContentItems.SingleAsync()).AgentReviewStatus
            .Should().Be(ContentItem.ReviewStatusPending);
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RollsBackProviderFailure_WhenCompletedAuditInsertFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            reviewHandler: (_, _, _) => throw new InvalidOperationException("raw provider failure"),
            interceptors: new FailReviewAuditInterceptor(CompletedAction));

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"audit_insert_failed:{CompletedAction}");
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        (await verification.ContentReviewTasks.SingleAsync()).Status
            .Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RollsBackCompletionTransition_WhenCompletedAuditInsertFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            interceptors: new FailReviewAuditInterceptor(CompletedAction));

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"audit_insert_failed:{CompletedAction}");
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        item.PublishingPolicyApplied.Should().BeNull();
        item.HumanApprovalRequirementReason.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_RollsBackItemAndAudit_WhenTaskCompletionFails()
    {
        await using var harness = await ContentReviewCoordinatorHarness.CreateAsync(
            interceptors: new FailReviewTaskCompletionInterceptor());

        var act = () => harness.Coordinator.ProcessAsync(
            harness.ReviewTaskId,
            harness.LeaseToken);

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>()
            .WithMessage("review_task_completion_failed");
        await using var verification = harness.Database.CreateDbContext();
        var item = await verification.ContentItems.SingleAsync();
        var task = await verification.ContentReviewTasks.SingleAsync();
        item.AgentReviewStatus.Should().Be(ContentItem.ReviewStatusRunning);
        item.AgentReviewedRevision.Should().BeNull();
        item.PublishingPolicyApplied.Should().BeNull();
        item.PublishingPolicyVersionApplied.Should().BeNull();
        item.ApprovedRevision.Should().BeNull();
        task.Status.Should().Be(ContentReviewTask.StatusLeased);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == StartedAction))
            .Should().Be(1);
        (await verification.AuditLogs.CountAsync(audit => audit.Action == CompletedAction))
            .Should().Be(0);
    }

    private static async Task AssertNoPublishingSideEffectsAsync(
        DbContext db,
        ContentItem item)
    {
        item.Status.Should().Be("draft");
        item.ApprovedRevision.Should().BeNull();
        item.ApprovalMode.Should().BeNull();
        item.ApprovalReason.Should().BeNull();
        item.ApprovedBy.Should().BeNull();
        item.ApprovedByAgentId.Should().BeNull();
        item.ApprovedAt.Should().BeNull();
        (await db.Set<ContentSchedule>().CountAsync()).Should().Be(0);
    }

    private static object CompletedAuditPayload(
        ContentReviewCoordinatorHarness harness,
        Guid? reviewerAgentId,
        string reviewStatus,
        string imageReviewStatus,
        string reasonCode,
        string publishingPolicy = Tenant.ContentPublishingPolicyHumanRequired,
        long publishingPolicyVersion = 1L) =>
        new
        {
            reviewTaskId = harness.ReviewTaskId,
            expectedRevision = 1,
            reviewerAgentId,
            reviewStatus,
            imageReviewStatus,
            reviewedImageCount = 0,
            reasonCode,
            publishingPolicy,
            publishingPolicyVersion
        };

    private static void AssertAuditPayload(string? actualJson, object expected)
    {
        actualJson.Should().NotBeNullOrWhiteSpace();
        var actualNode = JsonNode.Parse(actualJson!);
        var expectedNode = JsonSerializer.SerializeToNode(expected, AuditJsonOptions);
        JsonNode.DeepEquals(actualNode, expectedNode).Should().BeTrue(
            "the business audit payload must contain only the approved machine-safe fields");
    }

    private static string EventKey(Guid taskId, string transition) =>
        $"content-review:{taskId:N}:{transition}";
}
