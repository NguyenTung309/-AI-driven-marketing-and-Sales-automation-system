using Clawbot.Domain.Common;

namespace Clawbot.Domain.KnowledgeBase;

// Đề xuất tri thức do job chưng cất sinh ra (staging) — KHÔNG đụng kb_versions cho tới khi qua gate:
// người duyệt, hoặc auto khi rail đạt (verdict approve + accuracy không giảm, cả 2 accuracy phải đo được).
// Nội dung/evidence phải được PII-redact TRƯỚC khi đưa vào entity này.
public sealed class KbSuggestion : AggregateRoot<Guid>, ITenantOwned
{
    public const string OpAdd = "add";
    public const string OpUpdate = "update";
    public const string OpMerge = "merge";

    public const string StatusPending = "pending";
    public const string StatusApproved = "approved";
    public const string StatusRejected = "rejected";

    public const string VerdictApprove = "approve";
    public const string VerdictReject = "reject";
    public const string VerdictNeedsHuman = "needs_human";

    public const string ApprovalModeAuto = "auto";
    public const string ApprovalModeHuman = "human";

    public Guid TenantId { get; private set; }
    public string Op { get; private set; } = OpAdd;
    public Guid? TargetKbModuleId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string ContentMd { get; private set; } = string.Empty;
    public string Rationale { get; private set; } = string.Empty;
    // Mảng JSON {conversationId, snippetRedacted, signal} — chỉ id + trích đoạn đã redact, không text thô.
    public string EvidenceJson { get; private set; } = "[]";
    public string DedupHash { get; private set; } = string.Empty;
    public string? ReviewerVerdict { get; private set; }
    public string? ReviewerNotes { get; private set; }
    public decimal? AccuracyBefore { get; private set; }
    public decimal? AccuracyAfter { get; private set; }
    public string Status { get; private set; } = StatusPending;
    public string? ApprovalMode { get; private set; }
    public string? RejectedReason { get; private set; }
    public Guid? DecidedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    private KbSuggestion() { }

    public static KbSuggestion Create(
        Guid tenantId,
        string op,
        Guid? targetKbModuleId,
        string title,
        string contentMd,
        string rationale,
        string evidenceJson,
        string dedupHash,
        DateTimeOffset createdAt)
    {
        if (op is not (OpAdd or OpUpdate or OpMerge))
            throw new ArgumentException($"invalid_op:{op}", nameof(op));
        if (op == OpAdd && targetKbModuleId is not null)
            throw new ArgumentException("add_must_not_target_module", nameof(targetKbModuleId));
        if (op != OpAdd && targetKbModuleId is null)
            throw new ArgumentException("update_or_merge_requires_target_module", nameof(targetKbModuleId));
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("title_required", nameof(title));
        if (string.IsNullOrWhiteSpace(contentMd))
            throw new ArgumentException("content_required", nameof(contentMd));
        if (string.IsNullOrWhiteSpace(dedupHash))
            throw new ArgumentException("dedup_hash_required", nameof(dedupHash));

        return new KbSuggestion
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Op = op,
            TargetKbModuleId = targetKbModuleId,
            Title = title.Trim(),
            ContentMd = contentMd,
            Rationale = rationale ?? string.Empty,
            EvidenceJson = string.IsNullOrWhiteSpace(evidenceJson) ? "[]" : evidenceJson,
            DedupHash = dedupHash,
            CreatedAt = createdAt,
        };
    }

    public void RecordReview(string verdict, string? notes)
    {
        EnsurePending();
        if (verdict is not (VerdictApprove or VerdictReject or VerdictNeedsHuman))
            throw new ArgumentException($"invalid_verdict:{verdict}", nameof(verdict));
        ReviewerVerdict = verdict;
        ReviewerNotes = notes;
    }

    public void RecordAccuracy(decimal? before, decimal? after)
    {
        EnsurePending();
        AccuracyBefore = before;
        AccuracyAfter = after;
    }

    // Rail auto-approve: cả 2 accuracy phải ĐO ĐƯỢC và không giảm, reviewer phải approve.
    // Thiếu bộ test (accuracy NULL — điển hình op=add module mới) => false, rơi về người duyệt.
    public bool IsAutoApprovable =>
        Status == StatusPending
        && ReviewerVerdict == VerdictApprove
        && AccuracyBefore.HasValue
        && AccuracyAfter.HasValue
        && AccuracyAfter.Value >= AccuracyBefore.Value;

    // Người duyệt được sửa nội dung trước khi duyệt; auto không sửa.
    public void Approve(DateTimeOffset at, Guid? decidedBy, string approvalMode, string? editedContentMd = null)
    {
        EnsurePending();
        switch (approvalMode)
        {
            case ApprovalModeAuto when decidedBy is not null:
                throw new ArgumentException("auto_approval_has_no_decider", nameof(decidedBy));
            case ApprovalModeAuto when !IsAutoApprovable:
                throw new InvalidOperationException("auto_approve_rail_not_met");
            case ApprovalModeHuman when decidedBy is null:
                throw new ArgumentException("human_approval_requires_decider", nameof(decidedBy));
            case ApprovalModeAuto or ApprovalModeHuman:
                break;
            default:
                throw new ArgumentException($"invalid_approval_mode:{approvalMode}", nameof(approvalMode));
        }

        if (approvalMode == ApprovalModeHuman && !string.IsNullOrWhiteSpace(editedContentMd))
            ContentMd = editedContentMd;

        Status = StatusApproved;
        ApprovalMode = approvalMode;
        DecidedBy = decidedBy;
        DecidedAt = at;
    }

    public void Reject(DateTimeOffset at, Guid decidedBy, string reason)
    {
        EnsurePending();
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("reject_reason_required", nameof(reason));
        Status = StatusRejected;
        RejectedReason = reason;
        DecidedBy = decidedBy;
        DecidedAt = at;
    }

    private void EnsurePending()
    {
        if (Status != StatusPending)
            throw new InvalidOperationException($"suggestion_already_decided:{Status}");
    }
}
