using Clawbot.Domain.Common;

namespace Clawbot.Domain.Content;

public sealed class ContentItem : AggregateRoot<Guid>, ITenantOwned
{
    public const int MaxReviewReasonLength = 1024;
    public const int MaxApprovalReasonLength = 1024;
    public const int MaxHumanApprovalRequirementReasonLength = 32;
    public const int MaxAgentReviewAttempts = 5;

    public const string ReviewStatusPending = "pending";
    public const string ReviewStatusRunning = "running";
    public const string ReviewStatusPassed = "passed";
    public const string ReviewStatusRejected = "rejected";
    public const string ReviewStatusNeedsHuman = "needs_human";
    public const string ReviewStatusFailed = "failed";
    public const string ReviewStatusLegacyExempt = "legacy_exempt";

    public const string ImageReviewStatusPending = "pending";
    public const string ImageReviewStatusRunning = "running";
    public const string ImageReviewStatusReviewed = "reviewed";
    public const string ImageReviewStatusNotApplicable = "not_applicable";
    public const string ImageReviewStatusSkippedUnsupported = "skipped_unsupported";
    public const string ImageReviewStatusFailed = "failed";

    public const string ApprovalModeAutomatic = "automatic";
    public const string ApprovalModeHuman = "human";
    public const string ApprovalModeHumanOverride = "human_override";
    public const string PublishingPolicyAutomatic = "automatic";
    public const string PublishingPolicyHumanRequired = "human_required";
    public const string ReviewReasonReviewerIndependence = "reviewer_independence";
    public const string ReviewReasonReviewerUnavailable = "reviewer_unavailable";
    public const string ReviewReasonAttemptLimitReached = "content_review_attempt_limit_reached";
    public const string HumanApprovalReasonAgentNonPass = "agent_non_pass";
    public const string HumanApprovalReasonTenantPolicy = "tenant_policy";
    public const string HumanApprovalReasonMigrationCutover = "migration_cutover";

    public Guid TenantId { get; private set; }
    public Guid? BriefId { get; private set; }
    public string Platform { get; private set; } = string.Empty;
    public string Status { get; private set; } = "draft";  // draft|approved|scheduled|published|rejected
    public string Body { get; private set; } = string.Empty;
    public string AssetsJson { get; private set; } = "[]";
    public Guid? CreatedBy { get; private set; }
    // Review-gate P1: agent that generated this item — the reviewer must be a DIFFERENT definition
    // (separation of duties), enforced in ContentApproveTool and the Review RPC.
    public Guid? CreatedByAgentId { get; private set; }
    public Guid? ApprovedBy { get; private set; }
    // SPEC-16 P2-6: when a reviewer (lead-type) agent approves, attribution is the agent_definition id, not a human userId.
    public Guid? ApprovedByAgentId { get; private set; }
    public DateTimeOffset? ApprovedAt { get; private set; }
    public string? RejectedReason { get; private set; }

    // Prompt chaining P4: ảnh chụp L1 (plan) + L2 (outline, kèm SelectedHookIndex) dạng JSON — CHỈ set khi chuỗi
    // chạy đủ 4 mắt xích thành công. Repurpose/đổi hook (§4.5) tái dùng để chạy lại chỉ L3+L4, khỏi gọi lại LLM/RAG
    // cho L1/L2. NULL = bài tạo bằng single-shot (hoặc chain tắt) => repurpose chạy full chuỗi từ body.
    public string? ChainPlanJson { get; private set; }
    public string? ChainOutlineJson { get; private set; }
    // Phiên/kế hoạch orchestration tạo item này; null cho nội dung tạo ngoài orchestration.
    public Guid? OrchestrationSessionId { get; private set; }
    public int? OrchestrationPlanGeneration { get; private set; }
    // Bản nháp được người dùng sửa hoặc chủ động review lại không còn bị một replan tự động hủy.
    public DateTimeOffset? OrchestrationOwnershipClaimedAt { get; private set; }
    public Guid? OrchestrationOwnershipClaimedBy { get; private set; }

