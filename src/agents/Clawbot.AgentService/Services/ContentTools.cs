using System.Data;
using System.Globalization;
using System.Text.Json;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Integrations.Meta;
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

    public static int? OptionalInt(IReadOnlyDictionary<string, string> args, string key) =>
        args.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

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
            return ToolResult.Ok($"[dry-run] would generate + persist draft with args {JsonSerializer.Serialize(args, AgentJson.Options)}");
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
        // reviewer/publish loop can act on a stored item, not just returned text]. The LLM call intentionally happens
        // before this short transaction; the session lock prevents an old plan generation from inserting after replan.
        try
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);
            await OrchestrationSessionGenerationFence.EnsureCurrentAsync(_db, ctx, ct)
                .ConfigureAwait(false);

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
                createdByAgentId: ctx.AgentDefinitionId,
                orchestrationSessionId: ctx.SessionId,
                orchestrationPlanGeneration: ctx.OrchestrationPlanGeneration);
            _db.ContentItems.Add(item);
            _db.ContentReviewTasks.Add(
                ContentGenerationPersistence.CreateImmediateReviewTask(
                    tenantId,
                    item.Id,
                    item.ContentRevision,
                    now));
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);

            return ToolResult.Ok(JsonSerializer.Serialize(new
            {
                content_id = item.Id,
                platform = item.Platform,
                body = item.Body,
                status = item.Status,
            }, AgentJson.Options));
        }
        catch (OrchestrationPlanGenerationMismatchException)
        {
            return ToolResult.Fail("orchestration_plan_superseded");
        }
    }
}

// Phase 3.10: content.publish only transitions durable schedule state for ContentPublishJob.
// Never calls ISocialPublisher inline — external delivery is claim/attempt only.
public sealed class ContentPublishTool(
    AppDbContext db,
    IClock clock) : IAgentTool
{
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

        try
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);
            await OrchestrationSessionGenerationFence.EnsureCurrentAsync(_db, ctx, ct)
                .ConfigureAwait(false);

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
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return ToolResult.Ok(JsonSerializer.Serialize(new
            {
                content_id = item.Id,
                schedule_id = schedule.Id,
                status = schedule.Status,
            }, AgentJson.Options));
        }
        catch (OrchestrationPlanGenerationMismatchException)
        {
            return ToolResult.Fail("orchestration_plan_superseded");
        }
    }
}

// Phase 3.10: autonomous content.schedule no longer accepts caller-controlled times.
// Delegates to ContentAutoScheduler golden-hour intent (same path as approval routing).
public sealed class ContentScheduleTool(
    AppDbContext db,
    IClock clock,
    Clawbot.Infrastructure.Content.IContentAutoScheduler autoScheduler,
    IMetaIntegrationService? metaIntegrations = null) : IAgentTool
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;
    private readonly Clawbot.Infrastructure.Content.IContentAutoScheduler _autoScheduler = autoScheduler;
    private readonly IMetaIntegrationService? _metaIntegrations = metaIntegrations;

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

        try
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);
            await OrchestrationSessionGenerationFence.EnsureCurrentAsync(_db, ctx, ct)
                .ConfigureAwait(false);

            var item = await _db.ContentItems
                .IgnoreQueryFilters()
                .Where(i => i.TenantId == ctx.TenantId && i.Id == contentId.Value && i.DeletedAt == null)
                .FirstOrDefaultAsync(ct).ConfigureAwait(false);
            if (item is null)
                return ToolResult.Fail($"content item {contentId} not found for tenant.");
            if (!ContentPlatformCatalog.TryNormalizeWritable(item.Platform, out _))
                return ToolResult.Fail("content.platform_unsupported");

            var schedule = await _autoScheduler.CreateIntentAsync(
                item,
                publishTargetId: await ResolveDefaultFacebookTargetAsync(item, ct).ConfigureAwait(false),
                _clock.UtcNow,
                cancellationToken: ct).ConfigureAwait(false);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return ToolResult.Ok(JsonSerializer.Serialize(new
            {
                schedule_id = schedule.Id,
                content_id = item.Id,
                status = item.Status,
                schedule_status = schedule.Status,
                scheduled_at = schedule.ScheduledAt,
            }, AgentJson.Options));
        }
        catch (OrchestrationPlanGenerationMismatchException)
        {
            return ToolResult.Fail("orchestration_plan_superseded");
        }
        catch (InvalidOperationException ex)
        {
            return ToolResult.Fail(ex.Message);
        }
    }

    private async Task<Guid?> ResolveDefaultFacebookTargetAsync(ContentItem item, CancellationToken ct)
    {
        if (_metaIntegrations is null
            || !string.Equals(item.Platform, "facebook", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var pages = await _metaIntegrations.GetPublishablePagesAsync(item.TenantId, ct).ConfigureAwait(false);
            var page = pages.FirstOrDefault(x => x.IsDefault) ?? (pages.Count > 0 ? pages[0] : null);
            return page?.Id;
        }
        catch
        {
            return null;
        }
    }
}

