using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Clawbot.Agents.Contracts.Content;
using Clawbot.Agents.Contracts.Research;
using Clawbot.Agents.Core.Docs;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Content;
using Clawbot.Api.Jobs;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ContentEndpoints
{
    private const int MaxAssetsPerContentItem = 10;
    private const int MaxAssetUrlChars = 2048;
    private const long MaxAssetUploadBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAssetContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/gif",
        "image/jpeg",
        "image/png",
        "image/webp",
    };

    public static IEndpointRouteBuilder MapContent(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/content").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/briefs", ListBriefsAsync).RequirePermission("content:read");
        grp.MapGet("/briefs/{id:guid}", GetBriefAsync).RequirePermission("content:read");
        grp.MapPost("/briefs", CreateBriefAsync).RequirePermission("content:write");
        grp.MapPut("/briefs/{id:guid}", UpdateBriefAsync).RequirePermission("content:write");
        grp.MapDelete("/briefs/{id:guid}", DeleteBriefAsync).RequirePermission("content:write");

        grp.MapGet("/trends", TrendsAsync).RequirePermission("content:read");
        grp.MapPost("/trends/scan", ScanTrendsAsync).RequirePermission("content:write");
        ContentTrendSettingsEndpoints.Map(grp);

        grp.MapPost("/items/generate", GenerateItemAsync).RequirePermission("content:write");
        grp.MapPost("/image-prompts", GenerateImagePromptAsync).RequirePermission("content:write");
        grp.MapGet("/queue", QueueAsync).RequirePermission("content:read");
        grp.MapGet("/items", QueueAsync).RequirePermission("content:read");
        grp.MapGet("/items/{id:guid}", GetItemAsync).RequirePermission("content:read");
        grp.MapPut("/items/{id:guid}", UpdateItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/assets", UploadItemAssetAsync)
            .RequirePermission("content:write")
            .RequireRateLimiting(RateLimitingExtensions.UploadPolicy)
            .DisableAntiforgery();
        grp.MapDelete("/items/{id:guid}", DeleteItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/approve", ApproveItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/reject", RejectItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/schedule", ScheduleItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/repurpose", RepurposeItemAsync).RequirePermission("content:write");
        grp.MapGet("/calendar", CalendarAsync).RequirePermission("content:read");
        grp.MapGet("/publish-targets", PublishTargetsAsync).RequirePermission("content:read");
        grp.MapDelete("/schedule/{id:guid}", DeleteScheduleAsync).RequirePermission("content:write");

        return app;
    }

    private static async Task<IResult> ListBriefsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? status,
        [FromQuery] string? platform,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;
        var query = db.ContentBriefs.AsNoTracking()
            .Where(b => b.Status != "archived");
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(b => b.Platform == platform);

        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query
            .OrderByDescending(b => b.UpdatedAt)
            .ThenByDescending(b => b.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => ToDto(b))
            .ToListAsync(ct).ConfigureAwait(false);

        return Results.Ok(new { items = rows, total, page, pageSize });
    }

    private static async Task<IResult> GetBriefAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var brief = await db.ContentBriefs.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        return brief is null
            ? Error(http, StatusCodes.Status404NotFound, "content.brief_not_found", "Content brief not found.")
            : Results.Ok(ToDto(brief));
    }

    private static async Task<IResult> CreateBriefAsync(
        CreateContentBriefRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        ClaimsPrincipal user,
        HttpContext http,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Platform) || string.IsNullOrWhiteSpace(body.Brief))
            return Error(http, StatusCodes.Status400BadRequest, "content.brief_invalid", "platform and brief required.");

        var brief = ContentBrief.Create(
            tenant.TenantId, body.Platform.Trim(), body.Brief.Trim(), CurrentUserId(user), clock.UtcNow);
        db.ContentBriefs.Add(brief);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/content/briefs/{brief.Id}", ToDto(brief));
    }

    private static async Task<IResult> UpdateBriefAsync(
        Guid id,
        UpdateContentBriefRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Platform) || string.IsNullOrWhiteSpace(body.Brief))
            return Error(http, StatusCodes.Status400BadRequest, "content.brief_invalid", "platform and brief required.");

        var brief = await db.ContentBriefs.FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        if (brief is null)
            return Error(http, StatusCodes.Status404NotFound, "content.brief_not_found", "Content brief not found.");

        brief.Update(body.Platform.Trim(), body.Brief.Trim(), clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(brief));
    }

    private static async Task<IResult> DeleteBriefAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var brief = await db.ContentBriefs.FirstOrDefaultAsync(b => b.Id == id, ct).ConfigureAwait(false);
        if (brief is null)
            return Error(http, StatusCodes.Status404NotFound, "content.brief_not_found", "Content brief not found.");

        brief.MarkStatus("archived", clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    // Sinh nội dung chạy ngầm: validate + resolve brief tại đây (nhanh, lỗi trả 400 ngay),
    // phần gọi agent đẩy sang job — user nhận thông báo kèm link tới bài vừa sinh.
    private static async Task<IResult> GenerateItemAsync(
        GenerateContentItemRequest body,
        AppDbContext db,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        var resolved = await ResolveGenerateInputAsync(body, db, http, ct).ConfigureAwait(false);
        if (resolved.Error is not null)
            return resolved.Error;

        var jobId = await jobs.LaunchAsync(
            ContentGenerateJobHandler.JobType,
            $"Sinh nội dung {resolved.Platform}",
            new ContentGenerateJobPayload(resolved.BriefId, resolved.Platform, resolved.Brief),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

    // Sinh prompt ảnh chạy ngầm — kết quả nằm ở tóm tắt của job (mở từ thông báo).
    private static async Task<IResult> GenerateImagePromptAsync(
        GenerateImagePromptRequest body,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Brief))
            return Error(http, StatusCodes.Status400BadRequest, "content.image_prompt_invalid", "brief required.");

        var jobId = await jobs.LaunchAsync(
            ContentImagePromptJobHandler.JobType,
            "Sinh prompt ảnh cho bài đăng",
            new ContentImagePromptJobPayload(body),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static async Task<IResult> QueueAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? status,
        [FromQuery] string? platform,
        [FromQuery] string? cursor,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (pageSize is < 1 or > 200) pageSize = 50;

        var query = db.ContentItems.AsNoTracking().Where(i => i.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(i => i.Status == status);
        if (!string.IsNullOrWhiteSpace(platform))
            query = query.Where(i => i.Platform == platform);

        // Keyset on UpdatedAt DESC, Id DESC when cursor present; offset still supported for compat.
        if (cursor is not null || page <= 1)
        {
            var key = Clawbot.Api.Common.Pagination.KeysetQuery.Decode(cursor);
            int? total = key is null ? await query.CountAsync(ct).ConfigureAwait(false) : null;
            if (key is not null)
            {
                var ts = key.Value.Ts;
                var id = key.Value.Id;
                query = query.Where(i => i.UpdatedAt < ts || (i.UpdatedAt == ts && i.Id < id));
            }

            var fetched = await query
                .OrderByDescending(i => i.UpdatedAt)
                .ThenByDescending(i => i.Id)
                .Take(pageSize + 1)
                .Select(i => ToDto(i))
                .ToListAsync(ct).ConfigureAwait(false);

            var (rows, nextCursor) = Clawbot.Api.Common.Pagination.KeysetQuery.SliceWithCursor(
                fetched, pageSize, r => r.UpdatedAt, r => r.Id);
            return Results.Ok(new ContentQueueCursorPage(rows, nextCursor, total));
        }

        if (page < 1) page = 1;
        var offsetTotal = await query.CountAsync(ct).ConfigureAwait(false);
        var offsetItems = await query
            .OrderByDescending(i => i.UpdatedAt)
            .ThenByDescending(i => i.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => ToDto(i))
            .ToListAsync(ct).ConfigureAwait(false);

        return Results.Ok(new ContentQueueResponse(offsetItems, offsetTotal, page, pageSize));
    }

    private static async Task<IResult> GetItemAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var item = await LoadItemAsync(id, db, ct).ConfigureAwait(false);
        return item is null
            ? Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.")
            : Results.Ok(item);
    }

    private static async Task<IResult> UpdateItemAsync(
        Guid id,
        UpdateContentItemRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Body))
            return Error(http, StatusCodes.Status400BadRequest, "content.item_invalid", "body required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        item.UpdateBody(body.Body, clock.UtcNow);
        if (body.AssetsJson is not null)
        {
            if (!TryNormalizeAssetsJson(body.AssetsJson, out var normalizedAssetsJson))
                return Error(http, StatusCodes.Status400BadRequest, "content.assets_invalid", "assetsJson must be an image asset array.");
            item.SetAssets(normalizedAssetsJson, clock.UtcNow);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> UploadItemAssetAsync(
        Guid id,
        IFormFile file,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        [FromServices] IDocumentStorage storage,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        if (file is null || file.Length == 0)
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_missing", "Thiếu file ảnh.");
        if (file.Length > MaxAssetUploadBytes)
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_too_large", "Ảnh tối đa 5MB.");
        if (!AllowedAssetContentTypes.Contains(file.ContentType ?? string.Empty))
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_invalid_type", "Chỉ chấp nhận PNG, JPG, WebP hoặc GIF.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        if (!LooksLikeAllowedImage(bytes, file.ContentType))
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_invalid_type", "File không khớp định dạng ảnh.");

        var fileName = $"content-{item.Id}-{Guid.NewGuid():N}{ResolveAssetExtension(file)}";
        var url = await storage.SaveAsync(bytes, fileName, file.ContentType, ct).ConfigureAwait(false);
        var assetsJson = AddImageAsset(item.AssetsJson, url, file.FileName, file.ContentType);
        item.SetAssets(assetsJson, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Ok(new ContentAssetUploadResponse(url, assetsJson));
    }

    private static async Task<IResult> DeleteItemAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        item.SoftDelete(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ApproveItemAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        ClaimsPrincipal user,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var userId = CurrentUserId(user);
        if (userId is null)
            return Error(http, StatusCodes.Status400BadRequest, "content.user_missing", "Authenticated user id is required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        // G9: đã scheduled/published thì không approve lại được — tránh revert ngoài luồng.
        if (string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase))
            return Error(http, StatusCodes.Status400BadRequest, "content.item_not_approvable", $"Content item is '{item.Status}' and cannot be re-approved.");

        item.Approve(userId.Value, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> RejectItemAsync(
        Guid id,
        RejectContentItemRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        item.Reject(clock.UtcNow, body?.Reason);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> RepurposeItemAsync(
        Guid id,
        RepurposeContentItemRequest body,
        AppDbContext db,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        if (body.TargetPlatforms.Count == 0 || body.TargetPlatforms.Any(string.IsNullOrWhiteSpace))
            return Error(http, StatusCodes.Status400BadRequest, "content.repurpose_invalid", "targetPlatforms required.");

        var exists = await db.ContentItems.AsNoTracking()
            .AnyAsync(i => i.Id == id && i.DeletedAt == null, ct).ConfigureAwait(false);
        if (!exists)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        var jobId = await jobs.LaunchAsync(
            ContentRepurposeJobHandler.JobType,
            $"Chuyển thể bài sang {body.TargetPlatforms.Count} nền tảng",
            new ContentRepurposeJobPayload(id, body.TargetPlatforms),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static async Task<IResult> ScheduleItemAsync(
        Guid id,
        ScheduleContentItemRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IGoldenHourResolver goldenHour,
        Clawbot.SharedKernel.Content.IContentReviewPolicyResolver reviewPolicy,
        IMetaIntegrationService metaIntegrations,
        ContentAgent.ContentAgentClient grpc,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");
        if (!string.Equals(item.Status, "approved", StringComparison.OrdinalIgnoreCase))
            return Error(http, StatusCodes.Status400BadRequest, "content.item_not_approved", "Only approved content can be scheduled.");

        var now = clock.UtcNow;
        var resolution = ResolveScheduledAt(body, item, now, goldenHour);
        if (resolution.ErrorCode is not null)
            return Error(http, StatusCodes.Status400BadRequest, resolution.ErrorCode, resolution.Message ?? "Invalid schedule.");

        var exists = await db.ContentSchedules.AsNoTracking()
            .AnyAsync(s => s.ContentItemId == id && s.Status == "pending", ct).ConfigureAwait(false);
        if (exists)
            return Error(http, StatusCodes.Status409Conflict, "content.schedule_exists", "Content item already has a pending schedule.");

        // Review-gate P4 (SLA): lưu deadline mong muốn TRƯỚC khi qua review-gate — review fail thì
        // ContentReviewSlaJob vẫn có mốc để nhắc người duyệt kịp giờ đăng.
        item.SetDesiredPublishAt(resolution.ScheduledAt, now);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Review-gate P1: human approve xong vẫn phải có chữ ký reviewer agent trước khi lên lịch.
        // Sync review bounded; mọi lỗi/timeout => KHÔNG tạo schedule (fail-closed, QĐ3) — item giữ
        // trạng thái approved-chưa-ký, SLA job (Phase 4) nhắc người xử lý theo DesiredPublishAt vừa lưu.
        if (item.ApprovedByAgentId is null
            && await reviewPolicy.IsRequiredAsync(item.TenantId, ct).ConfigureAwait(false))
        {
            try
            {
                var review = await grpc.ReviewAsync(
                    new ReviewContentRequest { TenantId = item.TenantId.ToString(), ContentId = item.Id.ToString() },
                    deadline: DateTime.UtcNow.AddSeconds(25),
                    cancellationToken: ct).ResponseAsync.ConfigureAwait(false);
                if (!string.Equals(review.Verdict, "approve", StringComparison.OrdinalIgnoreCase))
                    return Error(http, StatusCodes.Status422UnprocessableEntity,
                        "content.review_" + review.Verdict,
                        string.IsNullOrWhiteSpace(review.Reason) ? "Nội dung chưa qua agent review." : review.Reason);
                // Reviewer đã stamp ApprovedByAgentId ở AgentService; entity local chỉ cần đi tiếp tạo schedule.
            }
            catch (RpcException)
            {
                return Error(http, StatusCodes.Status422UnprocessableEntity,
                    "content.review_unavailable",
                    "Không gọi được agent review — bài chưa được lên lịch. Thử lại hoặc chờ duyệt tay.");
            }
        }

        Guid? metaAssetId = null;
        if (string.Equals(item.Platform, "facebook", StringComparison.OrdinalIgnoreCase))
        {
            var pages = await metaIntegrations.GetPublishablePagesAsync(item.TenantId, ct).ConfigureAwait(false);
            var page = body.MetaAssetId.HasValue
                ? pages.FirstOrDefault(x => x.Id == body.MetaAssetId.Value)
                : pages.FirstOrDefault(x => x.IsDefault) ?? (pages.Count > 0 ? pages[0] : null);
            if (page is null)
                return Error(http, StatusCodes.Status400BadRequest, "content.meta_page_required", "Hãy kết nối và chọn Facebook Page trước khi lên lịch.");
            metaAssetId = page.Id;
        }
        else if (body.MetaAssetId.HasValue)
        {
            return Error(http, StatusCodes.Status400BadRequest, "content.meta_page_invalid", "Facebook Page chỉ áp dụng cho nội dung Facebook.");
        }

        var schedule = ContentSchedule.Schedule(item.TenantId, item.Id, item.Platform, resolution.ScheduledAt, now, metaAssetId);
        item.MarkScheduled(now);
        db.ContentSchedules.Add(schedule);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/content/schedule/{schedule.Id}", ToDto(schedule));
    }

    private static async Task<IResult> CalendarAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var fromValue = from ?? new DateTimeOffset(clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var toValue = to ?? fromValue.AddDays(30);
        if (toValue <= fromValue)
            return Error(http, StatusCodes.Status400BadRequest, "content.calendar_range_invalid", "to must be after from.");

        var schedules = await db.ContentSchedules.AsNoTracking()
            .Where(s => s.ScheduledAt >= fromValue && s.ScheduledAt < toValue)
            .OrderBy(s => s.ScheduledAt)
            .ToListAsync(ct).ConfigureAwait(false);
        if (schedules.Count == 0)
            return Results.Ok(new ContentCalendarResponse([]));

        var itemIds = schedules.Select(s => s.ContentItemId).Distinct().ToList();
        var itemsById = await db.ContentItems.AsNoTracking()
            .Where(i => itemIds.Contains(i.Id) && i.DeletedAt == null)
            .ToDictionaryAsync(i => i.Id, ct).ConfigureAwait(false);
        var rows = BuildCalendarRows(schedules, itemsById);

        return Results.Ok(new ContentCalendarResponse(rows));
    }

    private static async Task<IResult> PublishTargetsAsync(
        [FromQuery] string? platform,
        ITenantAccessor tenants,
        IMetaIntegrationService metaIntegrations,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (!string.Equals(platform, "facebook", StringComparison.OrdinalIgnoreCase))
            return Results.Ok(Array.Empty<ContentPublishTargetDto>());

        var pages = await metaIntegrations.GetPublishablePagesAsync(tenant.TenantId, ct).ConfigureAwait(false);
        return Results.Ok(pages.Select(x => new ContentPublishTargetDto(
            x.Id,
            "facebook",
            x.ExternalId,
            x.Name,
            x.IsDefault)).ToList());
    }

    private static async Task<IResult> DeleteScheduleAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var schedule = await db.ContentSchedules.FirstOrDefaultAsync(s => s.Id == id, ct).ConfigureAwait(false);
        if (schedule is null)
            return Error(http, StatusCodes.Status404NotFound, "content.schedule_not_found", "Content schedule not found.");
        if (!string.Equals(schedule.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return Error(http, StatusCodes.Status400BadRequest, "content.schedule_not_pending", "Only pending schedules can be canceled.");

        schedule.Cancel(clock.UtcNow);
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == schedule.ContentItemId && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is not null && string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            item.RevertToApproved(clock.UtcNow);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> TrendsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? week,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var query = db.ContentBriefs.AsNoTracking()
            .Where(b => b.Brief.StartsWith("[trend:"));

        if (!string.IsNullOrWhiteSpace(week))
        {
            if (!ContentTrendBriefFormatter.TryNormalizeWeekOf(week, out var normalizedWeek))
                return Error(http, StatusCodes.Status400BadRequest, "content.week_invalid", "week must use ISO format yyyy-Www.");

            var prefix = $"[trend:{normalizedWeek}]";
            query = query.Where(b => b.Brief.StartsWith(prefix));
        }

        var briefs = await query
            .OrderByDescending(b => b.UpdatedAt)
            .Select(b => b.Brief)
            .ToListAsync(ct).ConfigureAwait(false);
        var trends = briefs
            .Select(ToTrendDto)
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        return Results.Ok(new TrendScanResponse(trends));
    }

    private static async Task<IResult> ScanTrendsAsync(
        IJobLauncher jobs,
        ITenantAccessor tenants,
        IClock clock,
        [FromQuery] string? week,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var weekOf = ContentTrendBriefFormatter.CurrentWeekOf(clock.UtcNow);
        if (!string.IsNullOrWhiteSpace(week)
            && !ContentTrendBriefFormatter.TryNormalizeWeekOf(week, out weekOf))
        {
            return Error(http, StatusCodes.Status400BadRequest, "content.week_invalid", "week must use ISO format yyyy-Www.");
        }

        var jobId = await jobs.LaunchAsync(
            ContentTrendScanJobHandler.JobType,
            $"Quét xu hướng tuần {weekOf}",
            new ContentTrendScanJobPayload(weekOf),
            CurrentUserId(http),
            // 1 tuần chỉ cần 1 lần quét đang chạy — bấm 2 lần trả lại đúng job cũ.
            idempotencyKey: $"trends:{weekOf}",
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static async Task<GenerateInput> ResolveGenerateInputAsync(
        GenerateContentItemRequest body,
        AppDbContext db,
        HttpContext http,
        CancellationToken ct)
    {
        if (body.BriefId.HasValue)
        {
            var brief = await db.ContentBriefs.AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == body.BriefId.Value, ct).ConfigureAwait(false);
            if (brief is null)
                return new GenerateInput(null, string.Empty, string.Empty,
                    Error(http, StatusCodes.Status404NotFound, "content.brief_not_found", "Content brief not found."));

            // Trend-scan briefs carry the trend source (e.g. "google_trends") in Platform, which has no
            // prompt template — let the request pick the target platform, brief only provides the default.
            var platform = string.IsNullOrWhiteSpace(body.Platform) ? brief.Platform : body.Platform.Trim();
            return new GenerateInput(brief.Id, platform, brief.Brief, null);
        }

        if (string.IsNullOrWhiteSpace(body.Platform) || string.IsNullOrWhiteSpace(body.BriefText))
        {
            return new GenerateInput(null, string.Empty, string.Empty,
                Error(http, StatusCodes.Status400BadRequest, "content.generate_invalid", "briefId or platform and briefText required."));
        }

        return new GenerateInput(null, body.Platform.Trim(), body.BriefText.Trim(), null);
    }

    private static async Task<ContentItemDto?> LoadItemAsync(Guid id, AppDbContext db, CancellationToken ct) =>
        await db.ContentItems.AsNoTracking()
            .Where(i => i.Id == id && i.DeletedAt == null)
            .Select(i => ToDto(i))
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    private static async Task<IReadOnlyList<ContentItemDto>> LoadItemsAsync(
        List<Guid> ids,
        AppDbContext db,
        CancellationToken ct)
    {
        if (ids.Count == 0)
            return [];

        return await db.ContentItems.AsNoTracking()
            .Where(i => ids.Contains(i.Id) && i.DeletedAt == null)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => ToDto(i))
            .ToListAsync(ct).ConfigureAwait(false);
    }

    private static ContentBriefDto ToDto(ContentBrief brief) =>
        new(brief.Id, brief.Platform, brief.Brief, brief.Status, brief.CreatedBy, brief.CreatedAt, brief.UpdatedAt);

    private static ContentItemDto ToDto(ContentItem item) =>
        new(
            item.Id,
            item.BriefId,
            item.Platform,
            item.Status,
            item.Body,
            item.AssetsJson,
            item.CreatedBy,
            item.ApprovedBy,
            item.ApprovedAt,
            item.CreatedAt,
            item.UpdatedAt);

    private static ContentScheduleDto ToDto(ContentSchedule schedule) =>
        new(
            schedule.Id,
            schedule.ContentItemId,
            schedule.Platform,
            schedule.ScheduledAt,
            schedule.PostedAt,
            schedule.Status,
            schedule.PostUrl,
            schedule.CreatedAt,
            schedule.UpdatedAt,
            schedule.MetaAssetId,
            schedule.LikeCount,
            schedule.CommentCount,
            schedule.EngagementSyncedAt);

    internal static IReadOnlyList<ContentCalendarItemDto> BuildCalendarRows(
        IReadOnlyList<ContentSchedule> schedules,
        IReadOnlyDictionary<Guid, ContentItem> itemsById) =>
        schedules
            .Where(s => itemsById.ContainsKey(s.ContentItemId))
            .Select(s =>
            {
                var item = itemsById[s.ContentItemId];
                return new ContentCalendarItemDto(
                    s.Id,
                    s.ContentItemId,
                    s.Platform,
                    s.Status,
                    item.Body,
                    s.ScheduledAt,
                    s.PostedAt,
                    s.PostUrl,
                    s.MetaAssetId,
                    s.LikeCount,
                    s.CommentCount);
            })
            .ToList();

    internal static ScheduleResolution ResolveScheduledAt(
        ScheduleContentItemRequest body,
        ContentItem item,
        DateTimeOffset now,
        IGoldenHourResolver goldenHour)
    {
        var scheduledAt = body.ScheduledAt ?? goldenHour.ResolveNext(item.Platform, now);
        return scheduledAt <= now
            ? new ScheduleResolution(
                scheduledAt,
                "content.schedule_in_past",
                "scheduledAt must be in the future.")
            : new ScheduleResolution(scheduledAt, null, null);
    }

    internal static string AddImageAsset(string assetsJson, string url, string? fileName, string? contentType)
    {
        _ = TryNormalizeAssetsJson(assetsJson, out var normalizedExisting);
        var assets = JsonNode.Parse(normalizedExisting) as JsonArray ?? [];
        assets.Add(CreateAssetObject(url, fileName, contentType));
        return assets.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    internal static bool TryNormalizeAssetsJson(string assetsJson, out string normalized)
    {
        normalized = "[]";
        if (string.IsNullOrWhiteSpace(assetsJson))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(assetsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return false;

            var assets = new JsonArray();
            foreach (var asset in doc.RootElement.EnumerateArray())
            {
                if (assets.Count >= MaxAssetsPerContentItem || !TryReadImageAsset(asset, out var url, out var fileName, out var contentType))
                    return false;
                assets.Add(CreateAssetObject(url, fileName, contentType));
            }
            normalized = assets.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadImageAsset(JsonElement asset, out string url, out string? fileName, out string? contentType)
    {
        url = string.Empty;
        fileName = null;
        contentType = null;
        if (asset.ValueKind != JsonValueKind.Object)
            return false;
        var type = asset.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "image";
        if (!string.Equals(type, "image", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!asset.TryGetProperty("url", out var urlElement) || urlElement.ValueKind != JsonValueKind.String)
            return false;
        url = urlElement.GetString() ?? string.Empty;
        if (!IsAllowedAssetUrl(url))
            return false;
        if (asset.TryGetProperty("fileName", out var fileNameElement) && fileNameElement.ValueKind == JsonValueKind.String)
            fileName = fileNameElement.GetString();
        if (asset.TryGetProperty("contentType", out var contentTypeElement) && contentTypeElement.ValueKind == JsonValueKind.String)
            contentType = contentTypeElement.GetString();
        return string.IsNullOrWhiteSpace(contentType) || AllowedAssetContentTypes.Contains(contentType);
    }

    private static bool IsAllowedAssetUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxAssetUrlChars)
            return false;
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    private static JsonObject CreateAssetObject(string url, string? fileName, string? contentType) =>
        new()
        {
            ["type"] = "image",
            ["url"] = url,
            ["fileName"] = string.IsNullOrWhiteSpace(fileName) ? null : Path.GetFileName(fileName),
            ["contentType"] = string.IsNullOrWhiteSpace(contentType) ? null : contentType,
        };

    internal static bool LooksLikeAllowedImage(ReadOnlySpan<byte> bytes, string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/png" => bytes.Length >= 8
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
                && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            "image/jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/gif" => bytes.Length >= 6
                && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38
                && (bytes[4] == 0x37 || bytes[4] == 0x39) && bytes[5] == 0x61,
            "image/webp" => bytes.Length >= 12
                && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            _ => false,
        };

    private static string ResolveAssetExtension(IFormFile file)
    {
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is ".gif" or ".jpg" or ".jpeg" or ".png" or ".webp")
            return ext;

        return file.ContentType?.ToLowerInvariant() switch
        {
            "image/gif" => ".gif",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".png",
        };
    }

    private static TrendDto ToTrendDto(TrendItem trend, string weekOf) =>
        new(trend.Topic, trend.Source, trend.Metric, trend.RelevanceScore, trend.ContentIdeas.ToList(), weekOf);

    private static TrendDto? ToTrendDto(string brief)
    {
        if (!ContentTrendBriefFormatter.TryParse(brief, out var trend) || trend is null)
            return null;

        return new TrendDto(
            trend.Topic,
            trend.Source,
            trend.Metric,
            trend.RelevanceScore,
            trend.ContentIdeas,
            trend.WeekOf);
    }

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    private static IResult MapGrpcError(RpcException ex, HttpContext http) =>
        ex.StatusCode switch
        {
            StatusCode.InvalidArgument => Error(http, StatusCodes.Status400BadRequest, "content.agent_invalid", ex.Status.Detail),
            StatusCode.NotFound => Error(http, StatusCodes.Status404NotFound, "content.agent_not_found", ex.Status.Detail),
            StatusCode.Unavailable => Error(http, StatusCodes.Status503ServiceUnavailable, "content.agent_unavailable", ex.Status.Detail),
            _ => Error(http, StatusCodes.Status502BadGateway, "content.agent_failed", ex.Status.Detail),
        };

    private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
        Results.Json(
            new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier },
            statusCode: statusCode);

    private sealed record GenerateInput(Guid? BriefId, string Platform, string Brief, IResult? Error);

    internal sealed record ScheduleResolution(DateTimeOffset ScheduledAt, string? ErrorCode, string? Message);

    private sealed record ContentApiError(string ErrorCode, string Message, string RequestId);
}
