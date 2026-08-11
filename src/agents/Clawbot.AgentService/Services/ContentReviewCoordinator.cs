using System.Runtime.ExceptionServices;
using System.Text.Json;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Domain.Security;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

public sealed record ContentReviewExecutionRequest(
    Guid TenantId,
    Guid ContentItemId,
    int ExpectedRevision,
    string Platform,
    string Body,
    // Refine (P6, §4.7): ảnh chụp L1/L2 của item để chạy lại L3+L4 khi reviewer reject. null = bài single-shot
    // (chain tắt / chưa lưu) => bỏ qua refine, về hàng chờ người. BriefId để gán trace refine đúng bài.
    Guid? BriefId = null,
    string? ChainPlanJson = null,
    string? ChainOutlineJson = null);

public sealed record ContentReviewExecutionResult
{
    public string ReviewStatus { get; }
    public string ImageReviewStatus { get; }
    public int ReviewedImageCount { get; }
    public string ReasonCode { get; }
    // Lý do reject tự do (tiếng Việt) từ reviewer — refine (P6, §4.7) bơm vào L3 làm góp ý cần khắc phục.
    // ReasonCode là mã cố định (passed/reviewer_error/agent_non_pass); Reason là văn bản người/LLM đọc được.
    public string? Reason { get; }

    public ContentReviewExecutionResult(
        string reviewStatus,
        string imageReviewStatus,
        int reviewedImageCount,
        string? reasonCode,
        string? reason = null)
    {
        if (reviewStatus is not (
            ContentItem.ReviewStatusPassed
            or ContentItem.ReviewStatusRejected
            or ContentItem.ReviewStatusNeedsHuman
            or ContentItem.ReviewStatusFailed))
        {
            throw new ArgumentException(
                "content_review_status_invalid",
                nameof(reviewStatus));
        }

        if (imageReviewStatus is not (
            ContentItem.ImageReviewStatusReviewed
            or ContentItem.ImageReviewStatusNotApplicable
            or ContentItem.ImageReviewStatusSkippedUnsupported
            or ContentItem.ImageReviewStatusFailed))
        {
            throw new ArgumentException(
                "content_image_review_status_invalid",
                nameof(imageReviewStatus));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(reviewedImageCount);
        if (imageReviewStatus == ContentItem.ImageReviewStatusReviewed
            ? reviewedImageCount == 0
            : reviewedImageCount != 0)
        {
            throw new ArgumentException(
                "content_reviewed_image_count_invalid",
                nameof(reviewedImageCount));
        }

        var expectedReasonCode = reviewStatus switch
        {
            ContentItem.ReviewStatusPassed => "passed",
            ContentItem.ReviewStatusFailed => "reviewer_error",
            _ => "agent_non_pass"
        };
        if (!string.Equals(reasonCode, expectedReasonCode, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "content_review_reason_code_invalid",
                nameof(reasonCode));
        }

        ReviewStatus = reviewStatus;
        ImageReviewStatus = imageReviewStatus;
        ReviewedImageCount = reviewedImageCount;
        ReasonCode = expectedReasonCode;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }
}

public interface IContentReviewExecutor
{
    string AgentCode { get; }

    Task<ContentReviewExecutionResult> ReviewAsync(
        ContentReviewExecutionRequest request,
        CancellationToken cancellationToken);
}

public sealed record ContentPublishingPolicySnapshot(
    string Value,
    long Version);

public interface IContentPublishingApprovalPolicyResolver
{
    Task<ContentPublishingPolicySnapshot> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken);
}