    public int ContentRevision { get; private set; } = 1;
    public string AgentReviewStatus { get; private set; } = ReviewStatusPending;
    public int? AgentReviewedRevision { get; private set; }
    public Guid? ReviewedByAgentId { get; private set; }
    public DateTimeOffset? AgentReviewStartedAt { get; private set; }
    public DateTimeOffset? AgentReviewedAt { get; private set; }
    public string? AgentReviewReason { get; private set; }
    public string ImageReviewStatus { get; private set; } = ImageReviewStatusPending;
    public int ReviewedImageCount { get; private set; }
    public int AgentReviewAttemptCount { get; private set; }
    public string? PublishingPolicyApplied { get; private set; }
    public long? PublishingPolicyVersionApplied { get; private set; }
    public string? HumanApprovalRequirementReason { get; private set; }
    public int? ApprovedRevision { get; private set; }
    public string? ApprovalMode { get; private set; }
    public string? ApprovalReason { get; private set; }
    public Guid? ActivePublishAttemptId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    // Review-gate P4 (SLA): giờ đăng mong muốn — set NGAY khi người/agent lên lịch, kể cả khi review-gate
    // chặn không tạo được schedule row, để ContentReviewSlaJob còn deadline mà nhắc người duyệt.
    public DateTimeOffset? DesiredPublishAt { get; private set; }
    // Chống spam alert: mỗi tier (trước hạn / quá hạn) chỉ notify 1 lần.
    public DateTimeOffset? LastReviewAlertAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private ContentItem() { }

