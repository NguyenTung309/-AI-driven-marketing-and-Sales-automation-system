using System.Text.Json;
using Clawbot.AgentService.Services;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using CoreContent = Clawbot.Agents.Core.Content;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ContentToolsTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 28, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task ContentGenerateTool_PersistsDraftAndReturnsContentId()
    {
        // EARS[WHEN the content tool generates a draft THE SYSTEM SHALL persist a ContentItem(draft) and return its id]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var sut = BuildGenerateTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = "facebook", ["brief"] = "HSK launch" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "content-agent"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Platform.Should().Be("facebook");
        saved.Body.Should().Be("Draft for facebook: Brief=HSK launch");
        saved.Status.Should().Be("draft");
        saved.TenantId.Should().Be(fx.TenantId);
        var payload = JsonSerializer.Deserialize<JsonElement>(result.Output, JsonOpts);
        payload.GetProperty("content_id").GetString().Should().Be(saved.Id.ToString());
        payload.GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public async Task ContentGenerateTool_FailsWhenPlatformOrBriefMissing()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var sut = BuildGenerateTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = "facebook" },
            new ToolContext(fx.TenantId, "task-1"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("brief");
        (await fx.Db.ContentItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public void ContentGenerateTool_schema_exposes_only_canonical_writable_platforms()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var sut = BuildGenerateTool(fx);

        sut.InputSchemaJson.Should().Contain("facebook|instagram|zalo");
        sut.InputSchemaJson.Should().NotContain("tiktok");
        sut.InputSchemaJson.Should().NotContain("youtube");
        sut.InputSchemaJson.Should().NotContain("website");
    }

    [Theory]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    public async Task ContentGenerateTool_rejects_unsupported_platform_before_generation(string platform)
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var sut = BuildGenerateTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = platform, ["brief"] = "HSK launch" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "content-agent"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("unsupported platform");
        (await fx.Db.ContentItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await fx.Db.ContentReviewTasks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ContentGenerateTool_normalizes_canonical_platform_before_generation()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var sut = BuildGenerateTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = " Instagram ", ["brief"] = "HSK launch" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "content-agent"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Platform.Should().Be("instagram");
        saved.Body.Should().StartWith("Draft for instagram:");
    }

    [Fact]
    public async Task ContentApproveTool_ApprovesDraft_AttributingAgentActor()
    {
        // EARS[WHEN a reviewer agent approves a draft THE SYSTEM SHALL set status=approved and record the agent_definition id]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var agentDefId = Guid.NewGuid();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString(), ["decision"] = "approve" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: agentDefId, AgentCode: "reviewer"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be("approved");
        saved.ApprovedByAgentId.Should().Be(agentDefId);
        saved.ApprovedBy.Should().BeNull(); // agent approval, not human
    }

    [Fact]
    public async Task ContentApproveTool_RejectsDraft_AndSurfacesReason()
    {
        // EARS[WHEN rejecting THE SYSTEM SHALL set status=rejected and surface the reason in the tool result]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString(), ["decision"] = "reject", ["reason"] = "off-brand" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "reviewer"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be("rejected");
        var payload = JsonSerializer.Deserialize<JsonElement>(result.Output, JsonOpts);
        payload.GetProperty("reason").GetString().Should().Be("off-brand");
    }

    [Fact]
    public async Task ContentApproveTool_RefusesApproveWithoutAgentIdentity()
    {
        // EARS[WHEN approving without an agent identity THE SYSTEM SHALL refuse (no anonymous autonomous approval)]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString(), ["decision"] = "approve" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: null, AgentCode: null),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("agent identity");
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be("draft"); // unchanged
    }

    [Fact]
    public async Task ContentApproveTool_ReReviewsApprovedItem_AddingAgentSignoff()
    {
        // Phase 0 review-gate: human-approved item can still receive the mandatory agent review
        // (ApprovedByAgentId is the publish precondition in Phase 1), so 'approved' must be re-reviewable.
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.Approve(Guid.NewGuid(), Now); // human approved, no agent signoff yet
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var agentDefId = Guid.NewGuid();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString(), ["decision"] = "approve" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: agentDefId, AgentCode: "reviewer"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be("approved");
        saved.ApprovedByAgentId.Should().Be(agentDefId);
    }

    [Fact]
    public async Task ContentApproveTool_RejectsApprovedItem_DemotingIt()
    {
        // Reviewer catches an issue after human approval -> demote approved item to rejected.
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.Approve(Guid.NewGuid(), Now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString(), ["decision"] = "reject", ["reason"] = "sai gia" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "reviewer"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("rejected");
    }

    [Fact]
    public async Task ContentApproveTool_RefusesNonDraftItem()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var published = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        PrepareApproved(published);
        published.MarkScheduled(Now.AddMinutes(4));
        published.MarkPublished(Now.AddMinutes(5));
        fx.Db.ContentItems.Add(published);
        await fx.Db.SaveChangesAsync();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = published.Id.ToString(), ["decision"] = "approve" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "reviewer"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("published");
    }

    [Fact]
    public async Task ContentScheduleTool_CreatesGoldenHourIntent_IgnoringCallerTime()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "zalo", "body", createdBy: null, Now);
        PrepareApproved(item);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var golden = new DefaultGoldenHourResolver();
        var expected = golden.ResolveNext("zalo", Now);
        var sut = new ContentScheduleTool(
            fx.Db,
            new FixedClock(Now),
            new ContentAutoScheduler(fx.Db, golden));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string>
            {
                ["content_id"] = item.Id.ToString(),
                // Caller-controlled time must be ignored by autonomous tool (Phase 3.10).
                ["scheduled_at"] = Now.AddDays(10).ToString("o"),
            },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "reviewer", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var savedItem = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        savedItem.Status.Should().Be("scheduled");
        var schedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        schedule.ContentItemId.Should().Be(item.Id);
        schedule.ContentRevision.Should().Be(item.ContentRevision);
        schedule.Platform.Should().Be("zalo");
        schedule.ScheduledAt.Should().Be(expected);
        schedule.ApprovalMode.Should().Be(item.ApprovalMode);
        schedule.PublishingPolicyVersionApplied.Should().Be(item.PublishingPolicyVersionApplied);
    }

    [Theory]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    public async Task ContentScheduleTool_RejectsLegacyPlatformsWithoutCreatingSchedule(string platform)
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, platform, "body", createdBy: null, Now);
        PrepareApproved(item);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentScheduleTool(
            fx.Db,
            new FixedClock(Now),
            new ContentAutoScheduler(fx.Db, new DefaultGoldenHourResolver()));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("content.platform_unsupported");
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("approved");
    }

    [Fact]
    public async Task ContentScheduleTool_RefusesNonApprovedItem()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentScheduleTool(
            fx.Db,
            new FixedClock(Now),
            new ContentAutoScheduler(fx.Db, new DefaultGoldenHourResolver()));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content_current_revision_not_schedulable");
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ContentPublishTool_Queues_existing_schedule_without_calling_provider()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        PrepareApproved(item);
        item.MarkScheduled(Now.AddMinutes(4));
        var schedule = ContentSchedule.Schedule(
            fx.TenantId,
            item.Id,
            item.ContentRevision,
            item.Platform,
            Now.AddDays(1),
            Now.AddMinutes(4));
        schedule.SetApprovalContext(
            item.ApprovalMode!,
            item.PublishingPolicyVersionApplied!.Value,
            publishTargetId: null);
        schedule.MarkHeld("manual_hold", Now.AddMinutes(5));
        fx.Db.ContentItems.Add(item);
        fx.Db.ContentSchedules.Add(schedule);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentPublishTool(fx.Db, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "publisher", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("scheduled");
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync()).Status
            .Should().Be(ContentSchedule.StatusPending);
    }

    [Fact]
    public async Task ContentPublishTool_RefusesDraftItem()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentPublishTool(fx.Db, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content_current_revision_not_publishable");
    }

    [Fact]
    public async Task ContentPublishTool_requires_current_revision_schedule()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        PrepareApproved(item);
        item.MarkScheduled(Now.AddMinutes(4));
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentPublishTool(fx.Db, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "publisher", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content_schedule_required");
    }

    [Fact]
    public async Task ContentScheduleTool_RefusesLegacyApproveWithoutPublishingApproval()
    {
        // Publishing approval fields are required; legacy Approve alone cannot schedule.
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.Approve(Guid.NewGuid(), Now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentScheduleTool(
            fx.Db,
            new FixedClock(Now),
            new ContentAutoScheduler(fx.Db, new DefaultGoldenHourResolver()));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "publisher", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content_current_revision_not_schedulable");
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ContentPublishTool_RefusesUnreviewedItem_Unconditionally()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.Approve(Guid.NewGuid(), Now); // human approved only
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentPublishTool(fx.Db, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "publisher", CanPublishContent: true),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content_current_revision_not_publishable");
    }

    [Fact]
    public async Task ContentApproveTool_RefusesSelfApproval_ByCreatorAgent()
    {
        // Review-gate P1 (separation of duties): the generating agent cannot sign off its own content.
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var creatorDefId = Guid.NewGuid();
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now, createdByAgentId: creatorDefId);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = BuildApproveTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString(), ["decision"] = "approve" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: creatorDefId, AgentCode: "content-agent"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("reviewer_independence");
        (await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync()).Status.Should().Be("draft");
    }

    [Fact]
    public async Task ContentGenerateTool_StampsCreatingAgentDefinitionId()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var creatorDefId = Guid.NewGuid();
        var sut = BuildGenerateTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = "facebook", ["brief"] = "HSK launch" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: creatorDefId, AgentCode: "content-agent"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.CreatedByAgentId.Should().Be(creatorDefId);
        saved.ContentRevision.Should().Be(1);
        saved.Status.Should().Be("draft");

        var reviewTask = await fx.Db.ContentReviewTasks.IgnoreQueryFilters().SingleAsync();
        reviewTask.TenantId.Should().Be(fx.TenantId);
        reviewTask.ContentItemId.Should().Be(saved.Id);
        reviewTask.ContentRevision.Should().Be(1);
        reviewTask.Status.Should().Be(ContentReviewTask.StatusPending);
        reviewTask.NextAttemptAt.Should().Be(Now);
        reviewTask.CreatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task ContentGenerateTool_RequiresAgentDefinitionId()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var sut = BuildGenerateTool(fx);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["platform"] = "facebook", ["brief"] = "HSK launch" },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: null, AgentCode: "content-agent"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("content_generator_agent_required");
        (await fx.Db.ContentItems.IgnoreQueryFilters().CountAsync()).Should().Be(0);
        (await fx.Db.ContentReviewTasks.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    private static void PrepareApproved(ContentItem item)
    {
        item.BeginAgentReview(item.ContentRevision, Now.AddMinutes(1));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: Guid.NewGuid(),
            reason: "passed",
            at: Now.AddMinutes(2));
        item.ApproveAutomatically(
            item.ContentRevision,
            ContentItem.PublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            at: Now.AddMinutes(3));
    }

    private static ContentGenerateTool BuildGenerateTool(AgentServiceTestAppDb fx)
    {
        var agent = FakeContentAgent();
        return new ContentGenerateTool(agent, fx.Db, new FixedClock(Now));
    }

    private static ContentApproveTool BuildApproveTool(AgentServiceTestAppDb fx) =>
        new(fx.Db, new FixedClock(Now));

    private static CoreContent.ContentAgent FakeContentAgent()
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>()).Returns([]);
        var templates = Substitute.For<CoreContent.IPromptTemplateProvider>();
        templates.GetTemplate(Arg.Any<string>()).Returns(ci => $"{ci.ArgAt<string>(0)}: Brief={{{{brief}}}}");
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ClaudeReply($"Draft for {call.ArgAt<string>(2)}", 11, 7, 0m, "content-model"));
        return new CoreContent.ContentAgent(rag, templates, claude, new LlmCallScope());
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
