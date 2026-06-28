using System.Text.Json;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

// Shared arg parsing for the content tools (DRY: both tools read the same typed args from the string dictionary).
internal static class ContentToolArgs
{
    public static string String(IReadOnlyDictionary<string, string> args, string key) =>
        args.TryGetValue(key, out var v) ? v ?? string.Empty : string.Empty;

    public static string? OptionalString(IReadOnlyDictionary<string, string> args, string key) =>
        args.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public static Guid? OptionalGuid(IReadOnlyDictionary<string, string> args, string key)
    {
        if (!args.TryGetValue(key, out var v) || !Guid.TryParse(v, out var g)) return null;
        return g == Guid.Empty ? null : g;
    }
}

// SPEC-16 P2-5: content tool that PERSISTS the draft as a ContentItem (the orchestration adapter path previously
// only generated text without storing). Lives in AgentService (needs AppDbContext; Agents.Core cannot ref Infrastructure).
// Registered as an explicit IAgentTool so it overrides the text-only adapter-wrapped "content-agent" tool by name.
public sealed class ContentGenerateTool(
    ContentAgent agent,
    AppDbContext db,
    IClock clock) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly ContentAgent _agent = agent;
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public string Name => "content-agent";
    public string Description => "Generate and persist a content draft (ContentItem, status=draft) for a platform from a brief. Returns {content_id, platform, body, status}.";
    public string InputSchemaJson => """{"platform":"facebook|instagram|tiktok|youtube|zalo","brief":"text","brief_id?":"guid","kb_module_code?":"string"}""";
    public string RequiredPermission => "content:write";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        // EARS[WHEN dry-run is on THE SYSTEM SHALL preview the intended generation without persisting a draft]
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would generate + persist draft with args {JsonSerializer.Serialize(args, JsonOpts)}");

        var tenantId = ctx.TenantId;
        var platform = ContentToolArgs.String(args, "platform");
        var brief = ContentToolArgs.String(args, "brief");
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(brief))
            return ToolResult.Fail("platform and brief are required.");

        var briefId = ContentToolArgs.OptionalGuid(args, "brief_id");
        var kbModuleCode = ContentToolArgs.OptionalString(args, "kb_module_code");

        ContentDraftResult draft;
        try
        {
            draft = await _agent.GenerateAsync(
                new ContentGenerateRequest(tenantId, briefId, platform, brief, kbModuleCode), ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

        // EARS[WHEN the content tool generates a draft THE SYSTEM SHALL persist it as a ContentItem(draft) so the
        // reviewer/publish loop can act on a stored item, not just returned text]
        var item = ContentItem.Create(tenantId, draft.Platform, draft.Body, createdBy: null, _clock.UtcNow, briefId: draft.BriefId);
        _db.ContentItems.Add(item);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            content_id = item.Id,
            platform = item.Platform,
            body = item.Body,
            status = item.Status,
        }, JsonOpts));
    }
}

// SPEC-16 P2-7/P2-9: publish tool — directly publishes an approved/scheduled content item via ISocialPublisher
// (GraphSocialPublisher in 2C). Risk-gate (publish behind Tenant.RequireOrchestrationApproval) is Phase 4 (P4-4);
// this tool does the publish half so an autonomous run can publish immediately when approval is not required.
public sealed class ContentPublishTool(
    AppDbContext db,
    ISocialPublisher publisher,
    IClock clock) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly ISocialPublisher _publisher = publisher;
    private readonly IClock _clock = clock;

    public string Name => "content.publish";
    public string Description => "Publish an approved content item now via the social publisher (FB/Zalo Graph). Args: content_id. Returns {content_id, status, post_url?}.";
    public string InputSchemaJson => """{"content_id":"guid"}""";
    public string RequiredPermission => "content:write";
    // SPEC-16 P4-4: publishing is irreversible + outward-facing → High-risk, pauses for approval when tenant toggle on.
    public ToolRiskLevel RiskLevel => ToolRiskLevel.High;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        // EARS[WHEN dry-run is on THE SYSTEM SHALL preview the publish without calling the social publisher]
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would publish content {ContentToolArgs.String(args, "content_id")}");

        var contentId = ContentToolArgs.OptionalGuid(args, "content_id");
        if (contentId is null)
            return ToolResult.Fail("content_id is required.");

        var item = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == ctx.TenantId && i.Id == contentId.Value && i.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return ToolResult.Fail($"content item {contentId} not found for tenant.");

        // EARS[WHEN publishing THE SYSTEM SHALL require the item to be approved (no publishing drafts/rejected items)]
        if (!string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            return ToolResult.Fail($"content item is '{item.Status}', only approved or scheduled items can be published.");

        var now = _clock.UtcNow;
        // EARS[WHEN publishing THE SYSTEM SHALL call the social publisher and mark the item published on success]
        var result = await _publisher.PublishAsync(
            new PublishRequest(ctx.TenantId, item.Id, item.Platform, item.Body, item.AssetsJson, now), ct).ConfigureAwait(false);

        if (!result.Success)
            return ToolResult.Fail($"publish_failed: {result.Error}");

        item.MarkPublished(now);
        // If a schedule exists for this item, mark it posted too so the publish job doesn't retry.
        var schedule = await _db.ContentSchedules
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == ctx.TenantId && s.ContentItemId == item.Id && s.Status == "pending")
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        schedule?.MarkPosted(result.PostUrl ?? string.Empty, now);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            content_id = item.Id,
            status = item.Status,
            post_url = result.PostUrl,
        }, JsonOpts));
    }
}