// Reviewer-agent chỉ có content.review — tool đó cần content_id cụ thể và trước đây không tool nào liệt kê được
// bài đang chờ, nên mọi lượt review đều kết thúc bằng "cần cung cấp content_id". Tool này là bước tra cứu còn thiếu:
// read-only, tenant-scoped, trả về đúng các trường reviewer cần để quyết định.
public sealed class ContentListTool(AppDbContext db) : IAgentTool
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 50;
    private const int BodyPreviewLength = 400;
    private const int ScanLimit = 200;

    private readonly AppDbContext _db = db;

    public string Name => "content.list";
    public string Description =>
        "List this tenant's content items with their workflow state. Call this first to obtain content_id values for content.review.";

    public string InputSchemaJson => """
        {"type":"object","properties":{"workflow_state":{"type":"string","enum":["awaiting_agent_review","agent_review_running","agent_review_non_pass","review_failed","awaiting_human_approval","approved_awaiting_schedule","scheduled","published","rejected"],"description":"Lọc theo trạng thái quy trình. Bỏ trống = mọi bài chưa publish"},"platform":{"type":"string","description":"facebook|zalo|tiktok|website"},"limit":{"type":"integer","minimum":1,"maximum":50,"default":20}},"additionalProperties":false}
        """;

    public string RequiredPermission => "content:read";
    public ToolRiskLevel RiskLevel => ToolRiskLevel.Low;

    public async Task<ToolResult> InvokeAsync(IReadOnlyDictionary<string, string> args, ToolContext ctx, CancellationToken ct)
    {
        var workflowState = ContentToolArgs.OptionalString(args, "workflow_state")?.ToLowerInvariant();
        var platform = ContentToolArgs.OptionalString(args, "platform");
        var limit = Math.Clamp(ContentToolArgs.OptionalInt(args, "limit") ?? DefaultLimit, 1, MaxLimit);

        var query = _db.ContentItems
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == ctx.TenantId && i.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(i => i.Platform == platform);

        // workflow_state là giá trị suy ra (không có cột) nên phải lọc trong bộ nhớ. Chặn ScanLimit để một
        // tenant nhiều bài không kéo cả bảng về; sắp xếp mới nhất trước để lô quét luôn là phần đáng review.
        var candidates = await query
            .OrderByDescending(i => i.UpdatedAt)
            .Take(ScanLimit)
            .ToListAsync(ct).ConfigureAwait(false);

        var items = candidates
            .Select(i => new { Item = i, State = i.ResolveWorkflowState() })
            .Where(x => workflowState is null
                ? x.State != "published"
                : string.Equals(x.State, workflowState, StringComparison.Ordinal))
            .Take(limit)
            .Select(x => new
            {
                content_id = x.Item.Id,
                platform = x.Item.Platform,
                status = x.Item.Status,
                workflow_state = x.State,
                content_revision = x.Item.ContentRevision,
                agent_review_status = x.Item.AgentReviewStatus,
                agent_review_reason = x.Item.AgentReviewReason,
                created_by_agent_id = x.Item.CreatedByAgentId,
                updated_at = x.Item.UpdatedAt,
                body_preview = Preview(x.Item.Body),
            })
            .ToList();

        return ToolResult.Ok(JsonSerializer.Serialize(new
        {
            workflow_state = workflowState ?? "any_unpublished",
            total = items.Count,
            scanned = candidates.Count,
            items,
        }, AgentJson.Options));
    }

    private static string Preview(string? body)
    {
        var text = (body ?? string.Empty).Trim();
        return text.Length <= BodyPreviewLength ? text : text[..BodyPreviewLength] + "…";
    }
}

public sealed class ContentApproveTool(
    AppDbContext db,
    IClock clock) : IAgentTool
{
    private readonly AppDbContext _db = db;
    private readonly IClock _clock = clock;

    // Phase 4.9: canonical name content.review; legacy content.approve still resolves via ToolRegistry alias.
    public string Name => "content.review";
    // Mô tả cũ hứa "request durable Agent review" nhưng tool ghi thẳng verdict, không hề xếp hàng ContentReviewTask.
    // Nói đúng việc nó làm, và chỉ rõ phải lấy content_id từ content.list trước.
    public string Description =>
        "Record a non-publishing reviewer verdict (signoff or reject) on a draft. Never schedules or publishes. REQUIRES a concrete content_id — call content.list first to find one.";

    public string InputSchemaJson => """
        {"type":"object","properties":{"content_id":{"type":"string","format":"uuid","description":"Lấy từ tool content.list"},"decision":{"type":"string","enum":["approve","reject"]},"reason":{"type":"string","description":"Lý do, bắt buộc trên thực tế khi reject"}},"required":["content_id","decision"],"additionalProperties":false}
        """;
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

        try
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(IsolationLevel.Serializable, ct)
                .ConfigureAwait(false);
            await OrchestrationSessionGenerationFence.EnsureCurrentAsync(_db, ctx, ct)
                .ConfigureAwait(false);

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
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return ToolResult.Ok(JsonSerializer.Serialize(new
            {
                content_id = item.Id,
                status = item.Status,
                approved_by_agent_id = item.ApprovedByAgentId,
                reason,
            }, AgentJson.Options));
        }
        catch (OrchestrationPlanGenerationMismatchException)
        {
            return ToolResult.Fail("orchestration_plan_superseded");
        }
    }
}

