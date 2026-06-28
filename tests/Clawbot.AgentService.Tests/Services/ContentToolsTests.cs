using System.Text.Json;
using Clawbot.AgentService.Services;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Domain.Content;
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
    public async Task ContentApproveTool_RefusesNonDraftItem()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var published = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        published.MarkPublished(Now);
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
    public async Task ContentScheduleTool_SchedulesApprovedItem_AndCreatesScheduleRow()
    {
        // EARS[WHEN scheduling an approved item THE SYSTEM SHALL mark it scheduled and create a ContentSchedule row]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.ApproveByAgent(Guid.NewGuid(), Now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentScheduleTool(fx.Db, new FixedClock(Now));
        var scheduledAt = Now.AddDays(2);

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString(), ["scheduled_at"] = scheduledAt.ToString("o") },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "reviewer"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var savedItem = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        savedItem.Status.Should().Be("scheduled");
        var schedule = await fx.Db.ContentSchedules.IgnoreQueryFilters().SingleAsync();
        schedule.ContentItemId.Should().Be(item.Id);
        schedule.Platform.Should().Be("facebook");
        schedule.ScheduledAt.Should().Be(scheduledAt);
    }

    [Fact]
    public async Task ContentScheduleTool_RefusesNonApprovedItem()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var sut = new ContentScheduleTool(fx.Db, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString(), ["scheduled_at"] = Now.AddDays(2).ToString("o") },
            new ToolContext(fx.TenantId, "task-1"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("draft");
        (await fx.Db.ContentSchedules.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ContentPublishTool_PublishesApprovedItem_AndMarksPublished()
    {
        // EARS[WHEN publishing an approved item THE SYSTEM SHALL call the publisher and mark the item published on success]
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.ApproveByAgent(Guid.NewGuid(), Now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<Clawbot.Infrastructure.Content.Publishing.ISocialPublisher>();
        publisher.PublishAsync(Arg.Any<Clawbot.Infrastructure.Content.Publishing.PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Clawbot.Infrastructure.Content.Publishing.PublishResult(true, "https://fb/post/1", null));
        var sut = new ContentPublishTool(fx.Db, publisher, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "publisher"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be("published");
        await publisher.Received(1).PublishAsync(
            Arg.Is<Clawbot.Infrastructure.Content.Publishing.PublishRequest>(r => r.ContentItemId == item.Id && r.Platform == "facebook"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentPublishTool_RefusesDraftItem()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var draft = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        fx.Db.ContentItems.Add(draft);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<Clawbot.Infrastructure.Content.Publishing.ISocialPublisher>();
        var sut = new ContentPublishTool(fx.Db, publisher, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = draft.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("draft");
        await publisher.DidNotReceive().PublishAsync(Arg.Any<Clawbot.Infrastructure.Content.Publishing.PublishRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ContentPublishTool_SurfacesPublisherFailure()
    {
        using var fx = new AgentServiceTestAppDb(Guid.NewGuid());
        var item = ContentItem.Create(fx.TenantId, "facebook", "body", createdBy: null, Now);
        item.ApproveByAgent(Guid.NewGuid(), Now);
        fx.Db.ContentItems.Add(item);
        await fx.Db.SaveChangesAsync();
        var publisher = Substitute.For<Clawbot.Infrastructure.Content.Publishing.ISocialPublisher>();
        publisher.PublishAsync(Arg.Any<Clawbot.Infrastructure.Content.Publishing.PublishRequest>(), Arg.Any<CancellationToken>())
            .Returns(new Clawbot.Infrastructure.Content.Publishing.PublishResult(false, null, "facebook_not_configured"));
        var sut = new ContentPublishTool(fx.Db, publisher, new FixedClock(Now));

        var result = await sut.InvokeAsync(
            new Dictionary<string, string> { ["content_id"] = item.Id.ToString() },
            new ToolContext(fx.TenantId, "task-1", AgentDefinitionId: Guid.NewGuid(), AgentCode: "publisher"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("publish_failed");
        result.Error.Should().Contain("facebook_not_configured");
        var saved = await fx.Db.ContentItems.IgnoreQueryFilters().SingleAsync();
        saved.Status.Should().Be("approved"); // unchanged on failure
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
