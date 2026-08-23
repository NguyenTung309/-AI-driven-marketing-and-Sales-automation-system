using Clawbot.Domain.KnowledgeBase;
using FluentAssertions;

namespace Clawbot.Domain.Tests.KnowledgeBase;

public sealed class KbSuggestionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ModuleId = Guid.NewGuid();
    private static readonly Guid DeciderId = Guid.NewGuid();

    private static KbSuggestion CreateAdd() => KbSuggestion.Create(
        TenantId, KbSuggestion.OpAdd, null, "New FAQ", "Content here",
        "Rationale", "[{\"conversationId\":\"c1\"}]", "hash-abc", Now);

    private static KbSuggestion CreateUpdate() => KbSuggestion.Create(
        TenantId, KbSuggestion.OpUpdate, ModuleId, "Updated FAQ", "New content",
        "Rationale", "[]", "hash-def", Now);

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_AddOp_SetsFields()
    {
        var s = CreateAdd();

        s.TenantId.Should().Be(TenantId);
        s.Op.Should().Be(KbSuggestion.OpAdd);
        s.TargetKbModuleId.Should().BeNull();
        s.Title.Should().Be("New FAQ");
        s.ContentMd.Should().Be("Content here");
        s.Rationale.Should().Be("Rationale");
        s.EvidenceJson.Should().Contain("c1");
        s.DedupHash.Should().Be("hash-abc");
        s.Status.Should().Be(KbSuggestion.StatusPending);
        s.ReviewerVerdict.Should().BeNull();
        s.AccuracyBefore.Should().BeNull();
        s.AccuracyAfter.Should().BeNull();
        s.ApprovalMode.Should().BeNull();
        s.DecidedBy.Should().BeNull();
        s.DecidedAt.Should().BeNull();
    }

    [Fact]
    public void Create_UpdateOp_RequiresTargetModule()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpUpdate, null,
            "T", "C", "R", "[]", "h", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("targetKbModuleId");
    }

    [Fact]
    public void Create_AddOp_RejectsTargetModule()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, ModuleId,
            "T", "C", "R", "[]", "h", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("targetKbModuleId");
    }

    [Fact]
    public void Create_MergeOp_RequiresTargetModule()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpMerge, null,
            "T", "C", "R", "[]", "h", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("targetKbModuleId");
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("delete")]
    public void Create_RejectsInvalidOp(string op)
    {
        var act = () => KbSuggestion.Create(TenantId, op, null, "T", "C", "R", "[]", "h", Now);

        act.Should().Throw<ArgumentException>().WithParameterName(nameof(op));
    }

    [Fact]
    public void Create_ThrowsOnEmptyTitle()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null,
            "", "C", "R", "[]", "h", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("title");
    }

    [Fact]
    public void Create_ThrowsOnEmptyContent()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null,
            "T", "  ", "R", "[]", "h", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("contentMd");
    }

    [Fact]
    public void Create_ThrowsOnEmptyDedupHash()
    {
        var act = () => KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null,
            "T", "C", "R", "[]", "", Now);

        act.Should().Throw<ArgumentException>().WithParameterName("dedupHash");
    }

    [Fact]
    public void Create_DefaultsEvidenceJsonToEmptyArray()
    {
        var s = KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null,
            "T", "C", "R", evidenceJson: "  ", dedupHash: "h", createdAt: Now);

        s.EvidenceJson.Should().Be("[]");
    }

    [Fact]
    public void Create_TrimsTitle()
    {
        var s = KbSuggestion.Create(TenantId, KbSuggestion.OpAdd, null,
            "  Spaced Title  ", "C", "R", "[]", "h", Now);

        s.Title.Should().Be("Spaced Title");
    }

    // ── RecordReview ──────────────────────────────────────────────────

    [Theory]
    [InlineData(KbSuggestion.VerdictApprove)]
    [InlineData(KbSuggestion.VerdictReject)]
    [InlineData(KbSuggestion.VerdictNeedsHuman)]
    public void RecordReview_SetsVerdictAndNotes(string verdict)
    {
        var s = CreateAdd();

        s.RecordReview(verdict, "Looks good");

        s.ReviewerVerdict.Should().Be(verdict);
        s.ReviewerNotes.Should().Be("Looks good");
    }

    [Fact]
    public void RecordReview_ThrowsOnInvalidVerdict()
    {
        var s = CreateAdd();

        var act = () => s.RecordReview("maybe", null);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordReview_ThrowsWhenAlreadyDecided()
    {
        var s = CreateAdd();
        s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman);

        var act = () => s.RecordReview(KbSuggestion.VerdictApprove, null);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── RecordAccuracy ────────────────────────────────────────────────

    [Fact]
    public void RecordAccuracy_SetsBeforeAndAfter()
    {
        var s = CreateAdd();

        s.RecordAccuracy(0.75m, 0.82m);

        s.AccuracyBefore.Should().Be(0.75m);
        s.AccuracyAfter.Should().Be(0.82m);
    }

    [Fact]
    public void RecordAccuracy_AllowsNullValues()
    {
        var s = CreateAdd();

        s.RecordAccuracy(null, null);

        s.AccuracyBefore.Should().BeNull();
        s.AccuracyAfter.Should().BeNull();
    }

    // ── IsAutoApprovable ──────────────────────────────────────────────

    [Fact]
    public void IsAutoApprovable_TrueWhenRailMet()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(0.70m, 0.75m);

        s.IsAutoApprovable.Should().BeTrue();
    }

    [Fact]
    public void IsAutoApprovable_FalseWhenAccuracyDecreased()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(0.80m, 0.75m);

        s.IsAutoApprovable.Should().BeFalse();
    }

    [Fact]
    public void IsAutoApprovable_FalseWhenAccuracyMissing()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);

        s.IsAutoApprovable.Should().BeFalse();
    }

    [Fact]
    public void IsAutoApprovable_FalseWhenVerdictNotApprove()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictNeedsHuman, null);
        s.RecordAccuracy(0.70m, 0.80m);

        s.IsAutoApprovable.Should().BeFalse();
    }

    [Fact]
    public void IsAutoApprovable_FalseWhenAlreadyApproved()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(0.70m, 0.80m);
        s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman);

        s.IsAutoApprovable.Should().BeFalse();
    }

    // ── Approve ───────────────────────────────────────────────────────

    [Fact]
    public void Approve_HumanMode_SetsStatusAndDecider()
    {
        var s = CreateAdd();

        s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman);

        s.Status.Should().Be(KbSuggestion.StatusApproved);
        s.ApprovalMode.Should().Be(KbSuggestion.ApprovalModeHuman);
        s.DecidedBy.Should().Be(DeciderId);
        s.DecidedAt.Should().Be(Now);
    }

    [Fact]
    public void Approve_HumanMode_AppliesEditedContent()
    {
        var s = CreateAdd();

        s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman, "Edited content");

        s.ContentMd.Should().Be("Edited content");
    }

    [Fact]
    public void Approve_HumanMode_PreservesContentWhenNoEdit()
    {
        var s = CreateAdd();

        s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman);

        s.ContentMd.Should().Be("Content here");
    }

    [Fact]
    public void Approve_HumanMode_ThrowsWithoutDecider()
    {
        var s = CreateAdd();

        var act = () => s.Approve(Now, null, KbSuggestion.ApprovalModeHuman);

        act.Should().Throw<ArgumentException>().WithParameterName("decidedBy");
    }

    [Fact]
    public void Approve_AutoMode_SucceedsWhenRailMet()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(0.70m, 0.75m);

        s.Approve(Now, null, KbSuggestion.ApprovalModeAuto);

        s.Status.Should().Be(KbSuggestion.StatusApproved);
        s.ApprovalMode.Should().Be(KbSuggestion.ApprovalModeAuto);
        s.DecidedBy.Should().BeNull();
    }

    [Fact]
    public void Approve_AutoMode_ThrowsWithDecider()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);
        s.RecordAccuracy(0.70m, 0.80m);

        var act = () => s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeAuto);

        act.Should().Throw<ArgumentException>().WithParameterName("decidedBy");
    }

    [Fact]
    public void Approve_AutoMode_ThrowsWhenRailNotMet()
    {
        var s = CreateAdd();
        s.RecordReview(KbSuggestion.VerdictApprove, null);

        var act = () => s.Approve(Now, null, KbSuggestion.ApprovalModeAuto);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Approve_ThrowsOnInvalidMode()
    {
        var s = CreateAdd();

        var act = () => s.Approve(Now, DeciderId, "unknown");

        act.Should().Throw<ArgumentException>().WithParameterName("approvalMode");
    }

    [Fact]
    public void Approve_ThrowsWhenAlreadyDecided()
    {
        var s = CreateAdd();
        s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman);

        var act = () => s.Approve(Now, DeciderId, KbSuggestion.ApprovalModeHuman);

        act.Should().Throw<InvalidOperationException>();
    }

    // ── Reject ────────────────────────────────────────────────────────

    [Fact]
    public void Reject_SetsStatusAndReason()
    {
        var s = CreateAdd();

        s.Reject(Now, DeciderId, "Duplicate content");

        s.Status.Should().Be(KbSuggestion.StatusRejected);
        s.RejectedReason.Should().Be("Duplicate content");
        s.DecidedBy.Should().Be(DeciderId);
        s.DecidedAt.Should().Be(Now);
    }

    [Fact]
    public void Reject_ThrowsOnEmptyReason()
    {
        var s = CreateAdd();

        var act = () => s.Reject(Now, DeciderId, "");

        act.Should().Throw<ArgumentException>().WithParameterName("reason");
    }

    [Fact]
    public void Reject_ThrowsWhenAlreadyDecided()
    {
        var s = CreateAdd();
        s.Reject(Now, DeciderId, "Bad");

        var act = () => s.Reject(Now, DeciderId, "Again");

        act.Should().Throw<InvalidOperationException>();
    }
}
