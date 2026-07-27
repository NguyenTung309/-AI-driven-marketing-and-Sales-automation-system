using System.Text.Json;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
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
    public string InputSchemaJson => """{"platform":"facebook|instagram|zalo","brief":"text","brief_id?":"guid","kb_module_code?":"string"}""";
    public string RequiredPermission => "content:write";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        var tenantId = ctx.TenantId;
        var requestedPlatform = ContentToolArgs.String(args, "platform");
        var brief = ContentToolArgs.String(args, "brief");
        if (string.IsNullOrWhiteSpace(requestedPlatform) || string.IsNullOrWhiteSpace(brief))
            return ToolResult.Fail("platform and brief are required.");
        if (!ContentPlatformCatalog.TryNormalizeWritable(requestedPlatform, out var platform))
            return ToolResult.Fail("unsupported platform; expected facebook, instagram, or zalo.");

        // EARS[WHEN dry-run is on THE SYSTEM SHALL preview the intended generation without persisting a draft]
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would generate + persist draft with args {JsonSerializer.Serialize(args, JsonOpts)}");
        if (ctx.AgentDefinitionId is null || ctx.AgentDefinitionId == Guid.Empty)
            return ToolResult.Fail(ContentGenerationPersistence.GeneratorAgentRequired);

        var briefId = ContentToolArgs.OptionalGuid(args, "brief_id");
        var kbModuleCode = ContentToolArgs.OptionalString(args, "kb_module_code");

        ContentDraftResult draft;
        try
        {
            draft = await _agent.GenerateAsync(
                new ContentGenerateRequest(tenantId, briefId, platform!, brief, kbModuleCode), ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

        // EARS[WHEN the content tool generates a draft THE SYSTEM SHALL persist it as a ContentItem(draft) so the
        // reviewer/publish loop can act on a stored item, not just returned text]
        // createdByAgentId: reviewer-independence check needs to know which agent generated the item.
        // Phase 2.4: also enqueue one immediately-due durable review task in the same transaction.
        var now = _clock.UtcNow;
        var item = ContentItem.Create(
            tenantId,
            draft.Platform,
            draft.Body,
            createdBy: null,
            now,
            briefId: draft.BriefId,
            createdByAgentId: ctx.AgentDefinitionId);
        _db.ContentItems.Add(item);
        _db.ContentReviewTasks.Add(
            ContentGenerationPersistence.CreateImmediateReviewTask(
                tenantId,
                item.Id,
                item.ContentRevision,
                now));
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

// Phase 3.10: content.publish only transitions durable schedule state for ContentPublishJob.
// Never calls ISocialPublisher inline — external delivery is claim/attempt only.
public sealed class ContentPublishTool(
    AppDbContext db,
    IClock clock) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;

    public string Name => "content.publish";
    public string Description => "Queue the current revision's existing schedule for the durable publisher. Never calls a social provider inline. Args: content_id.";
    public string InputSchemaJson => """{"content_id":"guid"}""";
    public string RequiredPermission => "content:publish";
    // SPEC-16 P4-4: publishing is irreversible + outward-facing → High-risk, pauses for approval when tenant toggle on.
    public ToolRiskLevel RiskLevel => ToolRiskLevel.High;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        if (!ctx.CanPublishContent)
            return ToolResult.Fail("content_publish_permission_required");
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would queue content {ContentToolArgs.String(args, "content_id")}");

        var contentId = ContentToolArgs.OptionalGuid(args, "content_id");
        if (contentId is null)
            return ToolResult.Fail("content_id is required.");

        var item = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == ctx.TenantId && i.Id == contentId.Value && i.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return ToolResult.Fail($"content item {contentId} not found for tenant.");
        if (!item.CanPublishCurrentRevision())
            return ToolResult.Fail("content_current_revision_not_publishable");

        var schedules = await _db.ContentSchedules
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == ctx.TenantId
                && s.ContentItemId == item.Id
                && s.ContentRevision == item.ContentRevision
                && (s.Status == ContentSchedule.StatusPending
                    || s.Status == ContentSchedule.StatusHeld
                    || s.Status == ContentSchedule.StatusFailed))
            .ToListAsync(ct).ConfigureAwait(false);
        var schedule = schedules.OrderByDescending(s => s.CreatedAt).FirstOrDefault();
        if (schedule is null)
            return ToolResult.Fail("content_schedule_required");
        if (schedule.RequiresInstagramTargetReselection())
            return ToolResult.Fail(ContentSchedule.ErrorInstagramTargetReselectionRequired);

        if (schedule.Status != ContentSchedule.StatusPending
            && !schedule.TryResetForRetry(clock.UtcNow))
        {
            return ToolResult.Fail("content_schedule_not_retryable");
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            content_id = item.Id,
            schedule_id = schedule.Id,
            status = schedule.Status,
        }, JsonOpts));
    }
}

