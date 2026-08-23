using Clawbot.Domain.Tenants;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Tenants;

public sealed class TenantTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    private static Tenant CreateDefault() => Tenant.Create("my-tenant", "My Tenant", "pro", Now);

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsInitialDefaults()
    {
        var tenant = CreateDefault();

        tenant.Slug.Should().Be("my-tenant");
        tenant.DisplayName.Should().Be("My Tenant");
        tenant.PlanName.Should().Be("pro");
        tenant.IsActive.Should().BeTrue();
        tenant.ContentPublishingApprovalPolicy.Should().Be(Tenant.ContentPublishingPolicyHumanRequired);
        tenant.ContentPublishingPolicyVersion.Should().Be(1);
        tenant.OrchestratorFailurePolicy.Should().Be(Tenant.OrchestratorFailurePolicyPause);
        tenant.RequireContentReview.Should().BeFalse();
        tenant.RequireOrchestrationApproval.Should().BeFalse();
        tenant.RequireChatReplyApproval.Should().BeFalse();
        tenant.SkipChatReplyReview.Should().BeFalse();
        tenant.RequireKbHumanReview.Should().BeFalse();
        tenant.MonthlyCostCapUsd.Should().BeNull();
        tenant.AiAutoReplyResumeMinutes.Should().Be(5);
        tenant.IdleAlertMinutes.Should().Be(5);
        tenant.LeadLostAfterDays.Should().Be(60);
        tenant.CreatedAt.Should().Be(Now);
    }

    // ── UpdateBranding ────────────────────────────────────────────────

    [Fact]
    public void UpdateBranding_SetsAllFields()
    {
        var tenant = CreateDefault();

        tenant.UpdateBranding("Brand", "https://logo.png", "#FF0000", "#00FF00", "Support", "Hello!");

        tenant.BrandName.Should().Be("Brand");
        tenant.LogoUrl.Should().Be("https://logo.png");
        tenant.PrimaryColor.Should().Be("#FF0000");
        tenant.AccentColor.Should().Be("#00FF00");
        tenant.SupportName.Should().Be("Support");
        tenant.WidgetGreeting.Should().Be("Hello!");
    }

    [Fact]
    public void UpdateBranding_NormalizesEmptyStringsToNull()
    {
        var tenant = CreateDefault();
        tenant.UpdateBranding("Brand", "https://logo.png", "#FF0000", "#00FF00", "Support", "Hello!");

        tenant.UpdateBranding("", "  ", null, "", "  ", null);

        tenant.BrandName.Should().BeNull();
        tenant.LogoUrl.Should().BeNull();
        tenant.PrimaryColor.Should().BeNull();
        tenant.AccentColor.Should().BeNull();
        tenant.SupportName.Should().BeNull();
        tenant.WidgetGreeting.Should().BeNull();
    }

    // ── SetOrchestratorFailurePolicy ──────────────────────────────────

    [Theory]
    [InlineData("pause")]
    [InlineData("replan")]
    [InlineData("fail")]
    [InlineData(" PAUSE ")]
    public void SetOrchestratorFailurePolicy_AcceptsValidValues(string policy)
    {
        var tenant = CreateDefault();

        tenant.SetOrchestratorFailurePolicy(policy);

        tenant.OrchestratorFailurePolicy.Should().Be(policy.Trim().ToLowerInvariant());
    }

    [Fact]
    public void SetOrchestratorFailurePolicy_ThrowsOnInvalidValue()
    {
        var tenant = CreateDefault();

        var act = () => tenant.SetOrchestratorFailurePolicy("invalid");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*orchestrator_failure_policy_invalid*");
    }

    // ── SetContentPublishingApprovalPolicy ────────────────────────────

    [Fact]
    public void SetContentPublishingApprovalPolicy_TransitionsAndBumpsVersion()
    {
        var tenant = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        tenant.SetContentPublishingApprovalPolicy(Tenant.ContentPublishingPolicyAutomatic, updatedAt);

        tenant.ContentPublishingApprovalPolicy.Should().Be(Tenant.ContentPublishingPolicyAutomatic);
        tenant.ContentPublishingPolicyVersion.Should().Be(2);
        tenant.ContentPublishingPolicyUpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void SetContentPublishingApprovalPolicy_NoOpWhenSameValue()
    {
        var tenant = CreateDefault();
        var originalVersion = tenant.ContentPublishingPolicyVersion;

        tenant.SetContentPublishingApprovalPolicy(Tenant.ContentPublishingPolicyHumanRequired, Now.AddMinutes(5));

        tenant.ContentPublishingPolicyVersion.Should().Be(originalVersion);
    }

    [Fact]
    public void SetContentPublishingApprovalPolicy_ThrowsOnInvalidValue()
    {
        var tenant = CreateDefault();

        var act = () => tenant.SetContentPublishingApprovalPolicy("invalid", Now);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*content_publishing_policy_invalid*");
    }

    // ── Boolean setters ───────────────────────────────────────────────

    [Fact]
    public void SetRequireOrchestrationApproval_UpdatesFlag()
    {
        var tenant = CreateDefault();

        tenant.SetRequireOrchestrationApproval(true);

        tenant.RequireOrchestrationApproval.Should().BeTrue();
    }

    [Fact]
    public void SetRequireContentReview_UpdatesFlag()
    {
        var tenant = CreateDefault();

        tenant.SetRequireContentReview(true);

        tenant.RequireContentReview.Should().BeTrue();
    }

    [Fact]
    public void SetRequireChatReplyApproval_UpdatesFlag()
    {
        var tenant = CreateDefault();

        tenant.SetRequireChatReplyApproval(true);

        tenant.RequireChatReplyApproval.Should().BeTrue();
    }

    [Fact]
    public void SetSkipChatReplyReview_UpdatesFlag()
    {
        var tenant = CreateDefault();

        tenant.SetSkipChatReplyReview(true);

        tenant.SkipChatReplyReview.Should().BeTrue();
    }

    [Fact]
    public void SetRequireKbHumanReview_UpdatesFlag()
    {
        var tenant = CreateDefault();

        tenant.SetRequireKbHumanReview(true);

        tenant.RequireKbHumanReview.Should().BeTrue();
    }

    // ── Numeric setters with clamping ─────────────────────────────────

    [Fact]
    public void SetMonthlyCostCapUsd_SetsPositiveValue()
    {
        var tenant = CreateDefault();

        tenant.SetMonthlyCostCapUsd(500m);

        tenant.MonthlyCostCapUsd.Should().Be(500m);
    }

    [Fact]
    public void SetMonthlyCostCapUsd_ClearsOnZeroOrNegative()
    {
        var tenant = CreateDefault();
        tenant.SetMonthlyCostCapUsd(500m);

        tenant.SetMonthlyCostCapUsd(0m);

        tenant.MonthlyCostCapUsd.Should().BeNull();
    }

    [Fact]
    public void SetMonthlyCostCapUsd_ClearsOnNull()
    {
        var tenant = CreateDefault();
        tenant.SetMonthlyCostCapUsd(500m);

        tenant.SetMonthlyCostCapUsd(null);

        tenant.MonthlyCostCapUsd.Should().BeNull();
    }

    [Fact]
    public void SetAiAutoReplyResumeMinutes_ClampsToMax1440()
    {
        var tenant = CreateDefault();

        tenant.SetAiAutoReplyResumeMinutes(2000);

        tenant.AiAutoReplyResumeMinutes.Should().Be(1440);
    }

    [Fact]
    public void SetAiAutoReplyResumeMinutes_DefaultsToFiveOnZeroOrNegative()
    {
        var tenant = CreateDefault();

        tenant.SetAiAutoReplyResumeMinutes(0);

        tenant.AiAutoReplyResumeMinutes.Should().Be(5);
    }

    [Fact]
    public void SetIdleAlertMinutes_ClampsToMax1440()
    {
        var tenant = CreateDefault();

        tenant.SetIdleAlertMinutes(9999);

        tenant.IdleAlertMinutes.Should().Be(1440);
    }

    [Fact]
    public void SetIdleAlertMinutes_DefaultsToFiveOnNegative()
    {
        var tenant = CreateDefault();

        tenant.SetIdleAlertMinutes(-1);

        tenant.IdleAlertMinutes.Should().Be(5);
    }

    [Fact]
    public void SetLeadLostAfterDays_ClampsToMax365()
    {
        var tenant = CreateDefault();

        tenant.SetLeadLostAfterDays(500);

        tenant.LeadLostAfterDays.Should().Be(365);
    }

    [Fact]
    public void SetLeadLostAfterDays_DefaultsTo60OnNegative()
    {
        var tenant = CreateDefault();

        tenant.SetLeadLostAfterDays(-10);

        tenant.LeadLostAfterDays.Should().Be(60);
    }

    [Fact]
    public void SetLeadLostAfterDays_AllowsZero()
    {
        var tenant = CreateDefault();

        tenant.SetLeadLostAfterDays(0);

        tenant.LeadLostAfterDays.Should().Be(0);
    }
}
