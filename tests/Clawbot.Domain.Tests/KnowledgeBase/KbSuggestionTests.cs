using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.KnowledgeBase;

public sealed class KbSuggestionTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ModuleId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset CreatedAt = new(2026, 7, 11, 2, 0, 0, TimeSpan.Zero);

    private static KbSuggestion NewUpdateSuggestion() =>
        KbSuggestion.Create(TenantId, KbSuggestion.OpUpdate, ModuleId, "Học phí HSK4", "## Học phí\n5tr/khóa", "AI trượt 3 lần", "[]", "hash-1", CreatedAt);

    [Fact]
    public void Create_add_must_not_target_module()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, ModuleId, "t", "c", "r", "[]", "h", CreatedAt);

        act.Should().Throw<ArgumentException>().WithMessage("add_must_not_target_module*");
    }

    [Fact]
    public void Create_update_requires_target_module()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpUpdate, null, "t", "c", "r", "[]", "h", CreatedAt);

        act.Should().Throw<ArgumentException>().WithMessage("update_or_merge_requires_target_module*");
    }

    [Fact]
    public void Create_rejects_invalid_op_and_missing_dedup_hash()
    {
        var badOp = () => KbSuggestion.Create(TenantId, "noop", null, "t", "c", "r", "[]", "h", CreatedAt);
        var noHash = () => KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null, "t", "c", "r", "[]", " ", CreatedAt);

        badOp.Should().Throw<ArgumentException>().WithMessage("invalid_op*");
        noHash.Should().Throw<ArgumentException>().WithMessage("dedup_hash_required*");
    }

    [Fact]
    public void IsAutoApprovable_requires_verdict_approve_and_non_decreasing_measured_accuracy()
    {
        var s = NewUpdateSuggestion();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(before: 0.80m, after: 0.85m);

        s.IsAutoApprovable.Should().BeTrue();
    }

    [Theory]
    [InlineData(KbSuggestion.VerdictNeedsHuman, 0.80, 0.85)] // verdict không approve
    [InlineData(KbSuggestion.VerdictApprove, 0.80, 0.70)]    // accuracy giảm
    public void IsAutoApprovable_false_when_rail_not_met(string verdict, double before, double after)
    {
        var s = NewUpdateSuggestion();
        s.RecordReview(verdict, null);
        s.RecordAccuracy((decimal)before, (decimal)after);

        s.IsAutoApprovable.Should().BeFalse();
    }

    [Fact]
    public void IsAutoApprovable_false_when_accuracy_unmeasured()
    {
        // op=add module mới chưa có test case => accuracy NULL => không bao giờ auto "mù".
        var s = KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null, "Khai giảng", "## Lịch", "câu hỏi lặp", "[]", "hash-2", CreatedAt);
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(before: null, after: null);

        s.IsAutoApprovable.Should().BeFalse();
    }

    [Fact]
    public void Approve_auto_throws_when_rail_not_met()
    {
        var s = NewUpdateSuggestion();
        s.RecordReview(KbSuggestion.VerdictNeedsHuman, "không chắc");

        var act = () => s.Approve(CreatedAt.AddHours(1), decidedBy: null, KbSuggestion.ApprovalModeAuto);

        act.Should().Throw<InvalidOperationException>().WithMessage("auto_approve_rail_not_met");
        s.Status.Should().Be(KbSuggestion.StatusPending);
    }

    [Fact]
    public void Approve_auto_sets_mode_without_decider()
    {
        var s = NewUpdateSuggestion();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(0.80m, 0.80m);

        s.Approve(CreatedAt.AddHours(1), decidedBy: null, KbSuggestion.ApprovalModeAuto);

        s.Status.Should().Be(KbSuggestion.StatusApproved);
        s.ApprovalMode.Should().Be(KbSuggestion.ApprovalModeAuto);
        s.DecidedBy.Should().BeNull();
        s.DecidedAt.Should().Be(CreatedAt.AddHours(1));
    }

    [Fact]
    public void Approve_human_requires_decider_and_can_edit_content()
    {
        var s = NewUpdateSuggestion();
        var reviewer = Guid.NewGuid();

        var noDecider = () => s.Approve(CreatedAt.AddHours(1), decidedBy: null, KbSuggestion.ApprovalModeHuman);
        noDecider.Should().Throw<ArgumentException>().WithMessage("human_approval_requires_decider*");

        s.Approve(CreatedAt.AddHours(1), reviewer, KbSuggestion.ApprovalModeHuman, editedContentMd: "## Học phí\n5,5tr/khóa");

        s.ContentMd.Should().Contain("5,5tr");
        s.ApprovalMode.Should().Be(KbSuggestion.ApprovalModeHuman);
        s.DecidedBy.Should().Be(reviewer);
    }

    [Fact]
    public void Human_can_approve_even_when_rail_not_met()
    {
        // Người duyệt là gate cuối — rail chỉ ràng nhánh auto.
        var s = NewUpdateSuggestion();
        s.RecordReview(KbSuggestion.VerdictNeedsHuman, null);

        s.Approve(CreatedAt.AddHours(1), Guid.NewGuid(), KbSuggestion.ApprovalModeHuman);

        s.Status.Should().Be(KbSuggestion.StatusApproved);
    }

    [Fact]
    public void Decisions_are_one_way()
    {
        var s = NewUpdateSuggestion();
        s.Reject(CreatedAt.AddHours(1), Guid.NewGuid(), "trùng tri thức cũ");

        var reApprove = () => s.Approve(CreatedAt.AddHours(2), Guid.NewGuid(), KbSuggestion.ApprovalModeHuman);
        var reReview = () => s.RecordReview(KbSuggestion.VerdictApprove, null);

        reApprove.Should().Throw<InvalidOperationException>().WithMessage("suggestion_already_decided*");
        reReview.Should().Throw<InvalidOperationException>().WithMessage("suggestion_already_decided*");
        s.Status.Should().Be(KbSuggestion.StatusRejected);
        s.RejectedReason.Should().Be("trùng tri thức cũ");
    }

    [Fact]
    public void Reject_requires_reason()
    {
        var s = NewUpdateSuggestion();

        var act = () => s.Reject(CreatedAt.AddHours(1), Guid.NewGuid(), " ");

        act.Should().Throw<ArgumentException>().WithMessage("reject_reason_required*");
    }
}