public sealed class LockedContentPublishingApprovalPolicyResolver(AppDbContext db)
    : IContentPublishingApprovalPolicyResolver
{
    public async Task<ContentPublishingPolicySnapshot> ResolveAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant_id_required", nameof(tenantId));
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "content_publishing_policy_transaction_required");

        var tenant = db.Database.IsSqlServer()
            ? await db.Tenants
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM dbo.tenants WITH (UPDLOCK, HOLDLOCK)
                    WHERE id = {tenantId}
                    """)
                .AsNoTracking()
                .SingleAsync(cancellationToken)
            : await db.Tenants
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == tenantId, cancellationToken);

        return new ContentPublishingPolicySnapshot(
            tenant.ContentPublishingApprovalPolicy,
            tenant.ContentPublishingPolicyVersion);
    }
}

public sealed class ContentReviewCoordinator(
    AppDbContext db,
    IContentReviewExecutor executor,
    IContentPublishingApprovalPolicyResolver policyResolver,
    IClock clock,
    IContentAutoScheduler? autoScheduler = null,
    IContentRefiner? refiner = null) : IContentReviewCoordinator
{
    private const string StartedAction = "content.agent_review.started";
    private const string CompletedAction = "content.agent_review.completed";
    private const string StaleAction = "content.agent_review.stale_result_discarded";
    private const string ResourceType = "content_item";

    private static readonly JsonSerializerOptions AuditJsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IContentAutoScheduler? _autoScheduler = autoScheduler;
    private readonly IContentRefiner? _refiner = refiner;

    public async Task ProcessAsync(
        Guid tenantId,
        Guid taskId,
        Guid leaseToken,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("tenant_id_required", nameof(tenantId));

        var execution = await ClaimAsync(tenantId, taskId, leaseToken, cancellationToken);
        if (execution is null)
            return;

        var result = await RunReviewAsync(execution, cancellationToken);

        // Refine (P6, §4.7): reviewer reject kèm lý do máy đọc được + bài có L1/L2 đã lưu + chưa refine vòng nào
        // => chạy lại L3+L4 với lý do reject, sửa body TẠI CHỖ (giữ revision, review vẫn running), rồi chấm lại
        // ĐÚNG 1 lần bằng chính reviewer cũ. Vòng 2 vẫn reject => rơi về hàng chờ người (không refine tiếp).
        if (_refiner is not null && ShouldRefine(result, execution))
        {
            var refined = await TryApplyRefineAsync(execution, result, leaseToken, cancellationToken);
            if (refined is not null)
            {
                execution = refined;
                result = await RunReviewAsync(execution, cancellationToken);
            }
        }

        await CompleteAsync(execution, leaseToken, result, cancellationToken);
    }

    private async Task<ContentReviewExecutionResult> RunReviewAsync(
        ClaimedExecution execution,
        CancellationToken cancellationToken)
    {
        try
        {
            return await executor.ReviewAsync(execution.Request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return ReviewerFailure();
        }
    }

    // Chỉ reject (lý do máy đọc được, không rỗng) mới vào vòng sửa — needs_human/failed KHÔNG bao giờ kích refine
    // (§4.7). Bài thiếu L1/L2 (single-shot cũ / chain tắt) => bỏ qua. Bộ đếm 1-vòng kiểm lại trong transaction.
    private static bool ShouldRefine(ContentReviewExecutionResult result, ClaimedExecution execution) =>
        result.ReviewStatus == ContentItem.ReviewStatusRejected
        && !string.IsNullOrWhiteSpace(result.Reason)
        && !string.IsNullOrWhiteSpace(execution.Request.ChainPlanJson)
        && !string.IsNullOrWhiteSpace(execution.Request.ChainOutlineJson);

    // Chạy L3+L4 với lý do reject rồi ghi body mới TẠI CHỖ dưới lease đang giữ (không đổi revision, review vẫn
    // running). Trả execution mới (body + rowversion cập nhật) để chấm lại; null => giữ nguyên, CompleteAsync ghi
    // reject như thường. Guard: task còn lease + chưa refine vòng nào + item đúng revision/rowversion.
    private async Task<ClaimedExecution?> TryApplyRefineAsync(
        ClaimedExecution execution,
        ContentReviewExecutionResult rejectResult,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var request = execution.Request;
        ContentDraftRefineResult? draft;
        try
        {
            draft = await _refiner!.RefineAsync(
                new ContentRefineRequest(
                    request.TenantId,
                    request.BriefId,
                    request.Platform,
                    request.ChainPlanJson,
                    request.ChainOutlineJson,
                    rejectResult.Reason!),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (draft is null || string.IsNullOrWhiteSpace(draft.Body))
            return null;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var at = await GetLeaseTransitionTimeAsync(cancellationToken);
            var task = await LoadClaimedTaskAsync(
                execution.TaskId, request, leaseToken, at, cancellationToken);
            if (task is null || task.RefineAttemptCount > 0)
                return null;

            var item = await LoadTaskItemAsync(task, cancellationToken);
            if (item is null
                || item.ContentRevision != task.ContentRevision
                || !item.RowVersion.SequenceEqual(execution.ItemRowVersion))
            {
                return null;
            }

            var refinedAt = await GetLeaseTransitionTimeAsync(cancellationToken);
            if (!await TryFenceCompletionAsync(
                    task,
                    leaseToken,
                    refinedAt,
                    cancellationToken))
            {
                return null;
            }

            item.ApplyAgentRefine(draft.Body, refinedAt);
            task.RecordRefineAttempt(leaseToken, refinedAt);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            // Body + rowversion đổi sau khi ghi — execution mới để chấm lại và để fence CompleteAsync không báo conflict.
            return execution with
            {
                Request = request with { Body = item.Body },
                ItemRowVersion = item.RowVersion.ToArray(),
            };
        }
        // Lease hết hạn (chưa worker nào reclaim) hoặc item đổi trạng thái dưới chân refine: ApplyAgentRefine +
        // RecordRefineAttempt ném InvalidOperationException (content_review_not_running / content_item_deleted /
        // content_review_task_lease_expired), và SaveChangesAsync bung lại InvalidOperationException nội tại. Cùng
        // nghĩa "mất quyền dưới lease" như nhánh DbUpdateException (rowversion) — degrade về null để CompleteAsync
        // rơi vào fence lease tự no-op, đồng nhất với cách coordinator xử lý tranh lease ở mọi chỗ khác (không ném
        // exception ra worker). Trên SqlServer, LoadClaimedTaskAsync chỉ khớp lease theo danh tính (HasLeaseIdentity,
        // không kiểm hạn) nên khe hết-hạn-chưa-reclaim mới rơi tới đây; nhánh này khép kín nó.
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            return null;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<ClaimedExecution?> ClaimAsync(
        Guid tenantId,
        Guid taskId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var task = await LoadLeasedTaskAsync(
                tenantId,
                taskId,
                leaseToken,
                cancellationToken);
            if (task is null)
                return null;

            var item = await LoadTaskItemAsync(task, cancellationToken);
            if (item is null)
            {
                var failedAt = await GetLeaseTransitionTimeAsync(cancellationToken);
                if (await TryClaimDeliveryAsync(
                        task,
                        leaseToken,
                        failedAt,
                        cancellationToken))
                {
                    task.Fail(leaseToken, "content_review_item_unavailable", failedAt);
                    await SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                return null;
            }
            if (item.ContentRevision != task.ContentRevision)
            {
                var staleAt = await GetLeaseTransitionTimeAsync(cancellationToken);
                if (!await TryClaimDeliveryAsync(
                        task,
                        leaseToken,
                        staleAt,
                        cancellationToken))
                {
                    return null;
                }
                await CancelStaleAsync(task, item, staleAt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var orchestrationEligibility = await OrchestrationSessionGenerationFence.ResolveReviewEligibilityAsync(
                    db,
                    item,
                    cancellationToken).ConfigureAwait(false);
            var eligibilityAt = await GetLeaseTransitionTimeAsync(cancellationToken);
            if (orchestrationEligibility == OrchestrationItemReviewEligibility.Deferred)
            {
                task.DeferForOrchestrationStop(
                    leaseToken,
                    eligibilityAt.AddMinutes(1),
                    eligibilityAt);
                if (item.AgentReviewStatus == ContentItem.ReviewStatusRunning)
                {
                    item.DeferAgentReviewForOrchestrationStop(
                        task.ContentRevision,
                        eligibilityAt);
                }
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            if (orchestrationEligibility == OrchestrationItemReviewEligibility.Reject)
            {
                if (item.Status == "draft")
                {
                    item.RejectForOrchestrationFailure(
                        item.OrchestrationSessionId!.Value,
                        item.OrchestrationPlanGeneration!.Value,
                        eligibilityAt);
                }
                task.CancelForOrchestrationFailure(eligibilityAt);
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var reviewer = await FindReviewerAsync(task.TenantId, cancellationToken);
            if (reviewer is null || reviewer.Id == item.CreatedByAgentId)
            {
                var completed = await CompleteFallbackAsync(
                    task,
                    item,
                    leaseToken,
                    reviewer is null
                        ? ContentItem.ReviewReasonReviewerUnavailable
                        : ContentItem.ReviewReasonReviewerIndependence,
                    cancellationToken);
                if (completed)
                    await transaction.CommitAsync(cancellationToken);
                return null;
            }

            var hasStartedAudit = await HasStartedAuditAsync(
                task.TenantId,
                task.Id,
                task.ReviewCycle,
                cancellationToken);
            var claimedAt = await GetLeaseTransitionTimeAsync(cancellationToken);
            if (!await TryClaimDeliveryAsync(
                    task,
                    leaseToken,
                    claimedAt,
                    cancellationToken))
            {
                return null;
            }
            if (!await TryBeginReviewAsync(
                    task,
                    item,
                    leaseToken,
                    claimedAt,
                    cancellationToken))
            {
                await transaction.CommitAsync(cancellationToken);
                return null;
            }
            if (!hasStartedAudit)
                db.AuditLogs.Add(CreateStartedAudit(task, item.Id, reviewer.Id, claimedAt));
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return new ClaimedExecution(
                task.Id,
                new ContentReviewExecutionRequest(
                    task.TenantId,
                    item.Id,
                    task.ContentRevision,
                    item.Platform,
                    item.Body,
                    item.BriefId,
                    item.ChainPlanJson,
                    item.ChainOutlineJson),
                reviewer.Id,
                item.RowVersion.ToArray());
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            if (await WasClaimedByAnotherDeliveryAsync(
                    tenantId,
                    taskId,
                    leaseToken,
                    await GetLeaseTransitionTimeAsync(cancellationToken),
                    cancellationToken))
            {
                return null;
            }

            throw;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    private async Task<ContentReviewTask?> LoadLeasedTaskAsync(
        Guid tenantId,
        Guid taskId,
        Guid leaseToken,
        CancellationToken cancellationToken)
    {
        var task = await db.ContentReviewTasks.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidate => candidate.Id == taskId && candidate.TenantId == tenantId,
            cancellationToken);
        return task is not null
            && task.Status == ContentReviewTask.StatusLeased
            && task.LeaseToken == leaseToken
                ? task
                : null;
    }

    private Task<ContentItem?> LoadTaskItemAsync(
        ContentReviewTask task,
        CancellationToken cancellationToken) =>
        db.ContentItems.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidate => candidate.TenantId == task.TenantId
                && candidate.Id == task.ContentItemId,
            cancellationToken);

    private Task<AgentDefinition?> FindReviewerAsync(
        Guid tenantId,
        CancellationToken cancellationToken) =>
        db.AgentDefinitions.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidate => candidate.TenantId == tenantId
                && candidate.Code == executor.AgentCode
                && candidate.DeletedAt == null,
            cancellationToken);

    private async Task<bool> TryBeginReviewAsync(
        ContentReviewTask task,
        ContentItem item,
        Guid leaseToken,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        try
        {
            item.BeginAgentReview(task.ContentRevision, at);
            return true;
        }
        catch (InvalidOperationException exception)
            when (IsPermanentReviewIneligibility(exception.Message))
        {
            task.Fail(leaseToken, exception.Message, at);
            // Item đã cạn lượt review nhưng vẫn đang pending => UI kẹt "Chờ Agent review" vĩnh viễn.
            // Kết liễu item cùng lúc với task, trong cùng một SaveChanges.
            if (exception.Message == ContentItem.ReviewReasonAttemptLimitReached)
            {
                try
                {
                    item.MarkAgentReviewExhausted(task.ContentRevision, at);
                }
                catch (InvalidOperationException)
                {
                    // Revision đã đổi hoặc bài đã publish — không có gì phải sửa.
                }
            }

            await SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    private static bool IsPermanentReviewIneligibility(string errorCode) =>
        errorCode is "content_review_attempt_limit_reached"
            or "content_final_rejection_requires_new_revision"
            or "content_item_deleted"
            or "content_published_item_immutable"
            or "content_publish_attempt_active";

    private async Task<bool> CompleteFallbackAsync(
        ContentReviewTask task,
        ContentItem item,
        Guid leaseToken,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        var policy = await policyResolver.ResolveAsync(task.TenantId, cancellationToken);
        await db.Entry(task).ReloadAsync(cancellationToken);
        var completedAt = await GetLeaseTransitionTimeAsync(cancellationToken);
        if (!await TryClaimDeliveryAsync(
                task,
                leaseToken,
                completedAt,
                cancellationToken))
        {
            return false;
        }

        if (!await TryBeginReviewAsync(
                task,
                item,
                leaseToken,
                completedAt,
                cancellationToken))
        {
            return true;
        }

        item.RecordUnattributedReviewFallback(
            task.ContentRevision,
            ContentItem.ImageReviewStatusNotApplicable,
            reasonCode,
            completedAt);
        item.RecordReviewPolicySnapshot(
            task.ContentRevision,
            policy.Value,
            policy.Version,
            completedAt);
        task.Complete(leaseToken, completedAt);
        db.AuditLogs.Add(CreateCompletedAudit(
            task,
            item,
            reviewerAgentId: null,
            reasonCode,
            policy,
            stateSequence: 1,
            completedAt));
        await SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CancelStaleAsync(
        ContentReviewTask task,
        ContentItem item,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var hasStartedAudit = await HasStartedAuditAsync(
            task.TenantId,
            task.Id,
            task.ReviewCycle,
            cancellationToken);
        task.CancelStale(at);
        db.AuditLogs.Add(CreateStaleAudit(
            task,
            item.Id,
            item.ContentRevision,
            hasStartedAudit ? 2 : 1,
            at));
        await SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteAsync(
        ClaimedExecution execution,
        Guid leaseToken,
        ContentReviewExecutionResult result,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var validationAt = await GetLeaseTransitionTimeAsync(cancellationToken);
            var task = await LoadClaimedTaskAsync(
                execution.TaskId,
                execution.Request,
                leaseToken,
                validationAt,
                cancellationToken);
            if (task is null)
            {
                await TryRecoverLostLeaseExecutionItemAsync(execution, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var item = await LoadTaskItemAsync(task, cancellationToken)
                ?? throw new InvalidOperationException("content_review_item_not_found");
            if (item.ContentRevision != task.ContentRevision)
            {
                await CancelStaleAsync(task, item, validationAt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (!item.RowVersion.SequenceEqual(execution.ItemRowVersion))
                throw new DbUpdateConcurrencyException("content_item_concurrency_conflict");

            var reviewEligibility = await OrchestrationSessionGenerationFence.ResolveReviewEligibilityAsync(
                    db,
                    item,
                    cancellationToken).ConfigureAwait(false);
            var policy = await policyResolver.ResolveAsync(task.TenantId, cancellationToken);
            await db.Entry(task).ReloadAsync(cancellationToken);
            await db.Entry(item).ReloadAsync(cancellationToken);
            var completedAt = await GetLeaseTransitionTimeAsync(cancellationToken);
            if (!await TryFenceCompletionAsync(
                    task,
                    leaseToken,
                    completedAt,
                    cancellationToken))
            {
                await TryRecoverLostLeaseExecutionItemAsync(execution, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (reviewEligibility == OrchestrationItemReviewEligibility.Deferred)
            {
                task.DeferForOrchestrationStop(
                    leaseToken,
                    completedAt.AddMinutes(1),
                    completedAt);
                item.DeferAgentReviewForOrchestrationStop(
                    task.ContentRevision,
                    completedAt);
                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (reviewEligibility == OrchestrationItemReviewEligibility.Reject)
            {
                if (item.Status == "draft")
                {
                    item.RejectForOrchestrationFailure(
                        item.OrchestrationSessionId!.Value,
                        item.OrchestrationPlanGeneration!.Value,
                        completedAt);
                }

                if (task.Status is ContentReviewTask.StatusPending or ContentReviewTask.StatusLeased)
                {
                    task.CancelForOrchestrationFailure(completedAt);
                    var hasStartedAudit = await HasStartedAuditAsync(
                        task.TenantId,
                        task.Id,
                        task.ReviewCycle,
                        cancellationToken);
                    db.AuditLogs.Add(CreateStaleAudit(
                        task,
                        item.Id,
                        item.ContentRevision,
                        hasStartedAudit ? 2 : 1,
                        completedAt));
                }

                await SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            if (item.ContentRevision != task.ContentRevision)
            {
                await CancelStaleAsync(task, item, completedAt, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return;
            }
            if (!item.RowVersion.SequenceEqual(execution.ItemRowVersion))
                throw new DbUpdateConcurrencyException("content_item_concurrency_conflict");

            item.RecordAgentReview(
                task.ContentRevision,
                result.ReviewStatus,
                result.ImageReviewStatus,
                result.ReviewedImageCount,
                execution.ReviewerAgentId,
                result.ReasonCode,
                completedAt);
            item.RecordReviewPolicySnapshot(
                task.ContentRevision,
                policy.Value,
                policy.Version,
                completedAt);
            // Phase 3.3/3.4: automatic + passed → approve + schedule intent; otherwise human queue.
            await ApplyPublishingRoutingAsync(item, policy, completedAt, cancellationToken);
            task.Complete(leaseToken, completedAt);
            db.AuditLogs.Add(CreateCompletedAudit(
                task,
                item,
                execution.ReviewerAgentId,
                result.ReasonCode,
                policy,
                stateSequence: 2,
                completedAt));
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            if (await HasLostOwnershipAsync(
                    execution.Request,
                    leaseToken,
                    await GetLeaseTransitionTimeAsync(cancellationToken),
                    cancellationToken))
            {
                return;
            }

            throw;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // The external reviewer may outlive its lease. Restore only the unchanged item; a reclaimed
    // lease, a completed review, or a human edit changes the row version. This remains necessary
    // when a paused session resumes before the old reviewer returns.
    private async Task TryRecoverLostLeaseExecutionItemAsync(
        ClaimedExecution execution,
        CancellationToken cancellationToken)
    {
        var item = await db.ContentItems.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == execution.Request.TenantId
                    && candidate.Id == execution.Request.ContentItemId,
                cancellationToken)
            .ConfigureAwait(false);
        if (item is null
            || item.ContentRevision != execution.Request.ExpectedRevision
            || item.AgentReviewStatus != ContentItem.ReviewStatusRunning
            || !item.RowVersion.SequenceEqual(execution.ItemRowVersion))
        {
            return;
        }

        item.DeferAgentReviewForOrchestrationStop(
            execution.Request.ExpectedRevision,
            await GetLeaseTransitionTimeAsync(cancellationToken));
        await SaveChangesAsync(cancellationToken);
    }

    private async Task<ContentReviewTask?> LoadClaimedTaskAsync(
        Guid taskId,
        ContentReviewExecutionRequest request,
        Guid leaseToken,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var task = await db.ContentReviewTasks.IgnoreQueryFilters().SingleOrDefaultAsync(
            candidate => candidate.Id == taskId
                && candidate.TenantId == request.TenantId,
            cancellationToken);
        var hasExpectedLease = db.Database.IsSqlServer()
            ? HasLeaseIdentity(task, leaseToken)
            : HasActiveLease(task, leaseToken, at);
        return hasExpectedLease
            && task!.TenantId == request.TenantId
            && task.ContentItemId == request.ContentItemId
            && task.ContentRevision == request.ExpectedRevision
            && task.ClaimedLeaseToken == leaseToken
                ? task
                : null;
    }

    private async Task<bool> TryClaimDeliveryAsync(
        ContentReviewTask task,
        Guid leaseToken,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer() && !HasActiveLease(task, leaseToken, at))
            return false;

        var affected = db.Database.IsSqlServer()
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                /* content-review-delivery-claim */
                UPDATE dbo.content_review_tasks
                SET claimed_lease_token = lease_token
                WHERE id = {task.Id}
                  AND tenant_id = {task.TenantId}
                  AND status = N'leased'
                  AND lease_token = {leaseToken}
                  AND claimed_lease_token IS NULL
                  AND lease_expires_at > SYSDATETIMEOFFSET();
                """, cancellationToken)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                /* content-review-delivery-claim */
                UPDATE content_review_tasks
                SET claimed_lease_token = lease_token
                WHERE id = {task.Id}
                  AND tenant_id = {task.TenantId}
                  AND status = 'leased'
                  AND lease_token = {leaseToken}
                  AND claimed_lease_token IS NULL;
                """, cancellationToken);
        if (affected != 1)
            return false;

        await db.Entry(task).ReloadAsync(cancellationToken);
        return task.ClaimedLeaseToken == leaseToken;
    }

    private async Task<bool> TryFenceCompletionAsync(
        ContentReviewTask task,
        Guid leaseToken,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsSqlServer() && !IsClaimedActiveLease(task, leaseToken, at))
            return false;

        var affected = db.Database.IsSqlServer()
            ? await db.Database.ExecuteSqlInterpolatedAsync($"""
                /* content-review-completion-fence */
                UPDATE dbo.content_review_tasks
                SET claimed_lease_token = claimed_lease_token
                WHERE id = {task.Id}
                  AND tenant_id = {task.TenantId}
                  AND status = N'leased'
                  AND lease_token = {leaseToken}
                  AND claimed_lease_token = {leaseToken}
                  AND lease_expires_at > SYSDATETIMEOFFSET();
                """, cancellationToken)
            : await db.Database.ExecuteSqlInterpolatedAsync($"""
                /* content-review-completion-fence */
                UPDATE content_review_tasks
                SET claimed_lease_token = claimed_lease_token
                WHERE id = {task.Id}
                  AND tenant_id = {task.TenantId}
                  AND status = 'leased'
                  AND lease_token = {leaseToken}
                  AND claimed_lease_token = {leaseToken};
                """, cancellationToken);
        if (affected != 1)
            return false;

        await db.Entry(task).ReloadAsync(cancellationToken);
        return true;
    }

    private async Task<DateTimeOffset> GetLeaseTransitionTimeAsync(
        CancellationToken cancellationToken) =>
        db.Database.IsSqlServer()
            ? await db.Database
                .SqlQuery<DateTimeOffset>($"SELECT SYSDATETIMEOFFSET() AS [Value]")
                .SingleAsync(cancellationToken)
            : clock.UtcNow;

    private static bool HasLeaseIdentity(
        ContentReviewTask? task,
        Guid leaseToken) =>
        task is not null
        && task.Status == ContentReviewTask.StatusLeased
        && task.LeaseToken == leaseToken;

    private static bool HasActiveLease(
        ContentReviewTask? task,
        Guid leaseToken,
        DateTimeOffset at) =>
        HasLeaseIdentity(task, leaseToken)
        && task!.LeaseExpiresAt is not null
        && task.LeaseExpiresAt > at;

    private static bool IsClaimedActiveLease(
        ContentReviewTask task,
        Guid leaseToken,
        DateTimeOffset at) =>
        HasActiveLease(task, leaseToken, at)
        && task.ClaimedLeaseToken == leaseToken;

    private async Task<bool> WasClaimedByAnotherDeliveryAsync(
        Guid tenantId,
        Guid taskId,
        Guid leaseToken,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var task = await db.ContentReviewTasks.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == taskId && candidate.TenantId == tenantId,
                cancellationToken);
        return task is null
            || task.Status != ContentReviewTask.StatusLeased
            || task.LeaseToken != leaseToken
            || task.LeaseExpiresAt is null
            || task.LeaseExpiresAt <= at
            || task.ClaimedLeaseToken == leaseToken;
    }

    private async Task<bool> HasLostOwnershipAsync(
        ContentReviewExecutionRequest request,
        Guid leaseToken,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var task = await db.ContentReviewTasks.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.TenantId == request.TenantId
                    && candidate.ContentItemId == request.ContentItemId
                    && candidate.ContentRevision == request.ExpectedRevision,
                cancellationToken);
        return task is null
            || task.Status != ContentReviewTask.StatusLeased
            || task.LeaseToken != leaseToken
            || task.ClaimedLeaseToken != leaseToken
            || task.LeaseExpiresAt is null
            || task.LeaseExpiresAt <= at;
    }

    private Task<bool> HasStartedAuditAsync(
        Guid tenantId,
        Guid taskId,
        int reviewCycle,
        CancellationToken cancellationToken) =>
        db.AuditLogs.IgnoreQueryFilters().AnyAsync(
            audit => audit.TenantId == tenantId
                && audit.EventKey == EventKey(taskId, reviewCycle, "started"),
            cancellationToken);

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is InvalidOperationException innerException)
        {
            ExceptionDispatchInfo.Capture(innerException).Throw();
            throw;
        }
    }

    private static ContentReviewExecutionResult ReviewerFailure() =>
        new(
            ContentItem.ReviewStatusFailed,
            ContentItem.ImageReviewStatusNotApplicable,
            0,
            "reviewer_error");

    // Phase 3.3 automatic + text passed: ApproveAutomatically + ContentAutoScheduler intent.
    // Phase 3.4 human_required or non-pass: leave draft for human queue (no schedule).
    private async Task ApplyPublishingRoutingAsync(
        ContentItem item,
        ContentPublishingPolicySnapshot policy,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var canAutoApprove = string.Equals(
                policy.Value,
                ContentItem.PublishingPolicyAutomatic,
                StringComparison.Ordinal)
            && item.AgentReviewStatus == ContentItem.ReviewStatusPassed
            && item.ImageReviewStatus != ContentItem.ImageReviewStatusFailed
            && item.HumanApprovalRequirementReason is null;
        if (!canAutoApprove)
            return;

        item.ApproveAutomatically(
            item.ContentRevision,
            policy.Value,
            policy.Version,
            at);

        if (_autoScheduler is null)
            return;

        // Facebook target may be null → held intent at golden time (design: missing target still persists intent).
        await _autoScheduler.CreateIntentAsync(
            item,
            publishTargetId: null,
            at,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static AuditLog CreateStartedAudit(
        ContentReviewTask task,
        Guid itemId,
        Guid reviewerAgentId,
        DateTimeOffset at) =>
        AuditLog.CreateBusinessEvent(
            task.TenantId,
            userId: null,
            StartedAction,
            ResourceType,
            itemId,
            at,
            EventKey(task.Id, task.ReviewCycle, "started"),
            stateSequence: 1,
            JsonSerializer.Serialize(
                new
                {
                    reviewTaskId = task.Id,
                    expectedRevision = task.ContentRevision,
                    reviewCycle = task.ReviewCycle,
                    reviewerAgentId,
                    reviewStatus = ContentItem.ReviewStatusRunning
                },
                AuditJsonOptions));

    private static AuditLog CreateCompletedAudit(
        ContentReviewTask task,
        ContentItem item,
        Guid? reviewerAgentId,
        string reasonCode,
        ContentPublishingPolicySnapshot policy,
        long stateSequence,
        DateTimeOffset at) =>
        AuditLog.CreateBusinessEvent(
            task.TenantId,
            userId: null,
            CompletedAction,
            ResourceType,
            item.Id,
            at,
            EventKey(task.Id, task.ReviewCycle, "completed"),
            stateSequence,
            JsonSerializer.Serialize(
                new
                {
                    reviewTaskId = task.Id,
                    expectedRevision = task.ContentRevision,
                    reviewCycle = task.ReviewCycle,
                    reviewerAgentId,
                    reviewStatus = item.AgentReviewStatus,
                    imageReviewStatus = item.ImageReviewStatus,
                    reviewedImageCount = item.ReviewedImageCount,
                    reasonCode,
                    publishingPolicy = policy.Value,
                    publishingPolicyVersion = policy.Version
                },
                AuditJsonOptions));

    private static AuditLog CreateStaleAudit(
        ContentReviewTask task,
        Guid itemId,
        int currentRevision,
        long stateSequence,
        DateTimeOffset at) =>
        AuditLog.CreateBusinessEvent(
            task.TenantId,
            userId: null,
            StaleAction,
            ResourceType,
            itemId,
            at,
            EventKey(task.Id, task.ReviewCycle, "stale"),
            stateSequence,
            JsonSerializer.Serialize(
                new
                {
                    reviewTaskId = task.Id,
                    expectedRevision = task.ContentRevision,
                    reviewCycle = task.ReviewCycle,
                    currentRevision,
                    disposition = "stale_revision"
                },
                AuditJsonOptions));

    private static string EventKey(Guid taskId, int reviewCycle, string transition) =>
        $"content-review:{taskId:N}:cycle:{reviewCycle}:{transition}";

    private sealed record ClaimedExecution(
        Guid TaskId,
        ContentReviewExecutionRequest Request,
        Guid ReviewerAgentId,
        byte[] ItemRowVersion);
}