public sealed class ContentScheduleTool(
    AppDbContext db,
    IClock clock) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public string Name => "content.schedule";
    public string Description => "Schedule an approved content item for publishing. Args: content_id, scheduled_at (ISO-8601). Returns {schedule_id, content_id, status, scheduled_at}.";
    public string InputSchemaJson => """{"content_id":"guid","scheduled_at":"ISO-8601 datetime"}""";
    public string RequiredPermission => "content:write";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        // EARS[WHEN dry-run is on THE SYSTEM SHALL preview the schedule without creating a row]
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would schedule content {ContentToolArgs.String(args, "content_id")} at {ContentToolArgs.String(args, "scheduled_at")}");

        var contentId = ContentToolArgs.OptionalGuid(args, "content_id");
        if (contentId is null)
            return ToolResult.Fail("content_id is required.");
        var scheduledAtRaw = ContentToolArgs.OptionalString(args, "scheduled_at");
        if (scheduledAtRaw is null || !DateTimeOffset.TryParse(scheduledAtRaw, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var scheduledAt))
            return ToolResult.Fail("scheduled_at is required and must be a valid ISO-8601 datetime.");

        var item = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == ctx.TenantId && i.Id == contentId.Value && i.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return ToolResult.Fail($"content item {contentId} not found for tenant.");

        // EARS[WHEN scheduling THE SYSTEM SHALL require the item to be approved first (no scheduling drafts/rejected items)]
        if (!string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return ToolResult.Fail($"content item is '{item.Status}', only approved items can be scheduled.");

        var now = _clock.UtcNow;
        item.MarkScheduled(now);
        var schedule = ContentSchedule.Schedule(ctx.TenantId, item.Id, item.Platform, scheduledAt, now);
        _db.ContentSchedules.Add(schedule);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            schedule_id = schedule.Id,
            content_id = item.Id,
            status = item.Status,
            scheduled_at = schedule.ScheduledAt,
        }, JsonOpts));
    }
}

public sealed class ContentApproveTool(
    AppDbContext db,
    IClock clock) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    public string Name => "content.approve";
    public string Description => "Approve or reject a draft content item (reviewer action). Args: content_id, decision (approve|reject), optional reason.";
    public string InputSchemaJson => """{"content_id":"guid","decision":"approve|reject","reason?":"text"}""";
    public string RequiredPermission => "content:write";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        // EARS[WHEN dry-run is on THE SYSTEM SHALL preview the approve/reject without changing status]
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would {ContentToolArgs.String(args, "decision")} content {ContentToolArgs.String(args, "content_id")}");

        var contentId = ContentToolArgs.OptionalGuid(args, "content_id");
        if (contentId is null)
            return ToolResult.Fail("content_id is required.");
        var decision = ContentToolArgs.String(args, "decision").Trim().ToLowerInvariant();
        if (decision is not ("approve" or "reject"))
            return ToolResult.Fail("decision must be 'approve' or 'reject'.");
        var reason = ContentToolArgs.OptionalString(args, "reason");

        var item = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == ctx.TenantId && i.Id == contentId.Value && i.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return ToolResult.Fail($"content item {contentId} not found for tenant.");

        if (!string.Equals(item.Status, "draft", StringComparison.OrdinalIgnoreCase))
            return ToolResult.Fail($"content item is '{item.Status}', only draft items can be approved/rejected.");

        var now = _clock.UtcNow;
        if (decision == "approve")
        {
            // EARS[WHEN approving THE SYSTEM SHALL require an agent identity (ctx.AgentDefinitionId) and attribute the approval to it]
            if (ctx.AgentDefinitionId is null || ctx.AgentDefinitionId == Guid.Empty)
                return ToolResult.Fail("agent identity is required to approve on behalf of an agent.");
            item.ApproveByAgent(ctx.AgentDefinitionId.Value, now);
        }
        else
        {
            item.Reject(now);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            content_id = item.Id,
            status = item.Status,
            approved_by_agent_id = item.ApprovedByAgentId,
            reason,
        }, JsonOpts));
    }
}