    public static ContentItem Create(
        Guid tenantId,
        string platform,
        string body,
        Guid? createdBy,
        DateTimeOffset createdAt,
        Guid? briefId = null,
        Guid? createdByAgentId = null,
        string? chainPlanJson = null,
        string? chainOutlineJson = null,
        Guid? orchestrationSessionId = null,
        int? orchestrationPlanGeneration = null)
    {
        if (orchestrationSessionId == Guid.Empty)
            throw new ArgumentException("orchestration_session_id_required", nameof(orchestrationSessionId));
        if (orchestrationSessionId.HasValue != orchestrationPlanGeneration.HasValue)
            throw new ArgumentException("orchestration_provenance_incomplete");
        if (orchestrationPlanGeneration is < 0)
            throw new ArgumentOutOfRangeException(
                nameof(orchestrationPlanGeneration),
                "orchestration_plan_generation_required");

        return new ContentItem
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BriefId = briefId,
            Platform = platform,
            Body = body,
            CreatedBy = createdBy,
            CreatedByAgentId = createdByAgentId,
            ChainPlanJson = chainPlanJson,
            ChainOutlineJson = chainOutlineJson,
            OrchestrationSessionId = orchestrationSessionId,
            OrchestrationPlanGeneration = orchestrationPlanGeneration,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public void BeginAgentReview(int expectedRevision, DateTimeOffset at)
    {
        EnsureCurrentRevision(expectedRevision);
        EnsureNotPublishedOrDeleted();
        EnsureNoActivePublishAttempt();
        if (Status == "rejected")
            throw new InvalidOperationException("content_final_rejection_requires_new_revision");
        if (AgentReviewAttemptCount >= MaxAgentReviewAttempts)
            throw new InvalidOperationException("content_review_attempt_limit_reached");

        var nextAttemptCount = checked(AgentReviewAttemptCount + 1);
        AgentReviewStatus = ReviewStatusRunning;
        AgentReviewedRevision = null;
        ReviewedByAgentId = null;
        AgentReviewStartedAt = at;
        AgentReviewedAt = null;
        AgentReviewReason = null;
        ImageReviewStatus = ImageReviewStatusRunning;
        ReviewedImageCount = 0;
        AgentReviewAttemptCount = nextAttemptCount;
        PublishingPolicyApplied = null;
        PublishingPolicyVersionApplied = null;
        ClearPublishingApproval();
        RejectedReason = null;
        Status = "draft";
        UpdatedAt = at;
    }

    // A stopped orchestration session must not consume an item-level review attempt while the task lease is deferred.
    public void DeferAgentReviewForOrchestrationStop(int expectedRevision, DateTimeOffset at)
    {
        EnsureCurrentRevision(expectedRevision);
        EnsureNotPublishedOrDeleted();
        if (AgentReviewStatus != ReviewStatusRunning)
            throw new InvalidOperationException("content_review_not_running");

        AgentReviewStatus = ReviewStatusPending;
        AgentReviewedRevision = null;
        ReviewedByAgentId = null;
        AgentReviewStartedAt = null;
        AgentReviewedAt = null;
        AgentReviewReason = null;
        ImageReviewStatus = ImageReviewStatusPending;
        ReviewedImageCount = 0;
        if (AgentReviewAttemptCount > 0)
            AgentReviewAttemptCount--;
        UpdatedAt = at;
    }

    public void RecordAgentReview(
        int expectedRevision,
        string reviewStatus,
        string imageStatus,
        int reviewedImageCount,
        Guid reviewerAgentId,
        string? reason,
        DateTimeOffset at)
    {
        EnsureCurrentRevision(expectedRevision);
        EnsureNotPublishedOrDeleted();
        if (AgentReviewStatus != ReviewStatusRunning)
            throw new InvalidOperationException("content_review_not_running");
        if (!IsCompletedReviewStatus(reviewStatus))
            throw new ArgumentException("content_review_status_invalid", nameof(reviewStatus));
        if (!IsCompletedImageReviewStatus(imageStatus))
            throw new ArgumentException("content_image_review_status_invalid", nameof(imageStatus));
        ArgumentOutOfRangeException.ThrowIfNegative(reviewedImageCount);
        if (imageStatus == ImageReviewStatusReviewed
            ? reviewedImageCount == 0
            : reviewedImageCount != 0)
        {
            throw new ArgumentException(
                "content_reviewed_image_count_invalid",
                nameof(reviewedImageCount));
        }
        if (reviewerAgentId == Guid.Empty)
            throw new ArgumentException("reviewer_agent_id_required", nameof(reviewerAgentId));
        if (CreatedByAgentId == reviewerAgentId)
            throw new ArgumentException(
                "content_reviewer_must_differ_from_generator",
                nameof(reviewerAgentId));

        AgentReviewStatus = reviewStatus;
        AgentReviewedRevision = expectedRevision;
        ReviewedByAgentId = reviewerAgentId;
        AgentReviewedAt = at;
        AgentReviewReason = NormalizeOptional(reason, MaxReviewReasonLength);
        ImageReviewStatus = imageStatus;
        ReviewedImageCount = reviewedImageCount;
        UpdatedAt = at;
    }

    public void RecordUnattributedReviewFallback(
        int expectedRevision,
        string imageStatus,
        string reasonCode,
        DateTimeOffset at)
    {
        EnsureCurrentRevision(expectedRevision);
        EnsureNotPublishedOrDeleted();
        if (AgentReviewStatus != ReviewStatusRunning)
            throw new InvalidOperationException("content_review_not_running");
        if (imageStatus is not ImageReviewStatusNotApplicable and not ImageReviewStatusFailed)
            throw new ArgumentException("content_image_review_fallback_status_invalid", nameof(imageStatus));
        if (reasonCode is not ReviewReasonReviewerIndependence and not ReviewReasonReviewerUnavailable)
            throw new ArgumentException("content_review_fallback_reason_invalid", nameof(reasonCode));

        AgentReviewStatus = ReviewStatusNeedsHuman;
        AgentReviewedRevision = expectedRevision;
        ReviewedByAgentId = null;
        AgentReviewedAt = at;
        AgentReviewReason = reasonCode;
        ImageReviewStatus = imageStatus;
        ReviewedImageCount = 0;
        if (HumanApprovalRequirementReason != HumanApprovalReasonMigrationCutover)
            HumanApprovalRequirementReason = HumanApprovalReasonAgentNonPass;
        UpdatedAt = at;
    }

    // Trạng thái quy trình suy ra từ các cột (không lưu). Đặt ở domain vì cả Api (DTO) lẫn AgentService
    // (tool content.list cho reviewer-agent) đều cần cùng một định nghĩa — lệch nhau là agent review nhầm bài.
    public string ResolveWorkflowState()
    {
        if (Status == "published")
            return "published";
        if (Status == "rejected")
            return "rejected";
        if (Status == "scheduled")
            return "scheduled";
        if (Status == "approved")
            return "approved_awaiting_schedule";
        if (AgentReviewStatus == ReviewStatusRunning)
            return "agent_review_running";
        if (AgentReviewedRevision != ContentRevision || AgentReviewStatus is ReviewStatusPending)
            return "awaiting_agent_review";
        if (AgentReviewStatus == ReviewStatusFailed)
            return "review_failed";
        if (AgentReviewStatus is ReviewStatusRejected or ReviewStatusNeedsHuman)
            return "agent_review_non_pass";

        return "awaiting_human_approval";
    }

    // Hàng đợi review bền đã cạn lượt cho revision này (worker chết giữa chừng, lease hết hạn liên tục, hoặc
    // BeginAgentReview bị chặn vì đủ MaxAgentReviewAttempts). Trước đây chỉ task bị terminalize còn item giữ
    // nguyên pending => UI kẹt "Chờ Agent review" vĩnh viễn. Đẩy về needs_human: fail-closed (không bao giờ tự
    // duyệt) nhưng bài rơi vào hàng chờ người thay vì biến mất khỏi mọi hàng đợi.
    public void MarkAgentReviewExhausted(int expectedRevision, DateTimeOffset at)
    {
        EnsureCurrentRevision(expectedRevision);
        EnsureNotPublishedOrDeleted();
        // Đã có kết quả review cho đúng revision này thì thôi — tránh ghi đè verdict thật bằng needs_human.
        if (AgentReviewedRevision == expectedRevision && IsCompletedReviewStatus(AgentReviewStatus))
            return;

        AgentReviewStatus = ReviewStatusNeedsHuman;
        AgentReviewedRevision = expectedRevision;
        ReviewedByAgentId = null;
        AgentReviewedAt = at;
        AgentReviewReason = ReviewReasonAttemptLimitReached;
        ImageReviewStatus = ImageReviewStatusFailed;
        ReviewedImageCount = 0;
        if (HumanApprovalRequirementReason != HumanApprovalReasonMigrationCutover)
            HumanApprovalRequirementReason = HumanApprovalReasonAgentNonPass;
        UpdatedAt = at;
    }

    // Người vận hành bấm "Thử agent review lại" sau khi chu kỳ review đã kết thúc (cạn lượt / failed / needs_human):
    // mở lại đúng một chu kỳ mới cho revision hiện tại. Không tạo revision mới (body không đổi) và không tự duyệt —
    // chỉ đưa item về pending để ContentReviewDispatchWorker nhận lại. Hành động này đã sau cổng quyền content:write.
    public void ReopenAgentReview(DateTimeOffset at)
    {
        EnsureNotPublishedOrDeleted();
        EnsureNoActivePublishAttempt();
        if (Status == "rejected")
            throw new InvalidOperationException("content_final_rejection_requires_new_revision");
        if (Status != "draft")
            throw new InvalidOperationException("content_review_retry_not_draft");
        if (AgentReviewStatus == ReviewStatusRunning)
            throw new InvalidOperationException("content_review_running");

        AgentReviewStatus = ReviewStatusPending;
        AgentReviewedRevision = null;
        ReviewedByAgentId = null;
        AgentReviewStartedAt = null;
        AgentReviewedAt = null;
        AgentReviewReason = null;
        ImageReviewStatus = ImageReviewStatusPending;
        ReviewedImageCount = 0;
        AgentReviewAttemptCount = 0;
        PublishingPolicyApplied = null;
        PublishingPolicyVersionApplied = null;
        HumanApprovalRequirementReason = null;
        ClearPublishingApproval();
        Status = "draft";
        UpdatedAt = at;
    }

    public void RecordReviewPolicySnapshot(
        int expectedRevision,
        string appliedPolicy,
        long appliedPolicyVersion,
        DateTimeOffset at)
    {
        EnsureCurrentCompletedReview(expectedRevision);
        if (!IsValidPublishingPolicy(appliedPolicy))
            throw new ArgumentException(
                "content_publishing_policy_invalid",
                nameof(appliedPolicy));
        EnsurePositivePolicyVersion(appliedPolicyVersion);

        PublishingPolicyApplied = appliedPolicy;
        PublishingPolicyVersionApplied = appliedPolicyVersion;
        if (HumanApprovalRequirementReason != HumanApprovalReasonMigrationCutover)
        {
            HumanApprovalRequirementReason = AgentReviewStatus != ReviewStatusPassed
                || ImageReviewStatus == ImageReviewStatusFailed
                    ? HumanApprovalReasonAgentNonPass
                    : appliedPolicy == PublishingPolicyHumanRequired
                        ? HumanApprovalReasonTenantPolicy
                        : null;
        }

        ClearPublishingApproval();
        Status = "draft";
        UpdatedAt = at;
    }

    public void ApproveAutomatically(
        int expectedRevision,
        string appliedPolicy,
        long appliedPolicyVersion,
        DateTimeOffset at)
    {
        EnsureCurrentPassedReview(expectedRevision);
        if (appliedPolicy != PublishingPolicyAutomatic)
            throw new ArgumentException("automatic_approval_requires_automatic_policy", nameof(appliedPolicy));
        EnsurePositivePolicyVersion(appliedPolicyVersion);
        if (HumanApprovalRequirementReason is not null)
            throw new InvalidOperationException("content_human_approval_required");

        RecordPublishingApproval(
            expectedRevision,
            appliedPolicy,
            appliedPolicyVersion,
            ApprovalModeAutomatic,
            approverUserId: null,
            reason: null,
            at);
    }

    public void ApproveForPublishing(
        int expectedRevision,
        Guid userId,
        string appliedPolicy,
        long appliedPolicyVersion,
        string? overrideReason,
        DateTimeOffset at)
    {
        EnsureCurrentCompletedReview(expectedRevision);
        if (userId == Guid.Empty)
            throw new ArgumentException("approver_user_id_required", nameof(userId));
        if (!IsValidPublishingPolicy(appliedPolicy))
            throw new ArgumentException("content_publishing_policy_invalid", nameof(appliedPolicy));
        EnsurePositivePolicyVersion(appliedPolicyVersion);

        var isOverride = AgentReviewStatus != ReviewStatusPassed
            || ImageReviewStatus == ImageReviewStatusFailed;
        var reason = NormalizeOptional(overrideReason, MaxApprovalReasonLength);
        if (isOverride && reason is null)
            throw new ArgumentException("content_override_reason_required", nameof(overrideReason));

        RecordPublishingApproval(
            expectedRevision,
            appliedPolicy,
            appliedPolicyVersion,
            isOverride ? ApprovalModeHumanOverride : ApprovalModeHuman,
            userId,
            isOverride ? reason : null,
            at);
        HumanApprovalRequirementReason = null;
    }

    public void RejectForPublishing(int expectedRevision, Guid userId, string reason, DateTimeOffset at)
    {
        EnsureCurrentCompletedReview(expectedRevision);
        if (userId == Guid.Empty)
            throw new ArgumentException("reviewer_user_id_required", nameof(userId));
        var normalizedReason = NormalizeRequired(reason, MaxApprovalReasonLength, "content_reject_reason_required");

        ClearPublishingApproval();
        Status = "rejected";
        RejectedReason = normalizedReason;
        HumanApprovalRequirementReason = null;
        UpdatedAt = at;
    }

    // Một phiên orchestration đã bị thay thế hoặc thất bại không được để lại draft mồ côi. Chỉ nội dung
    // do đúng phiên tạo, còn draft và chưa vào delivery mới được terminalize tự động.
    public void RejectForOrchestrationFailure(
        Guid orchestrationSessionId,
        int orchestrationPlanGeneration,
        DateTimeOffset at)
    {
        if (orchestrationSessionId == Guid.Empty)
            throw new ArgumentException("orchestration_session_id_required", nameof(orchestrationSessionId));
        if (orchestrationPlanGeneration < 0)
            throw new ArgumentOutOfRangeException(
                nameof(orchestrationPlanGeneration),
                "orchestration_plan_generation_required");
        if (OrchestrationSessionId != orchestrationSessionId
            || OrchestrationPlanGeneration != orchestrationPlanGeneration)
        {
            throw new InvalidOperationException("content_orchestration_provenance_mismatch");
        }
        if (OrchestrationOwnershipClaimedAt is not null)
            throw new InvalidOperationException("content_orchestration_ownership_claimed");

        EnsureNotPublishedOrDeleted();
        EnsureNoActivePublishAttempt();
        if (Status is not ("draft" or "approved" or "scheduled"))
            throw new InvalidOperationException("content_orchestration_rejection_not_actionable");

        ClearPublishingApproval();
        Status = "rejected";
        RejectedReason = "orchestration_plan_failed";
        HumanApprovalRequirementReason = null;
        UpdatedAt = at;
    }

    // Người dùng tiếp quản nội dung từ orchestration trước khi sửa hoặc yêu cầu review lại.
    // Provenance vẫn giữ để audit, nhưng replan không được hủy công việc đã do người chủ động tiếp quản.
    public void ClaimOrchestrationOwnershipForHuman(Guid? userId, DateTimeOffset at)
    {
        EnsureNotPublishedOrDeleted();
        EnsureNoActivePublishAttempt();
        if (OrchestrationSessionId is null || OrchestrationOwnershipClaimedAt is not null)
            return;

        OrchestrationOwnershipClaimedAt = at;
        OrchestrationOwnershipClaimedBy = userId;
        if (Status == "scheduled")
        {
            ClearPublishingApproval();
            Status = "draft";
        }
        UpdatedAt = at;
    }

    public void ReviseBody(string body, DateTimeOffset at)
    {
        var normalizedBody = NormalizeRequired(body, int.MaxValue, "content_body_required");
        if (string.Equals(Body, normalizedBody, StringComparison.Ordinal))
            return;

        EnsureRevisionCanChange();
        var nextRevision = checked(ContentRevision + 1);
        Body = normalizedBody;
        InvalidateForRevision(nextRevision, at);
    }

    // Đổi hook (P5, §4.5): body mới do chạy lại L3+L4 với hook marketer chọn; ChainOutlineJson cập nhật hook đã chọn
    // để lần đổi sau vẫn tái dùng. Luôn tạo revision mới + reset review (như ReviseBody) kể cả khi body trùng —
    // marketer đã chủ động đổi hook nên cần review lại. ChainPlanJson giữ nguyên (L1 không đổi khi chỉ đổi hook).
    public void ReviseForHookChange(string body, string chainOutlineJson, DateTimeOffset at)
    {
        var normalizedBody = NormalizeRequired(body, int.MaxValue, "content_body_required");
        EnsureRevisionCanChange();
        var nextRevision = checked(ContentRevision + 1);
        Body = normalizedBody;
        if (!string.IsNullOrWhiteSpace(chainOutlineJson))
            ChainOutlineJson = chainOutlineJson;
        InvalidateForRevision(nextRevision, at);
    }

    // Refine (P6, §4.7): reviewer reject kèm lý do => content-agent viết lại L3+L4, đổi body NGAY TRONG lượt review
    // đang chạy. GIỮ NGUYÊN revision + GIỮ review đang running (khác ReviseBody: không tạo revision mới, không reset
    // review) — coordinator chấm lại chính body này rồi RecordAgentReview đóng lượt. ChainPlanJson/OutlineJson không đổi.
    public void ApplyAgentRefine(string body, DateTimeOffset at)
    {
        var normalizedBody = NormalizeRequired(body, int.MaxValue, "content_body_required");
        EnsureNotPublishedOrDeleted();
        if (AgentReviewStatus != ReviewStatusRunning)
            throw new InvalidOperationException("content_review_not_running");
        Body = normalizedBody;
        UpdatedAt = at;
    }

    public void ReviseAssets(string assetsJson, DateTimeOffset at)
    {
        var normalizedAssets = NormalizeRequired(assetsJson, int.MaxValue, "content_assets_required");
        if (string.Equals(AssetsJson, normalizedAssets, StringComparison.Ordinal))
            return;

        EnsureRevisionCanChange();
        var nextRevision = checked(ContentRevision + 1);
        AssetsJson = normalizedAssets;
        InvalidateForRevision(nextRevision, at);
    }

    public bool CanScheduleCurrentRevision() =>
        DeletedAt is null
        && ActivePublishAttemptId is null
        && Status is "approved" or "scheduled"
        && HasCurrentCompletedReview()
        && ApprovedRevision == ContentRevision;

    public bool CanPublishCurrentRevision() =>
        DeletedAt is null
        && ActivePublishAttemptId is null
        && Status == "scheduled"
        && HasCurrentCompletedReview()
        && ApprovedRevision == ContentRevision;

    public void RequireHumanApproval(string reason, DateTimeOffset at)
    {
        EnsureNotPublishedOrDeleted();
        if (!IsValidHumanApprovalRequirementReason(reason))
        {
            throw new ArgumentException(
                "content_human_approval_reason_invalid",
                nameof(reason));
        }

        HumanApprovalRequirementReason = reason;
        ClearPublishingApproval();
        Status = "draft";
        UpdatedAt = at;
    }

    public void Approve(Guid approverUserId, DateTimeOffset at)
    {
        EnsureNotPublishedOrDeleted();
        Status = "approved";
        ApprovedBy = approverUserId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    // EARS[WHEN a reviewer agent approves a draft THE SYSTEM SHALL record the agent_definition id as the approver
    // (not a human userId) so audit attribution distinguishes autonomous approval from human approval]
    public void ApproveByAgent(Guid agentDefinitionId, DateTimeOffset at)
    {
        EnsureNotPublishedOrDeleted();
        if (agentDefinitionId == Guid.Empty) throw new ArgumentException("agent definition id required", nameof(agentDefinitionId));
        Status = "approved";
        ApprovedByAgentId = agentDefinitionId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    // Review-gate: đóng dấu chữ ký reviewer-agent lên item ĐÃ 'scheduled' mà KHÔNG đổi status — gỡ deadlock
    // "scheduled nhưng chưa ký" (publish job giữ, Review từng từ chối status scheduled). Gọi ApproveByAgent ở
    // đây sẽ revert về 'approved' và hủy lịch, nên tách riêng: chỉ gắn chữ ký, giữ nguyên 'scheduled'.
    public void AttachAgentSignoff(Guid agentDefinitionId, DateTimeOffset at)
    {
        EnsureNotPublishedOrDeleted();
        if (agentDefinitionId == Guid.Empty) throw new ArgumentException("agent definition id required", nameof(agentDefinitionId));
        ApprovedByAgentId = agentDefinitionId;
        ApprovedAt = at;
        UpdatedAt = at;
    }

    public void Reject(DateTimeOffset at, string? reason = null)
    {
        EnsureNotPublishedOrDeleted();
        Status = "rejected";
        RejectedReason = string.IsNullOrWhiteSpace(reason) ? RejectedReason : reason.Trim();
        UpdatedAt = at;
    }

    public void UpdateBody(string body, DateTimeOffset at) => ReviseBody(body, at);

    public void MarkScheduled(DateTimeOffset at)
    {
        if (!CanScheduleCurrentRevision())
            throw new InvalidOperationException("content_current_revision_not_schedulable");
        Status = "scheduled";
        UpdatedAt = at;
    }

    public void ClaimPublishAttempt(int expectedRevision, Guid publishAttemptId, DateTimeOffset at)
    {
        EnsureCurrentRevision(expectedRevision);
        if (publishAttemptId == Guid.Empty)
            throw new ArgumentException("publish_attempt_id_required", nameof(publishAttemptId));
        if (!CanPublishCurrentRevision())
            throw new InvalidOperationException("content_current_revision_not_publishable");

        ActivePublishAttemptId = publishAttemptId;
        UpdatedAt = at;
    }

    public void ReleasePublishAttempt(Guid publishAttemptId, DateTimeOffset at)
    {
        if (ActivePublishAttemptId != publishAttemptId)
            throw new InvalidOperationException("content_publish_attempt_mismatch");
        ActivePublishAttemptId = null;
        UpdatedAt = at;
    }

    public void MarkPublished(Guid publishAttemptId, DateTimeOffset at)
    {
        if (ActivePublishAttemptId != publishAttemptId)
            throw new InvalidOperationException("content_publish_attempt_mismatch");
        if (Status != "scheduled"
            || !HasCurrentCompletedReview()
            || ApprovedRevision != ContentRevision)
        {
            throw new InvalidOperationException("content_current_revision_not_publishable");
        }

        ActivePublishAttemptId = null;
        Status = "published";
        UpdatedAt = at;
    }

    public void MarkPublished(DateTimeOffset at)
    {
        if (!CanPublishCurrentRevision())
            throw new InvalidOperationException("content_current_revision_not_publishable");
        Status = "published";
        UpdatedAt = at;
    }

    // Bridge overload: giữ caller cũ compile nhưng không còn cho phép tắt review bằng flag.
    public void MarkPublished(DateTimeOffset at, bool requireAgentReview)
    {
        _ = requireAgentReview;
        MarkPublished(at);
    }

    public void SoftDelete(DateTimeOffset at)
    {
        EnsureNoActivePublishAttempt();
        DeletedAt = at;
        UpdatedAt = at;
    }

    public void SetAssets(string json, DateTimeOffset at) => ReviseAssets(json, at);

    public void RevertToApproved(DateTimeOffset at)
    {
        // Bridge for cancel-schedule paths only. Published items stay immutable so a
        // same-revision re-schedule cannot reopen after MarkPublished.
        EnsureNotPublishedOrDeleted();
        Status = "approved";
        UpdatedAt = at;
    }

    public void SetDesiredPublishAt(DateTimeOffset desiredAt, DateTimeOffset at)
    {
        DesiredPublishAt = desiredAt;
        UpdatedAt = at;
    }

    public void MarkReviewAlerted(DateTimeOffset at) => LastReviewAlertAt = at;

    private void RecordPublishingApproval(
        int revision,
        string appliedPolicy,
        long appliedPolicyVersion,
        string approvalMode,
        Guid? approverUserId,
        string? reason,
        DateTimeOffset at)
    {
        PublishingPolicyApplied = appliedPolicy;
        PublishingPolicyVersionApplied = appliedPolicyVersion;
        ApprovedRevision = revision;
        ApprovalMode = approvalMode;
        ApprovalReason = reason;
        ApprovedBy = approverUserId;
        ApprovedByAgentId = null;
        ApprovedAt = at;
        RejectedReason = null;
        Status = "approved";
        UpdatedAt = at;
    }

    private void InvalidateForRevision(int nextRevision, DateTimeOffset at)
    {
        ContentRevision = nextRevision;
        AgentReviewStatus = ReviewStatusPending;
        AgentReviewedRevision = null;
        ReviewedByAgentId = null;
        AgentReviewStartedAt = null;
        AgentReviewedAt = null;
        AgentReviewReason = null;
        ImageReviewStatus = ImageReviewStatusPending;
        ReviewedImageCount = 0;
        AgentReviewAttemptCount = 0;
        PublishingPolicyApplied = null;
        PublishingPolicyVersionApplied = null;
        HumanApprovalRequirementReason = null;
        ClearPublishingApproval();
        RejectedReason = null;
        DesiredPublishAt = null;
        LastReviewAlertAt = null;
        Status = "draft";
        UpdatedAt = at;
    }

    private void ClearPublishingApproval()
    {
        ApprovedRevision = null;
        ApprovalMode = null;
        ApprovalReason = null;
        ApprovedBy = null;
        ApprovedByAgentId = null;
        ApprovedAt = null;
    }

    private void EnsureCurrentPassedReview(int expectedRevision)
    {
        EnsureCurrentCompletedReview(expectedRevision);
        if (AgentReviewStatus != ReviewStatusPassed)
            throw new InvalidOperationException("content_agent_review_not_passed");
        if (ImageReviewStatus == ImageReviewStatusFailed)
            throw new InvalidOperationException("content_image_review_failed");
    }

    private void EnsureCurrentCompletedReview(int expectedRevision)
    {
        EnsureCurrentRevision(expectedRevision);
        EnsureNotPublishedOrDeleted();
        EnsureNoActivePublishAttempt();
        if (!HasCurrentCompletedReview())
            throw new InvalidOperationException("content_current_revision_not_reviewed");
    }

    private bool HasCurrentCompletedReview() =>
        AgentReviewedRevision == ContentRevision
        && IsCompletedReviewStatus(AgentReviewStatus)
        && IsCompletedImageReviewStatus(ImageReviewStatus);

    private void EnsureCurrentRevision(int expectedRevision)
    {
        if (expectedRevision != ContentRevision)
            throw new InvalidOperationException("content_revision_changed");
    }

    private void EnsureRevisionCanChange()
    {
        EnsureNotPublishedOrDeleted();
        EnsureNoActivePublishAttempt();
    }

    private void EnsureNotPublishedOrDeleted()
    {
        if (DeletedAt is not null)
            throw new InvalidOperationException("content_item_deleted");
        if (Status == "published")
            throw new InvalidOperationException("content_published_item_immutable");
    }

    private void EnsureNoActivePublishAttempt()
    {
        if (ActivePublishAttemptId is not null)
            throw new InvalidOperationException("content_publish_attempt_active");
    }

    private static void EnsurePositivePolicyVersion(long appliedPolicyVersion) =>
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appliedPolicyVersion);

    private static bool IsCompletedReviewStatus(string status) =>
        status is ReviewStatusPassed or ReviewStatusRejected or ReviewStatusNeedsHuman or ReviewStatusFailed;

    private static bool IsCompletedImageReviewStatus(string status) =>
        status is ImageReviewStatusReviewed
            or ImageReviewStatusNotApplicable
            or ImageReviewStatusSkippedUnsupported
            or ImageReviewStatusFailed;

    private static bool IsValidPublishingPolicy(string policy) =>
        policy is PublishingPolicyAutomatic or PublishingPolicyHumanRequired;

    private static bool IsValidHumanApprovalRequirementReason(string? reason) =>
        reason is HumanApprovalReasonAgentNonPass
            or HumanApprovalReasonTenantPolicy
            or HumanApprovalReasonMigrationCutover
        && reason.Length <= MaxHumanApprovalRequirementReasonLength;

    private static string NormalizeRequired(string? value, int maxLength, string errorCode)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            throw new ArgumentException(errorCode, nameof(value));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptional(string? value, int maxLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized))
            return null;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
}