// Phase 3.10: autonomous content.schedule no longer accepts caller-controlled times.
// Delegates to ContentAutoScheduler golden-hour intent (same path as approval routing).
public sealed class ContentScheduleTool(
    AppDbContext db,
    IClock clock,
    Clawbot.Infrastructure.Content.IContentAutoScheduler autoScheduler) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly Clawbot.Infrastructure.Content.IContentAutoScheduler _autoScheduler = autoScheduler;

    public string Name => "content.schedule";
    public string Description =>
        "Create the revision-bound golden-hour schedule intent for an approved content item. Args: content_id. Caller-controlled scheduled_at is ignored for autonomous tools.";
    public string InputSchemaJson => """{"content_id":"guid"}""";
    public string RequiredPermission => "content:publish";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.High;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        if (!ctx.CanPublishContent)
            return ToolResult.Fail("content_publish_permission_required");
        if (ctx.DryRun)
            return ToolResult.Ok($"[dry-run] would create auto-schedule intent for content {ContentToolArgs.String(args, "content_id")}");

        var contentId = ContentToolArgs.OptionalGuid(args, "content_id");
        if (contentId is null)
            return ToolResult.Fail("content_id is required.");

        var item = await _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == ctx.TenantId && i.Id == contentId.Value && i.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (item is null)
            return ToolResult.Fail($"content item {contentId} not found for tenant.");
        if (!ContentPlatformCatalog.TryNormalizeWritable(item.Platform, out _))
            return ToolResult.Fail("content.platform_unsupported");

        try
        {
            var schedule = await _autoScheduler.CreateIntentAsync(
                item,
                publishTargetId: null,
                _clock.UtcNow,
                cancellationToken: ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ToolResult.Ok(JsonSerializer.Serialize(new
            {
                schedule_id = schedule.Id,
                content_id = item.Id,
                status = item.Status,
                schedule_status = schedule.Status,
                scheduled_at = schedule.ScheduledAt,
            }, JsonOpts));
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }
}

public sealed class ContentApproveTool(
    AppDbContext db,
    IClock clock) : IAgentTool
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    // Phase 4.9: canonical name content.review; legacy content.approve still resolves via ToolRegistry alias.
    public string Name => "content.review";
    public string Description =>
        "Request durable Agent review or apply a non-publishing agent signoff/reject on a draft. Never schedules or publishes. Args: content_id, decision (approve|reject), optional reason.";
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

        // Phase 0 review-gate: 'approved' (human) items stay re-reviewable so the reviewer agent can add
        // the ApprovedByAgentId signoff that Phase 1 enforces as the publish precondition.
        if (!string.Equals(item.Status, "draft", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return ToolResult.Fail($"content item is '{item.Status}', only draft or approved items can be reviewed.");

        var now = _clock.UtcNow;
        if (decision == "approve")
        {
            // EARS[WHEN approving THE SYSTEM SHALL require an agent identity (ctx.AgentDefinitionId) and attribute the approval to it]
            if (ctx.AgentDefinitionId is null || ctx.AgentDefinitionId == Guid.Empty)
                return ToolResult.Fail("agent identity is required to approve on behalf of an agent.");
            // Review-gate P1 (separation of duties): the generating agent cannot sign off its own content.
            if (item.CreatedByAgentId is not null && item.CreatedByAgentId == ctx.AgentDefinitionId)
                return ToolResult.Fail("reviewer_independence: the agent that generated this item cannot approve it; a different reviewer agent must sign off.");
            item.ApproveByAgent(ctx.AgentDefinitionId.Value, now);
        }
        else
        {
            item.Reject(now, reason);
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

