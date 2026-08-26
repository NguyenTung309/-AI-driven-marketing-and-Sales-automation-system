using System.Globalization;
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
using Clawbot.Api.Services;
using Clawbot.Domain.Content;
using Clawbot.Domain.Security;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ContentEndpoints
{
    private const int MaxAssetsPerContentItem = 10;
    private const int MaxAssetUrlChars = 2048;
    private const long MaxAssetUploadBytes = 5 * 1024 * 1024;
    private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> AllowedAssetContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/gif",
        "image/jpeg",
        "image/png",
        "image/webp",
    };
    private static readonly HashSet<string> TrustedSocialPostHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook.com",
        "www.facebook.com",
        "m.facebook.com",
        "instagram.com",
        "www.instagram.com",
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
        grp.MapGet("/items/{id:guid}/assets/{assetId:guid}", GetItemAssetAsync).RequirePermission("content:read");
        grp.MapPut("/items/{id:guid}", UpdateItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/assets", UploadItemAssetAsync)
            .RequirePermission("content:write")
            .RequireRateLimiting(RateLimitingExtensions.UploadPolicy)
            .DisableAntiforgery();
        grp.MapDelete("/items/{id:guid}/assets/{assetId:guid}", DeleteItemAssetAsync)
            .RequirePermission("content:write");
        grp.MapDelete("/items/{id:guid}", DeleteItemAsync).RequirePermission("content:write");
        // Phase 4.4: human publishing decisions (not Agent review).
        grp.MapPost("/items/{id:guid}/approve", ApproveItemAsync).RequirePermission("content:approve");
        grp.MapPost("/items/{id:guid}/reject", RejectItemAsync).RequirePermission("content:approve");
        // Phase 4.5: upsert durable review task only — no inline LLM call.
        grp.MapPost("/items/{id:guid}/agent-review/retry", RetryAgentReviewAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/schedule", ScheduleItemAsync).RequirePermission("content:write");
        grp.MapPost("/items/{id:guid}/repurpose", RepurposeItemAsync).RequirePermission("content:write");
        // P5 §4.5: đọc danh sách hook L2 đã lưu + đổi hook (chạy lại L3+L4 với hook marketer chọn).
        grp.MapGet("/items/{id:guid}/hooks", GetItemHooksAsync).RequirePermission("content:read");
        grp.MapPost("/items/{id:guid}/regenerate-hook", RegenerateHookAsync).RequirePermission("content:write");
        // P5 §6: dashboard vận hành chuỗi sinh nội dung (fallback rate, gate fail/step, token/độ trễ, review approve).
        grp.MapGet("/chain-metrics", ChainMetricsAsync).RequirePermission("content:read");
        grp.MapGet("/post-performance", PostPerformanceAsync).RequirePermission("content:read");
        // Xem binh luan ngay trong app: goi thang Graph, khong luu DB (tranh om PII nguoi binh luan).
        grp.MapGet("/schedules/{id:guid}/comments", ScheduleCommentsAsync).RequirePermission("content:read");
        grp.MapGet("/calendar", CalendarAsync).RequirePermission("content:read");
        grp.MapGet("/publish-targets", PublishTargetsAsync).RequirePermission("content:read");
        grp.MapDelete("/schedule/{id:guid}", DeleteScheduleAsync).RequirePermission("content:write");
        // Phase 4.6: privileged durable-state transitions only (never provider inline).
        grp.MapPost("/schedules/{id:guid}/publish/retry", RetryPublishScheduleAsync).RequirePermission("content:publish");
        grp.MapPost("/schedules/{id:guid}/publish/reconcile", ReconcilePublishScheduleAsync).RequirePermission("content:publish");
        ContentPublishingPolicyEndpoints.Map(grp);

        app.MapGet("/api/public/content/assets/{assetId:guid}.jpg", GetPublicAssetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        app.MapGet("/api/public/content/assets/{assetId:guid}.jpeg", GetPublicAssetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        app.MapGet("/api/public/content/assets/{assetId:guid}", GetPublicAssetAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        app.MapGet("/api/content/items/{id:guid}/assets/{assetId:guid}.jpg", (Guid id, Guid assetId, AppDbContext db, IDocumentStorage storage, HttpContext http, CancellationToken ct) => GetPublicAssetAsync(assetId, db, storage, http, ct))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        app.MapGet("/api/content/items/{id:guid}/assets/{assetId:guid}.jpeg", (Guid id, Guid assetId, AppDbContext db, IDocumentStorage storage, HttpContext http, CancellationToken ct) => GetPublicAssetAsync(assetId, db, storage, http, ct))
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

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
        if (!ContentPlatformCatalog.TryNormalizeWritable(body.Platform, out var platform))
            return UnsupportedPlatform(http);

        var brief = ContentBrief.Create(
            tenant.TenantId, platform!, body.Brief.Trim(), CurrentUserId(user), clock.UtcNow);
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

        string platform;
        if (ContentPlatformCatalog.TryNormalizeWritable(body.Platform, out var normalizedPlatform))
        {
            platform = normalizedPlatform!;
        }
        else if (string.Equals(body.Platform.Trim(), brief.Platform.Trim(), StringComparison.OrdinalIgnoreCase)
                 && !ContentPlatformCatalog.TryNormalizeWritable(brief.Platform, out _))
        {
            platform = brief.Platform;
        }
        else
        {
            return UnsupportedPlatform(http);
        }

        brief.Update(platform, body.Brief.Trim(), clock.UtcNow);
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
        if (!ContentPlatformCatalog.TryNormalizeWritable(body.Platform, out var platform))
            return UnsupportedPlatform(http);

        var jobId = await jobs.LaunchAsync(
            ContentImagePromptJobHandler.JobType,
            "Sinh prompt ảnh cho bài đăng",
            new ContentImagePromptJobPayload(body with { Platform = platform }),
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
        if (cursor is not null)
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
        var tenantId = tenants.Require().TenantId;
        if (string.IsNullOrWhiteSpace(body.Body))
            return Error(http, StatusCodes.Status400BadRequest, "content.item_invalid", "body required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        try
        {
            var revisionBefore = item.ContentRevision;
            var now = clock.UtcNow;
            item.ClaimOrchestrationOwnershipForHuman(CurrentUserId(http), now);
            item.UpdateBody(body.Body, now);
            if (body.AssetsJson is not null)
            {
                if (!TryNormalizeAssetsJson(body.AssetsJson, out var normalizedAssetsJson))
                    return Error(http, StatusCodes.Status400BadRequest, "content.assets_invalid", "assetsJson must be an image asset array.");
                // AssetsJson remains a derived compatibility view; authoritative set is content_assets.
                item.SetAssets(normalizedAssetsJson, now);
            }

            if (item.ContentRevision != revisionBefore)
            {
                await CancelStaleScheduleIntentsAsync(db, item.Id, revisionBefore, now, ct).ConfigureAwait(false);
                db.ContentReviewTasks.Add(ContentAssetLifecycle.CreateQuietPeriodReviewTask(
                    tenantId,
                    item.Id,
                    item.ContentRevision,
                    now));
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(ToDto(item));
        }
        catch (InvalidOperationException exception) when (
            exception.Message is "content_published_item_immutable" or "content_publish_attempt_active")
        {
            return Error(http, StatusCodes.Status409Conflict, exception.Message, exception.Message);
        }
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
        var tenantId = tenants.Require().TenantId;
        if (file is null || file.Length == 0)
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_missing", "Thiếu file ảnh.");
        if (file.Length > MaxAssetUploadBytes)
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_too_large", "Ảnh tối đa 5MB.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");
        if (string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase)
            || item.ActivePublishAttemptId is not null)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                item.ActivePublishAttemptId is not null
                    ? "content_publish_attempt_active"
                    : "content_published_item_immutable",
                "Published or actively-claimed content cannot receive new assets.");
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct).ConfigureAwait(false);
        var bytes = ms.ToArray();
        var contentType = ResolveAssetContentType(bytes, file.ContentType);
        if (contentType is null)
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_invalid_type", "Chỉ chấp nhận PNG, JPG, WebP hoặc GIF hợp lệ.");

        var now = clock.UtcNow;
        var existing = await db.ContentAssets
            .Where(a => a.TenantId == tenantId && a.ContentItemId == item.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (existing.Count(a => a.Status == ContentAsset.StatusReady) >= MaxAssetsPerContentItem)
            return Error(http, StatusCodes.Status400BadRequest, "content.asset_limit", "Tối đa 10 ảnh mỗi bài.");

        // Phase 2.9: reserve server-owned row + storage key first; object upload outside the final txn.
        var asset = ContentAsset.Reserve(
            tenantId,
            item.Id,
            file.FileName,
            ResolveAssetExtension(contentType),
            ContentAssetLifecycle.NextSortOrder(existing),
            now);
        db.ContentAssets.Add(asset);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        string url;
        try
        {
            // Storage key is the authority; local backend flattens path segments safely.
            _ = await storage.SaveAsync(bytes, asset.StorageKey, contentType, ct).ConfigureAwait(false);
            url = ItemAssetUrl(item.Id, asset.Id);
        }
        catch (Exception)
        {
            asset.MarkFailed("content_asset_upload_failed", clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Error(http, StatusCodes.Status502BadGateway, "content.asset_upload_failed", "Không lưu được ảnh.");
        }

        try
        {
            var readyAt = clock.UtcNow;
            asset.MarkReady(
                ContentAssetLifecycle.ComputeSha256(bytes),
                bytes.LongLength,
                contentType,
                readyAt);

            var readyAssets = existing
                .Where(a => a.Status == ContentAsset.StatusReady)
                .Append(asset)
                .ToList();
            var displayUrls = BuildDisplayUrlMap(item.Id, item.AssetsJson, readyAssets, asset.Id, url);
            var assetsJson = ContentAssetLifecycle.BuildDerivedAssetsJson(readyAssets, displayUrls);
            var revisionBefore = item.ContentRevision;
            item.ClaimOrchestrationOwnershipForHuman(CurrentUserId(http), readyAt);
            item.ReviseAssets(assetsJson, readyAt);
            if (item.ContentRevision != revisionBefore)
                await CancelStaleScheduleIntentsAsync(db, item.Id, revisionBefore, readyAt, ct).ConfigureAwait(false);
            db.ContentReviewTasks.Add(ContentAssetLifecycle.CreateQuietPeriodReviewTask(
                tenantId,
                item.Id,
                item.ContentRevision,
                readyAt));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            return Results.Ok(new ContentAssetUploadResponse(url, assetsJson, asset.Id));
        }
        catch (InvalidOperationException exception) when (
            exception.Message is "content_published_item_immutable" or "content_publish_attempt_active")
        {
            return Error(http, StatusCodes.Status409Conflict, exception.Message, exception.Message);
        }
    }

    private static async Task<IResult> GetItemAssetAsync(
        Guid id,
        Guid assetId,
        AppDbContext db,
        ITenantAccessor tenants,
        IDocumentStorage storage,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var asset = await db.ContentAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == assetId
                && item.ContentItemId == id
                && item.TenantId == tenantId
                && item.Status == ContentAsset.StatusReady, ct)
            .ConfigureAwait(false);
        if (asset is null) return Results.NotFound();

        try
        {
            var bytes = await storage.ReadAsync(asset.StorageKey, ct).ConfigureAwait(false);
            if (asset.Sha256 is { Length: > 0 } hash)
                http.Response.Headers.ETag = $"\"{Convert.ToHexString(hash)}\"";
            http.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(bytes, asset.ContentType ?? "application/octet-stream");
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> GetPublicAssetAsync(
        Guid assetId,
        AppDbContext db,
        [FromServices] IDocumentStorage storage,
        HttpContext http,
        CancellationToken ct)
    {
        var asset = await db.ContentAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == assetId && item.Status == ContentAsset.StatusReady, ct)
            .ConfigureAwait(false);
        if (asset is null) return Results.NotFound();

        try
        {
            var bytes = await storage.ReadAsync(asset.StorageKey, ct).ConfigureAwait(false);
            if (asset.Sha256 is { Length: > 0 } hash)
                http.Response.Headers.ETag = $"\"{Convert.ToHexString(hash)}\"";
            http.Response.Headers.CacheControl = "public, max-age=86400";
            return Results.File(bytes, asset.ContentType ?? "image/jpeg");
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> DeleteItemAssetAsync(
        Guid id,
        Guid assetId,
        AppDbContext db,
        ITenantAccessor tenants,
        IDocumentStorage storage,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        var asset = await db.ContentAssets
            .FirstOrDefaultAsync(
                a => a.Id == assetId && a.TenantId == tenantId && a.ContentItemId == item.Id,
                ct)
            .ConfigureAwait(false);
        if (asset is null || asset.Status == ContentAsset.StatusDeleted)
            return Error(http, StatusCodes.Status404NotFound, "content.asset_not_found", "Asset not found.");

        if (asset.Status != ContentAsset.StatusDeletePending)
        {
            var deleteRequestedAt = clock.UtcNow;
            asset.MarkDeletePending("asset_removed", deleteRequestedAt);

            var readyAssets = await db.ContentAssets
                .Where(a => a.TenantId == tenantId
                    && a.ContentItemId == item.Id
                    && a.Status == ContentAsset.StatusReady
                    && a.Id != asset.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var displayUrls = BuildDisplayUrlMap(item.Id, item.AssetsJson, readyAssets, Guid.Empty, string.Empty);
            var assetsJson = ContentAssetLifecycle.BuildDerivedAssetsJson(readyAssets, displayUrls);
            item.ClaimOrchestrationOwnershipForHuman(CurrentUserId(http), deleteRequestedAt);
            item.ReviseAssets(assetsJson, deleteRequestedAt);
            db.ContentReviewTasks.Add(ContentAssetLifecycle.CreateQuietPeriodReviewTask(
                tenantId,
                item.Id,
                item.ContentRevision,
                deleteRequestedAt));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        await storage.DeleteAsync(asset.StorageKey, ct).ConfigureAwait(false);
        asset.MarkDeleted(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Dictionary<Guid, string> BuildDisplayUrlMap(
        Guid contentItemId,
        string existingAssetsJson,
        List<ContentAsset> readyAssets,
        Guid newAssetId,
        string newUrl)
    {
        var map = new Dictionary<Guid, string>();
        if (TryNormalizeAssetsJson(existingAssetsJson, out var normalized)
            && JsonNode.Parse(normalized) is JsonArray arr)
        {
            foreach (var node in arr.OfType<JsonObject>())
            {
                var assetIdText = node["assetId"]?.GetValue<string>();
                var url = node["url"]?.GetValue<string>();
                if (Guid.TryParse(assetIdText, out var assetId)
                    && !string.IsNullOrWhiteSpace(url)
                    && readyAssets.Any(a => a.Id == assetId))
                {
                    map[assetId] = url!;
                }
            }
        }

        // Legacy AssetsJson without assetId: assign URLs in sort order for ready assets missing a map entry.
        if (map.Count < readyAssets.Count
            && TryNormalizeAssetsJson(existingAssetsJson, out var legacyNormalized)
            && JsonNode.Parse(legacyNormalized) is JsonArray legacyArr)
        {
            var orphanUrls = legacyArr
                .OfType<JsonObject>()
                .Select(n => n["url"]?.GetValue<string>())
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Cast<string>()
                .Where(u => !map.ContainsValue(u))
                .ToList();
            var missing = readyAssets
                .Where(a => a.Id != newAssetId && !map.ContainsKey(a.Id))
                .OrderBy(a => a.SortOrder)
                .ToList();
            for (var i = 0; i < missing.Count && i < orphanUrls.Count; i++)
                map[missing[i].Id] = orphanUrls[i];
        }

        if (newAssetId != Guid.Empty && !string.IsNullOrWhiteSpace(newUrl))
            map[newAssetId] = newUrl;

        foreach (var asset in readyAssets)
            map[asset.Id] = ItemAssetUrl(contentItemId, asset.Id);
        return map;
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
        ApproveContentItemRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        Clawbot.Infrastructure.Content.IContentAutoScheduler autoScheduler,
        IMetaIntegrationService metaIntegrations,
        ClaimsPrincipal user,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantContext = tenants.Require();
        var userId = CurrentUserId(user);
        if (userId is null)
            return Error(http, StatusCodes.Status400BadRequest, "content.user_missing", "Authenticated user id is required.");
        if (body is null || body.ExpectedRevision <= 0)
            return Error(http, StatusCodes.Status400BadRequest, "content.expected_revision_required", "expectedRevision is required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantContext.TenantId, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");
        if (tenant is null)
            return Error(http, StatusCodes.Status404NotFound, "content.tenant_not_found", "Tenant not found.");
        if (!ContentPlatformCatalog.TryNormalizeWritable(item.Platform, out _))
            return UnsupportedPlatform(http);

        try
        {
            var now = clock.UtcNow;
            // Phase 3.5: human approve/override + revision-bound schedule intent in one transaction.
            item.ApproveForPublishing(
                body.ExpectedRevision,
                userId.Value,
                tenant.ContentPublishingApprovalPolicy,
                tenant.ContentPublishingPolicyVersion,
                body.OverrideReason,
                now);

            // Resolve default Facebook page so the schedule doesn't end up held with auto_schedule_target_missing.
            Guid? publishTargetId = null;
            if (string.Equals(item.Platform, "facebook", StringComparison.OrdinalIgnoreCase))
            {
                var pages = await metaIntegrations.GetPublishablePagesAsync(tenantContext.TenantId, ct).ConfigureAwait(false);
                var page = pages.FirstOrDefault(x => x.IsDefault) ?? (pages.Count > 0 ? pages[0] : null);
                if (page is not null) publishTargetId = page.Id;
            }

            await autoScheduler.CreateIntentAsync(item, publishTargetId, now, cancellationToken: ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(ToDto(item));
        }
        catch (ArgumentException exception)
        {
            return Error(http, StatusCodes.Status400BadRequest, exception.Message, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Error(http, StatusCodes.Status409Conflict, exception.Message, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(http, StatusCodes.Status409Conflict, "content.revision_changed", "Content revision changed.");
        }
    }

    private static async Task<IResult> RejectItemAsync(
        Guid id,
        RejectContentItemRequest body,
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
        if (body is null || body.ExpectedRevision <= 0)
            return Error(http, StatusCodes.Status400BadRequest, "content.expected_revision_required", "expectedRevision is required.");

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        try
        {
            var now = clock.UtcNow;
            // Phase 3.6: final publishing rejection; cancel any active schedule intent for this revision.
            item.RejectForPublishing(
                body.ExpectedRevision,
                userId.Value,
                body.Reason ?? string.Empty,
                now);
            // Only cancel recoverable intents; publishing/outcome_unknown are claim-bound and stay locked.
            var activeSchedules = await db.ContentSchedules
                .Where(schedule =>
                    schedule.ContentItemId == item.Id
                    && schedule.ContentRevision == body.ExpectedRevision
                    && (schedule.Status == ContentSchedule.StatusPending
                        || schedule.Status == ContentSchedule.StatusHeld
                        || schedule.Status == ContentSchedule.StatusFailed))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var schedule in activeSchedules)
                schedule.Cancel(now, ContentSchedule.ErrorCanceledByUser);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(ToDto(item));
        }
        catch (ArgumentException exception)
        {
            return Error(http, StatusCodes.Status400BadRequest, exception.Message, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Error(http, StatusCodes.Status409Conflict, exception.Message, exception.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(http, StatusCodes.Status409Conflict, "content.revision_changed", "Content revision changed.");
        }
    }

    private static async Task<IResult> RepurposeItemAsync(
        Guid id,
        RepurposeContentItemRequest? body,
        AppDbContext db,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        var requestedTargets = body?.TargetPlatforms;
        if (requestedTargets is null
            || requestedTargets.Count == 0
            || requestedTargets.Any(string.IsNullOrWhiteSpace))
        {
            return Error(http, StatusCodes.Status400BadRequest, "content.repurpose_invalid", "targetPlatforms required.");
        }

        IReadOnlyList<string> targetPlatforms;
        try
        {
            targetPlatforms = ContentPlatformCatalog.NormalizeWritable(requestedTargets);
        }
        catch (ArgumentException)
        {
            return UnsupportedPlatform(http);
        }

        var exists = await db.ContentItems.AsNoTracking()
            .AnyAsync(i => i.Id == id && i.DeletedAt == null, ct).ConfigureAwait(false);
        if (!exists)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        var jobId = await jobs.LaunchAsync(
            ContentRepurposeJobHandler.JobType,
            $"Chuyển thể bài sang {targetPlatforms.Count} nền tảng",
            new ContentRepurposeJobPayload(id, targetPlatforms),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    // P5 §4.5: trả danh sách hook L2 đã lưu (từ ChainOutlineJson) + hook đang được chọn, để marketer đổi tay.
    // Bài single-shot cũ / chain tắt không có ChainOutlineJson => trả canRegenerate=false, hooks rỗng.
    private static async Task<IResult> GetItemHooksAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var row = await db.ContentItems.AsNoTracking()
            .Where(i => i.Id == id && i.DeletedAt == null)
            .Select(i => new { i.ChainOutlineJson })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (row is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        var (hooks, selectedIndex) = ContentHookReader.Read(row.ChainOutlineJson);
        var options = hooks
            .Select((text, index) => new ContentHookOptionDto(index, text, index == selectedIndex))
            .ToList();
        return Results.Ok(new ContentItemHooksResponse(options.Count > 0, options));
    }

    // P5 §4.5: đổi hook chạy ngầm — chạy lại L3+L4 với hook đã chọn, sửa bài tại chỗ (revision mới + chờ review lại).
    private static async Task<IResult> RegenerateHookAsync(
        Guid id,
        RegenerateHookApiRequest? body,
        AppDbContext db,
        IJobLauncher jobs,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        if (body is null || body.HookIndex < 0)
            return Error(http, StatusCodes.Status400BadRequest, "content.hook_index_invalid", "hookIndex required.");

        var item = await db.ContentItems
            .FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct).ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        item.ClaimOrchestrationOwnershipForHuman(CurrentUserId(http), clock.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.item_conflict",
                "Content changed concurrently. Refresh and try again.");
        }

        var jobId = await jobs.LaunchAsync(
            ContentRegenerateHookJobHandler.JobType,
            "Đổi hook mở bài",
            new ContentRegenerateHookJobPayload(id, body.HookIndex),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    // P5 §6: dashboard chỉ số chuỗi — đọc tổng hợp từ content_generation_traces (telemetry PII-free) trong cửa sổ
    // N ngày. Fallback rate, tỉ lệ fail từng cổng, token/chi phí trung bình mỗi lượt, độ trễ p95; approve rate lấy từ
    // content_items. Tính p95 in-memory (cần phân phối); trace chỉ giữ 30 ngày (retention) nên khối lượng có hạn.
    private static readonly string[] ChainStepOrder = ["plan", "outline", "write", "package"];
    private const string ChainFallbackStepId = "fallback";
    private const string ChainGatePassed = "passed";

    private static async Task<IResult> ChainMetricsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int? days,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var windowDays = days is null or < 1 or > 90 ? 7 : days.Value;
        var since = clock.UtcNow.AddDays(-windowDays);

        var rows = await db.ContentGenerationTraces.AsNoTracking()
            .Where(t => t.CreatedAt >= since)
            .Select(t => new { t.ChainRunId, t.StepId, t.GateResult, t.InputTokens, t.OutputTokens, t.UsdCost, t.LatencyMs })
            .ToListAsync(ct).ConfigureAwait(false);

        var runIds = rows.Select(r => r.ChainRunId).Distinct().ToList();
        var totalRuns = runIds.Count;
        var fallbackRuns = rows
            .Where(r => r.StepId == ChainFallbackStepId)
            .Select(r => r.ChainRunId)
            .Distinct()
            .Count();

        var totalTokens = rows.Sum(r => (long)r.InputTokens + r.OutputTokens);
        var totalCost = rows.Sum(r => r.UsdCost);

        var steps = ChainStepOrder.Select(stepId =>
        {
            var stepRows = rows.Where(r => r.StepId == stepId).ToList();
            var attempts = stepRows.Count;
            var gateFailures = stepRows.Count(r => r.GateResult != ChainGatePassed);
            var p95 = Percentile95(stepRows.Select(r => r.LatencyMs).ToList());
            return new ContentChainStepMetricDto(
                stepId,
                attempts,
                gateFailures,
                attempts > 0 ? Math.Round((double)gateFailures / attempts, 4) : 0,
                p95);
        }).ToList();

        var reviewTotal = await db.ContentItems.AsNoTracking()
            .Where(i => i.DeletedAt == null && i.AgentReviewedAt >= since)
            .CountAsync(ct).ConfigureAwait(false);
        var reviewApproved = await db.ContentItems.AsNoTracking()
            .Where(i => i.DeletedAt == null
                && i.AgentReviewedAt >= since
                && i.AgentReviewStatus == ContentItem.ReviewStatusPassed)
            .CountAsync(ct).ConfigureAwait(false);

        return Results.Ok(new ContentChainMetricsResponse(
            windowDays,
            totalRuns,
            fallbackRuns,
            totalRuns > 0 ? Math.Round((double)fallbackRuns / totalRuns, 4) : 0,
            totalRuns > 0 ? Math.Round((double)totalTokens / totalRuns, 1) : 0,
            totalRuns > 0 ? Math.Round((double)totalCost / totalRuns, 6) : 0,
            steps,
            reviewApproved,
            reviewTotal,
            reviewTotal > 0 ? Math.Round((double)reviewApproved / reviewTotal, 4) : 0));
    }

    // p95 tất định trên mẫu nhỏ: sắp xếp, lấy phần tử ở vị trí ceil(0.95*n)-1. Rỗng => 0.
    private static long Percentile95(List<long> values)
    {
        if (values.Count == 0)
            return 0;
        values.Sort();
        var rank = (int)Math.Ceiling(0.95 * values.Count) - 1;
        return values[Math.Clamp(rank, 0, values.Count - 1)];
    }

    private const int MaxPostComments = 25;

    // Binh luan cua bai dang KHONG duoc luu trong he thong (conversations khong tro ve bai dang),
    // nen phai hoi Graph tai thoi diem mo dialog. Khong ghi xuong DB => khong phat sinh retention PII.
    private static async Task<IResult> ScheduleCommentsAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IMetaIntegrationService meta,
        IMetaGraphClient graph,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var schedule = await db.ContentSchedules.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == id && row.TenantId == tenantId, ct)
            .ConfigureAwait(false);
        if (schedule is null)
            return Error(http, StatusCodes.Status404NotFound, "content.schedule_not_found", "Schedule not found.");

        if (!string.Equals(schedule.Platform, "facebook", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Ok(new ContentPostCommentsResponse(
                id, [], 0, false, "platform_not_supported"));
        }

        var objectId = MetaEngagementSyncJob.IsSafeGraphObjectId(schedule.ExternalPostId)
            ? schedule.ExternalPostId
            : MetaEngagementSyncJob.ExtractPostId(schedule.PostUrl);
        if (string.IsNullOrWhiteSpace(objectId))
            return Results.Ok(new ContentPostCommentsResponse(id, [], 0, false, "post_id_unavailable"));

        var credential = schedule.MetaAssetId.HasValue
            ? await meta.ResolvePageForEngagementAsync(tenantId, schedule.MetaAssetId, ct).ConfigureAwait(false)
            : await meta.ResolvePageForEngagementByExternalIdAsync(
                tenantId,
                MetaEngagementSyncJob.ExtractPageId(objectId),
                ct).ConfigureAwait(false);
        if (credential is null)
            return Results.Ok(new ContentPostCommentsResponse(id, [], 0, false, "no_page_credential"));

        try
        {
            using var doc = await graph.GetAsync(
                tenantId,
                $"{objectId}/comments",
                new Dictionary<string, string?>
                {
                    ["fields"] = "id,from{name},message,created_time,like_count,comment_count",
                    ["order"] = "reverse_chronological",
                    ["limit"] = MaxPostComments.ToString(CultureInfo.InvariantCulture),
                    ["summary"] = "true",
                },
                credential.PageAccessToken,
                ct).ConfigureAwait(false);

            var items = ParsePostComments(doc.RootElement);
            var total = ReadCommentSummaryTotal(doc.RootElement) ?? items.Count;
            return Results.Ok(new ContentPostCommentsResponse(
                id, items, total, total > items.Count, null));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Graph loi (token het han, page mat quyen...) khong duoc lam vo dialog.
            return Results.Ok(new ContentPostCommentsResponse(id, [], 0, false, "graph_unavailable"));
        }
    }

    private static List<ContentPostCommentDto> ParsePostComments(JsonElement root)
    {
        var items = new List<ContentPostCommentDto>();
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array)
        {
            return items;
        }

        foreach (var node in data.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object)
                continue;
            var commentId = node.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(commentId))
                continue;

            var author = node.TryGetProperty("from", out var fromEl)
                && fromEl.ValueKind == JsonValueKind.Object
                && fromEl.TryGetProperty("name", out var nameEl)
                && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()
                : null;
            var message = node.TryGetProperty("message", out var messageEl) && messageEl.ValueKind == JsonValueKind.String
                ? messageEl.GetString()
                : null;
            DateTimeOffset? createdAt = node.TryGetProperty("created_time", out var createdEl)
                && createdEl.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(createdEl.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;

            items.Add(new ContentPostCommentDto(
                commentId!,
                string.IsNullOrWhiteSpace(author) ? "Người dùng Facebook" : author!,
                message ?? string.Empty,
                createdAt,
                ReadIntProperty(node, "like_count") ?? 0,
                ReadIntProperty(node, "comment_count") ?? 0));
        }

        return items;
    }

    private static int? ReadCommentSummaryTotal(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("summary", out var summary)
        && summary.ValueKind == JsonValueKind.Object
        && summary.TryGetProperty("total_count", out var total)
        && total.TryGetInt32(out var value)
        && value >= 0
            ? value
            : null;

    private static int? ReadIntProperty(JsonElement node, string name) =>
        node.TryGetProperty(name, out var element) && element.TryGetInt32(out var value) && value >= 0
            ? value
            : null;

    private static async Task<IResult> PostPerformanceAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int? days,
        [FromQuery] string? platform,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var normalizedPlatform = string.IsNullOrWhiteSpace(platform)
            ? null
            : platform.Trim().ToLowerInvariant();
        if (normalizedPlatform is not null and not ("facebook" or "instagram"))
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.post_performance_platform_invalid",
                "platform must be facebook or instagram.");
        }

        var windowDays = NormalizePostPerformanceWindowDays(days);
        var response = await BuildPostPerformanceAsync(
            db,
            clock.UtcNow,
            windowDays,
            normalizedPlatform,
            ct).ConfigureAwait(false);
        return Results.Ok(response);
    }

    internal static int NormalizePostPerformanceWindowDays(int? days) =>
        days is >= 1 and <= 90 ? days.Value : 30;

    internal static async Task<ContentPostPerformanceResponse> BuildPostPerformanceAsync(
        AppDbContext db,
        DateTimeOffset to,
        int windowDays,
        string? platform,
        CancellationToken ct)
    {
        var from = to.AddDays(-windowDays);
        var schedules = db.ContentSchedules.AsNoTracking()
            .Where(schedule => schedule.Status == ContentSchedule.StatusPosted
                && schedule.PostedAt.HasValue
                && schedule.PostedAt >= from
                && schedule.PostedAt < to
                && (schedule.Platform == "facebook" || schedule.Platform == "instagram"));
        if (platform is not null)
            schedules = schedules.Where(schedule => schedule.Platform == platform);

        var aggregate = await schedules
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Posts = group.Count(),
                SyncedPosts = group.Count(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue),
                Likes = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.LikeCount),
                Comments = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.CommentCount),
                OldestEngagementAttemptAt = group.Where(schedule => schedule.EngagementSyncedAt.HasValue)
                    .Min(schedule => schedule.EngagementSyncedAt),
            })
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var totals = aggregate is null
            ? CreatePostPerformanceTotals(0, 0, null, null)
            : CreatePostPerformanceTotals(
                aggregate.Posts,
                aggregate.SyncedPosts,
                aggregate.Likes,
                aggregate.Comments);
        var freshness = new ContentPostPerformanceFreshnessDto(
            totals.SyncedPosts,
            totals.Posts - totals.SyncedPosts,
            aggregate?.OldestEngagementAttemptAt);

        var platformRows = await schedules
            .GroupBy(schedule => schedule.Platform)
            .Select(group => new
            {
                Platform = group.Key,
                Posts = group.Count(),
                SyncedPosts = group.Count(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue),
                Likes = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.LikeCount),
                Comments = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.CommentCount),
            })
            .OrderBy(row => row.Platform)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var byPlatform = platformRows
            .Select(row => new ContentPostPerformancePlatformDto(
                row.Platform,
                row.Posts,
                row.SyncedPosts,
                row.SyncedPosts > 0 ? row.Likes : null,
                row.SyncedPosts > 0 ? row.Comments : null,
                CalculateAverageEngagement(row.SyncedPosts, row.Likes, row.Comments)))
            .ToList();

        var targetRows = await schedules
            .GroupBy(schedule => schedule.MetaAssetId)
            .Select(group => new
            {
                MetaAssetId = group.Key,
                Posts = group.Count(),
                SyncedPosts = group.Count(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue),
                Likes = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.LikeCount),
                Comments = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.CommentCount),
            })
            .OrderByDescending(row => row.Posts)
            .ThenBy(row => row.MetaAssetId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var assetIds = targetRows
            .Where(row => row.MetaAssetId.HasValue)
            .Select(row => row.MetaAssetId!.Value)
            .ToList();
        var assetNames = assetIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.MetaAssets.AsNoTracking()
                .Where(asset => assetIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, asset => asset.Name, ct)
                .ConfigureAwait(false);
        var byTarget = targetRows
            .Select(row => new ContentPostPerformanceTargetDto(
                row.MetaAssetId,
                row.MetaAssetId is null
                    ? "Không xác định Page"
                    : assetNames.TryGetValue(row.MetaAssetId.Value, out var name)
                        ? name
                        : row.MetaAssetId.Value.ToString(),
                row.Posts,
                row.SyncedPosts,
                row.SyncedPosts > 0 ? row.Likes : null,
                row.SyncedPosts > 0 ? row.Comments : null,
                CalculateAverageEngagement(row.SyncedPosts, row.Likes, row.Comments)))
            .ToList();

        var dailyRows = await schedules
            .GroupBy(schedule => schedule.PostedAt!.Value.Date)
            .Select(group => new
            {
                Date = group.Key,
                Posts = group.Count(),
                SyncedPosts = group.Count(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue),
                Likes = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.LikeCount),
                Comments = group.Where(schedule => schedule.LikeCount.HasValue && schedule.CommentCount.HasValue)
                    .Sum(schedule => (long?)schedule.CommentCount),
            })
            .OrderBy(row => row.Date)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var daily = dailyRows
            .Select(row => new ContentPostPerformanceDailyDto(
                DateOnly.FromDateTime(row.Date),
                row.Posts,
                row.SyncedPosts,
                row.SyncedPosts > 0 ? row.Likes : null,
                row.SyncedPosts > 0 ? row.Comments : null))
            .ToList();

        var topRows = await schedules
            .Select(schedule => new
            {
                schedule.Id,
                schedule.ContentItemId,
                schedule.Platform,
                schedule.PostUrl,
                schedule.MetaAssetId,
                schedule.EngagementSyncedAt,
                schedule.ReactionsTotal,
                schedule.ReactionLove,
                schedule.ReactionHaha,
                schedule.ReactionWow,
                schedule.ReactionSad,
                schedule.ReactionAngry,
                schedule.ReactionCare,
                PostedAt = schedule.PostedAt!.Value,
                schedule.LikeCount,
                schedule.CommentCount,
                Total = schedule.LikeCount.HasValue && schedule.CommentCount.HasValue
                    ? (long?)schedule.LikeCount.Value + schedule.CommentCount.Value
                    : null,
            })
            .OrderByDescending(row => row.Total.HasValue)
            .ThenByDescending(row => row.Total)
            .ThenByDescending(row => row.PostedAt)
            .ThenBy(row => row.Id)
            .Take(10)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var topAssetIds = topRows
            .Where(row => row.MetaAssetId.HasValue)
            .Select(row => row.MetaAssetId!.Value)
            .Distinct()
            .ToList();
        var topAssetNames = topAssetIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.MetaAssets.AsNoTracking()
                .Where(asset => topAssetIds.Contains(asset.Id))
                .ToDictionaryAsync(asset => asset.Id, asset => asset.Name, ct)
                .ConfigureAwait(false);
        var topItemIds = topRows.Select(row => row.ContentItemId).Distinct().ToList();
        var itemBodies = topItemIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.ContentItems.AsNoTracking()
                .Where(item => topItemIds.Contains(item.Id) && item.DeletedAt == null)
                .ToDictionaryAsync(item => item.Id, item => item.Body, ct)
                .ConfigureAwait(false);
        var topPosts = topRows
            .Select(row => new ContentPostPerformanceTopPostDto(
                row.Id,
                row.ContentItemId,
                itemBodies.ContainsKey(row.ContentItemId),
                row.Platform,
                CreateContentExcerpt(itemBodies.GetValueOrDefault(row.ContentItemId)),
                IsTrustedSocialPostUrl(row.PostUrl) ? row.PostUrl : null,
                row.PostedAt,
                row.LikeCount,
                row.CommentCount,
                row.Total,
                row.MetaAssetId,
                row.MetaAssetId is null
                    ? "Không xác định Page"
                    : topAssetNames.TryGetValue(row.MetaAssetId.Value, out var topName)
                        ? topName
                        : row.MetaAssetId.Value.ToString(),
                row.EngagementSyncedAt,
                row.ReactionsTotal,
                row.ReactionLove,
                row.ReactionHaha,
                row.ReactionWow,
                row.ReactionSad,
                row.ReactionAngry,
                row.ReactionCare))
            .ToList();

        return new ContentPostPerformanceResponse(
            windowDays,
            from,
            to,
            totals,
            freshness,
            byPlatform,
            byTarget,
            daily,
            topPosts);
    }

    private static ContentPostPerformanceTotalsDto CreatePostPerformanceTotals(
        int posts,
        int syncedPosts,
        long? likes,
        long? comments) =>
        new(
            posts,
            syncedPosts,
            syncedPosts > 0 ? likes : null,
            syncedPosts > 0 ? comments : null,
            CalculateAverageEngagement(syncedPosts, likes, comments));

    private static double? CalculateAverageEngagement(int syncedPosts, long? likes, long? comments) =>
        syncedPosts > 0
            ? Math.Round(((likes ?? 0) + (comments ?? 0)) / (double)syncedPosts, 1)
            : null;

    private static bool IsTrustedSocialPostUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var url)
        && url.Scheme == Uri.UriSchemeHttps
        && url.IsDefaultPort
        && string.IsNullOrEmpty(url.UserInfo)
        && TrustedSocialPostHosts.Contains(url.Host);

    private static string CreateContentExcerpt(string? body)
    {
        var trimmed = body?.Trim();
        return string.IsNullOrEmpty(trimmed)
            ? "Không còn nội dung bài viết"
            : trimmed.Length <= 160
                ? trimmed
                : $"{trimmed[..157]}...";
    }

    private static async Task<IResult> ScheduleItemAsync(
        Guid id,
        ScheduleContentItemRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IGoldenHourResolver goldenHour,
        Clawbot.Infrastructure.Content.IContentAutoScheduler autoScheduler,
        IMetaIntegrationService metaIntegrations,
        IInstagramCredentialResolver instagramCredentials,
        HttpContext http,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var item = await db.ContentItems
            .FirstOrDefaultAsync(
                i => i.TenantId == tenant.TenantId && i.Id == id && i.DeletedAt == null,
                ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");
        var now = clock.UtcNow;
        // Calendar/manual schedule still allows an explicit time; default remains golden hour.
        // Durable path only: revision-bound approval context via ContentAutoScheduler — no gRPC review gate,
        // no ApprovedByAgentId, no RequireContentReview toggle.
        var resolution = ResolveScheduledAt(body, item, now, goldenHour);
        if (resolution.ErrorCode is not null)
            return Error(http, StatusCodes.Status400BadRequest, resolution.ErrorCode, resolution.Message ?? "Invalid schedule.");

        var isFacebook = string.Equals(item.Platform, "facebook", StringComparison.OrdinalIgnoreCase);
        var isInstagram = string.Equals(item.Platform, "instagram", StringComparison.OrdinalIgnoreCase);
        ContentSchedule? activeSchedule = null;
        if (!body.MetaAssetId.HasValue && (isFacebook || isInstagram))
        {
            activeSchedule = await db.ContentSchedules
                .IgnoreQueryFilters()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    schedule => schedule.TenantId == item.TenantId
                        && schedule.ContentItemId == item.Id
                        && schedule.ActiveRevisionSlot == item.ContentRevision,
                    ct)
                .ConfigureAwait(false);
        }

        if (activeSchedule?.RequiresInstagramTargetReselection() == true
            && !body.ConfirmInstagramAccount)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.instagram_target_reselection_required",
                "Instagram target must be explicitly reselected before this schedule can be changed.");
        }

        var preserveExistingTarget = activeSchedule is not null
            && (isFacebook
                ? activeSchedule.MetaAssetId.HasValue
                : !string.IsNullOrWhiteSpace(activeSchedule.ProviderTargetId));
        Guid? metaAssetId = null;
        string? providerTargetId = null;
        if (!preserveExistingTarget && isFacebook)
        {
            var pages = await metaIntegrations.GetPublishablePagesAsync(item.TenantId, ct).ConfigureAwait(false);
            var page = body.MetaAssetId.HasValue
                ? pages.FirstOrDefault(x => x.Id == body.MetaAssetId.Value)
                : pages.FirstOrDefault(x => x.IsDefault) ?? (pages.Count > 0 ? pages[0] : null);
            if (page is null)
                return Error(http, StatusCodes.Status400BadRequest, "content.meta_page_required", "Hãy kết nối và chọn Facebook Page trước khi lên lịch.");
            metaAssetId = page.Id;
        }
        else if (!preserveExistingTarget && isInstagram)
        {
            var standalone = await instagramCredentials.ResolveAsync(item.TenantId, ct).ConfigureAwait(false);
            if (standalone.Status == InstagramCredentialResolutionStatus.Invalid
                || (standalone.Status == InstagramCredentialResolutionStatus.Resolved
                    && standalone.Credential is null))
            {
                return Error(
                    http,
                    StatusCodes.Status400BadRequest,
                    "content.instagram_credentials_invalid",
                    "Thông tin Instagram độc lập không hợp lệ. Hãy sửa hoặc tắt ghi đè trong Quản trị hệ thống.");
            }

            if (standalone.Status == InstagramCredentialResolutionStatus.Resolved
                && standalone.Credential is not null)
            {
                if (body.MetaAssetId.HasValue)
                {
                    return Error(
                        http,
                        StatusCodes.Status400BadRequest,
                        "content.instagram_target_mode_conflict",
                        "Không thể chọn Meta Page khi ghi đè Instagram độc lập đang được bật.");
                }

                providerTargetId = standalone.Credential.InstagramUserId;
            }
            else
            {
                if (!body.MetaAssetId.HasValue)
                    return Error(http, StatusCodes.Status400BadRequest, "content.instagram_target_required", "Hãy chọn Meta Page đã liên kết Instagram trước khi lên lịch.");

                var instagram = await metaIntegrations
                    .ResolveInstagramAsync(item.TenantId, body.MetaAssetId.Value, ct)
                    .ConfigureAwait(false);
                if (instagram.Status != MetaInstagramResolutionStatus.Resolved || instagram.Credential is null)
                {
                    var errorCode = instagram.Status switch
                    {
                        MetaInstagramResolutionStatus.ReconnectRequired => "content.instagram_reconnect_required",
                        MetaInstagramResolutionStatus.MissingScopes => "content.instagram_permissions_missing",
                        MetaInstagramResolutionStatus.NotLinked => "content.instagram_not_linked",
                        MetaInstagramResolutionStatus.PageUnavailable => "content.instagram_target_unavailable",
                        _ => "content.instagram_meta_unavailable",
                    };
                    return Error(http, StatusCodes.Status400BadRequest, errorCode, "Meta Page đã chọn chưa sẵn sàng để đăng Instagram.");
                }

                metaAssetId = instagram.Credential.PageAssetId;
                providerTargetId = instagram.Credential.InstagramUserId;
            }
        }
        else if (!preserveExistingTarget && body.MetaAssetId.HasValue)
        {
            return Error(http, StatusCodes.Status400BadRequest, "content.meta_page_invalid", "Meta Page chỉ áp dụng cho nội dung Facebook hoặc Instagram.");
        }

        try
        {
            var schedule = await autoScheduler.CreateIntentAsync(
                item,
                publishTargetId: metaAssetId,
                at: now,
                desiredPublishAt: resolution.ScheduledAt,
                providerTargetId: providerTargetId,
                cancellationToken: ct).ConfigureAwait(false);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Created($"/api/content/schedule/{schedule.Id}", ToDto(schedule));
        }
        catch (InvalidOperationException exception) when (exception.Message == "content_current_revision_not_schedulable")
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.item_not_schedulable",
                "Only content with a completed Agent review and publishing approval for the current revision can be scheduled.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "content_approval_context_missing")
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.approval_context_missing",
                "Current content revision is missing publishing approval context.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "content_schedule_canceled_by_user")
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.schedule_canceled_by_user",
                "User-canceled schedule for this revision cannot be recreated automatically. Create a new revision or explicit reschedule flow.");
        }
        catch (InvalidOperationException exception) when (
            exception.Message == "content_schedule_instagram_target_reselection_required")
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.instagram_target_reselection_required",
                "Instagram target must be explicitly reselected before this schedule can be changed.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "content_schedule_in_past")
        {
            return Error(http, StatusCodes.Status400BadRequest, "content.schedule_in_past", "scheduledAt must be in the future.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(http, StatusCodes.Status409Conflict, "content.revision_changed", "Content revision changed.");
        }
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
            .Where(s => s.ScheduledAt >= fromValue
                && s.ScheduledAt < toValue
                && s.Status != ContentSchedule.StatusPosted
                && s.Status != ContentSchedule.StatusCanceled)
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
        IInstagramCredentialResolver instagramCredentials,
        HttpContext http,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var normalizedPlatform = platform?.Trim().ToLowerInvariant();
        if (normalizedPlatform is not ("facebook" or "instagram"))
        {
            http.Response.Headers["X-Clawbot-Publish-Target-Mode"] = "unsupported";
            return Results.Ok(Array.Empty<ContentPublishTargetDto>());
        }

        if (normalizedPlatform == "instagram")
        {
            var standalone = await instagramCredentials.ResolveAsync(tenant.TenantId, ct).ConfigureAwait(false);
            if (standalone.Status == InstagramCredentialResolutionStatus.Invalid
                || (standalone.Status == InstagramCredentialResolutionStatus.Resolved
                    && standalone.Credential is null))
            {
                http.Response.Headers["X-Clawbot-Publish-Target-Mode"] = "invalid";
                return Results.Ok(Array.Empty<ContentPublishTargetDto>());
            }
            if (standalone.Status == InstagramCredentialResolutionStatus.Resolved)
            {
                http.Response.Headers["X-Clawbot-Publish-Target-Mode"] = "standalone";
                return Results.Ok(Array.Empty<ContentPublishTargetDto>());
            }
        }

        var pages = await metaIntegrations.GetPublishablePagesAsync(tenant.TenantId, ct).ConfigureAwait(false);
        var targets = pages.Select(x => new ContentPublishTargetDto(
            x.Id,
            normalizedPlatform,
            x.ExternalId,
            x.Name,
            x.IsDefault)).ToList();
        http.Response.Headers["X-Clawbot-Publish-Target-Mode"] = "linked_meta";
        return Results.Ok(targets);
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
        if (schedule.Status is not (ContentSchedule.StatusPending or ContentSchedule.StatusHeld or ContentSchedule.StatusFailed))
            return Error(http, StatusCodes.Status400BadRequest, "content.schedule_not_cancelable", "Only pending, held, or failed schedules can be canceled.");

        schedule.Cancel(clock.UtcNow, "canceled_by_user");
        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == schedule.ContentItemId && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is not null && string.Equals(item.Status, "scheduled", StringComparison.OrdinalIgnoreCase))
            item.RevertToApproved(clock.UtcNow);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    // Phase 4.5: retry the durable review task for the current revision — never runs LLM inline.
    private static async Task<IResult> RetryAgentReviewAsync(
        Guid id,
        RetryAgentReviewRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        if (body is null || body.ExpectedRevision <= 0)
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.expected_revision_required",
                "expectedRevision is required.");
        }

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");
        if (item.ContentRevision != body.ExpectedRevision)
            return Error(http, StatusCodes.Status409Conflict, "content.revision_changed", "Content revision changed.");
        if (item.Status != "draft")
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.review_retry_not_draft",
                "Only draft content can re-enter agent review.");
        }
        if (item.ActivePublishAttemptId is not null)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content_publish_attempt_active",
                "Actively-claimed content cannot retry agent review.");
        }
        if (item.AgentReviewStatus == ContentItem.ReviewStatusRunning)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.review_in_progress",
                "Agent review is already running for this item.");
        }

        var now = clock.UtcNow;
        var task = await db.ContentReviewTasks
            .Where(candidate => candidate.TenantId == tenantId
                && candidate.ContentItemId == item.Id
                && candidate.ContentRevision == item.ContentRevision)
            .SingleOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (task?.Status == ContentReviewTask.StatusPending)
        {
            if (task.NextAttemptAt > now)
            {
                return Error(
                    http,
                    StatusCodes.Status429TooManyRequests,
                    "content.review_retry_cooldown",
                    "Agent review retry is cooling down for this item.");
            }

            item.ClaimOrchestrationOwnershipForHuman(CurrentUserId(http), now);
            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Error(
                    http,
                    StatusCodes.Status409Conflict,
                    "content.review_retry_conflict",
                    "Content review changed concurrently. Refresh and try again.");
            }

            return Results.Ok(ToDto(item));
        }

        if (task?.Status == ContentReviewTask.StatusLeased)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.review_in_progress",
                "Agent review is already running for this item.");
        }

        try
        {
            item.ClaimOrchestrationOwnershipForHuman(CurrentUserId(http), now);
            item.ReopenAgentReview(now);
            if (task is null)
            {
                // Legacy repair path only: normal revisions always have their one durable task already.
                db.ContentReviewTasks.Add(ContentAssetLifecycle.CreateQuietPeriodReviewTask(
                    tenantId,
                    item.Id,
                    item.ContentRevision,
                    now));
            }
            else
            {
                var previousStatus = task.Status;
                var previousErrorCode = task.LastErrorCode;
                var previousAttemptCount = task.AttemptCount;
                var previousReviewCycle = task.ReviewCycle;
                task.ReopenForManualRetry(now);
                db.AuditLogs.Add(AuditLog.CreateBusinessEvent(
                    tenantId,
                    CurrentUserId(http),
                    "content.agent_review.manual_retry",
                    "content_item",
                    item.Id,
                    now,
                    $"content-review:{task.Id:N}:manual-retry:{task.ReviewCycle}",
                    task.ReviewCycle,
                    JsonSerializer.Serialize(
                        new
                        {
                            reviewTaskId = task.Id,
                            expectedRevision = task.ContentRevision,
                            previousStatus,
                            previousErrorCode,
                            previousAttemptCount,
                            previousReviewCycle,
                            reviewCycle = task.ReviewCycle,
                        },
                        WebJsonOptions)));
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(ToDto(item));
        }
        catch (InvalidOperationException exception)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                exception.Message,
                "Content item cannot re-enter agent review.");
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error(
                http,
                StatusCodes.Status409Conflict,
                "content.review_retry_conflict",
                "Content review changed concurrently. Refresh and try again.");
        }
        catch (DbUpdateException exception) when (IsReviewTaskUniqueViolation(exception))
        {
            // Two manual retries can race only for legacy data without a task. Treat the collision as
            // idempotent success only after proving the other request established the expected state.
            db.ChangeTracker.Clear();
            var refreshed = await db.ContentItems.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(candidate => candidate.Id == id
                    && candidate.TenantId == tenantId
                    && candidate.DeletedAt == null, ct)
                .ConfigureAwait(false);
            var refreshedTask = refreshed is null
                ? null
                : await db.ContentReviewTasks.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.TenantId == tenantId
                        && candidate.ContentItemId == refreshed.Id
                        && candidate.ContentRevision == refreshed.ContentRevision, ct)
                    .ConfigureAwait(false);
            if (refreshed is null
                || refreshed.ContentRevision != body.ExpectedRevision
                || refreshed.Status != "draft"
                || refreshed.AgentReviewStatus != ContentItem.ReviewStatusPending
                || (refreshed.OrchestrationSessionId is not null
                    && refreshed.OrchestrationOwnershipClaimedAt is null)
                || refreshedTask?.Status is not (ContentReviewTask.StatusPending or ContentReviewTask.StatusLeased))
            {
                throw;
            }

            return Results.Ok(ToDto(refreshed));
        }
    }

    // Phase 4.6: reset durable schedule state only — Hangfire ContentPublishJob transmits later.
    private static async Task<IResult> RetryPublishScheduleAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var schedule = await db.ContentSchedules
            .FirstOrDefaultAsync(s => s.TenantId == tenant.TenantId && s.Id == id, ct)
            .ConfigureAwait(false);
        if (schedule is null)
            return Error(http, StatusCodes.Status404NotFound, "content.schedule_not_found", "Content schedule not found.");
        if (schedule.RequiresInstagramTargetReselection())
        {
            return Error(
                http,
                StatusCodes.Status422UnprocessableEntity,
                "content.instagram_target_reselection_required",
                "Instagram target must be reselected before publishing can be retried.");
        }

        if (!schedule.TryResetForRetry(clock.UtcNow))
        {
            return Error(
                http,
                StatusCodes.Status422UnprocessableEntity,
                "content.schedule_not_retryable",
                "Chỉ thử lại được lịch đang chờ đăng, bị giữ, hoặc đã thất bại. outcome_unknown cần reconcile.");
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(schedule));
    }

    // Phase 4.6: privileged operator decision after verification — never calls the provider.
    private static async Task<IResult> ReconcilePublishScheduleAsync(
        Guid id,
        ReconcilePublishRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        if (body is null || string.IsNullOrWhiteSpace(body.Outcome))
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.reconcile_outcome_required",
                "outcome is required (succeeded|failed).");
        }

        var outcome = body.Outcome.Trim().ToLowerInvariant();
        if (outcome is not ("succeeded" or "failed"))
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "content.reconcile_outcome_invalid",
                "outcome must be succeeded or failed.");
        }

        var schedule = await db.ContentSchedules.FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);
        if (schedule is null)
            return Error(http, StatusCodes.Status404NotFound, "content.schedule_not_found", "Content schedule not found.");
        if (!string.Equals(schedule.Status, ContentSchedule.StatusOutcomeUnknown, StringComparison.Ordinal))
        {
            return Error(
                http,
                StatusCodes.Status422UnprocessableEntity,
                "content.schedule_not_outcome_unknown",
                "Only outcome_unknown schedules can be reconciled.");
        }

        var item = await db.ContentItems.FirstOrDefaultAsync(i => i.Id == schedule.ContentItemId && i.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (item is null)
            return Error(http, StatusCodes.Status404NotFound, "content.item_not_found", "Content item not found.");

        var attempt = await db.ContentPublishAttempts
            .Where(a => a.ScheduleId == schedule.Id
                && a.Status == ContentPublishAttempt.StatusOutcomeUnknown)
            .OrderByDescending(a => a.CompletedAt)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        try
        {
            if (outcome == "succeeded")
            {
                if (!string.IsNullOrWhiteSpace(body.ExternalPostId)
                    && !ContentSchedule.IsValidExternalPostId(body.ExternalPostId))
                {
                    return Error(
                        http,
                        StatusCodes.Status400BadRequest,
                        "content.external_post_id_invalid",
                        "External post ID contains invalid path characters.");
                }

                var externalId = string.IsNullOrWhiteSpace(body.ExternalPostId)
                    ? schedule.ExternalPostId
                    : body.ExternalPostId.Trim();
                if (!ContentSchedule.IsValidExternalPostId(externalId))
                {
                    return Error(
                        http,
                        StatusCodes.Status400BadRequest,
                        "content.external_post_id_required",
                        "A valid provider post ID is required to reconcile a successful publication.");
                }

                attempt?.ReconcileSucceeded(externalId!, now);
                schedule.MarkReconciledPosted(schedule.PostUrl ?? string.Empty, externalId!, now);
                if (item.ActivePublishAttemptId is not null)
                    item.MarkPublished(item.ActivePublishAttemptId.Value, now);
                else if (item.Status != "published")
                    item.MarkPublished(now);
            }
            else
            {
                var errorCode = string.IsNullOrWhiteSpace(body.ErrorCode)
                    ? "publish_reconciled_failed"
                    : body.ErrorCode.Trim();
                attempt?.ReconcileFailed(errorCode, now);
                schedule.MarkReconciledFailed(now, errorCode);
                if (item.ActivePublishAttemptId is not null)
                    item.ReleasePublishAttempt(item.ActivePublishAttemptId.Value, now);
            }
        }
        catch (InvalidOperationException exception)
        {
            return Error(http, StatusCodes.Status422UnprocessableEntity, exception.Message, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Error(http, StatusCodes.Status400BadRequest, exception.Message, exception.Message);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(schedule));
    }

    private static async Task<IResult> TrendsAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] string? week,
        [FromQuery] bool? includeRaw,
        HttpContext http,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var normalizedWeek = string.Empty;
        var query = db.ContentBriefs.AsNoTracking()
            .Where(b => b.Brief.StartsWith("[trend:"));

        if (!string.IsNullOrWhiteSpace(week))
        {
            if (!ContentTrendBriefFormatter.TryNormalizeWeekOf(week, out normalizedWeek))
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

        IReadOnlyList<RawTrendDto>? rawTrends = null;
        if (includeRaw == true)
        {
            var rawPrefix = string.IsNullOrWhiteSpace(week) ? "[trend-raw:" : $"[trend-raw:{normalizedWeek}]";
            var rawBriefs = await db.ContentBriefs.AsNoTracking()
                .Where(item => item.TenantId == tenantId && item.Brief.StartsWith(rawPrefix))
                .OrderByDescending(item => item.UpdatedAt)
                .Select(item => item.Brief)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            rawTrends = ParseRawTrends(rawBriefs);
        }

        return Results.Ok(new TrendScanResponse(trends, rawTrends));
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

            var requestedPlatform = string.IsNullOrWhiteSpace(body.Platform) ? brief.Platform : body.Platform;
            if (!ContentPlatformCatalog.TryNormalizeWritable(requestedPlatform, out var platform))
            {
                return new GenerateInput(null, string.Empty, string.Empty, UnsupportedPlatform(http));
            }

            return new GenerateInput(brief.Id, platform!, brief.Brief, null);
        }

        if (string.IsNullOrWhiteSpace(body.Platform) || string.IsNullOrWhiteSpace(body.BriefText))
        {
            return new GenerateInput(null, string.Empty, string.Empty,
                Error(http, StatusCodes.Status400BadRequest, "content.generate_invalid", "briefId or platform and briefText required."));
        }
        if (!ContentPlatformCatalog.TryNormalizeWritable(body.Platform, out var normalizedPlatform))
            return new GenerateInput(null, string.Empty, string.Empty, UnsupportedPlatform(http));

        return new GenerateInput(null, normalizedPlatform!, body.BriefText.Trim(), null);
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

    private static ContentItemDto ToDto(ContentItem item)
    {
        var agentReview = new ContentAgentReviewDto(
            item.AgentReviewStatus,
            item.AgentReviewedRevision,
            item.ReviewedByAgentId,
            item.AgentReviewedAt,
            item.AgentReviewReason,
            item.ImageReviewStatus,
            item.ReviewedImageCount);

        var publishingStatus = ResolvePublishingApprovalStatus(item);
        var publishingApproval = new ContentPublishingApprovalDto(
            publishingStatus,
            item.PublishingPolicyApplied,
            item.PublishingPolicyVersionApplied,
            item.ApprovedRevision,
            item.ApprovalMode,
            item.ApprovedBy,
            item.ApprovedAt,
            item.ApprovalReason,
            item.HumanApprovalRequirementReason);

        var workflowState = ResolveWorkflowState(item);
        var reviewCompleteForCurrent =
            item.AgentReviewedRevision == item.ContentRevision
            && item.AgentReviewStatus is ContentItem.ReviewStatusPassed
                or ContentItem.ReviewStatusRejected
                or ContentItem.ReviewStatusNeedsHuman
                or ContentItem.ReviewStatusFailed;
        var scheduleBlockedReason = ScheduleBlockedReason(item);

        return new ContentItemDto(
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
            item.UpdatedAt,
            item.ContentRevision,
            agentReview,
            publishingApproval,
            workflowState,
            CanApprove: reviewCompleteForCurrent
                && item.Status == "draft"
                && item.DeletedAt is null
                && item.ActivePublishAttemptId is null,
            CanReject: reviewCompleteForCurrent
                && item.Status is "draft" or "approved"
                && item.DeletedAt is null
                && item.ActivePublishAttemptId is null,
            // Cạn lượt không còn ẩn nút: retry giờ mở lại một chu kỳ mới (ReopenAgentReview) thay vì trả 429.
            CanRetryReview: item.Status is not "published" and not "rejected"
                && item.DeletedAt is null
                && item.ActivePublishAttemptId is null
                && item.AgentReviewStatus is not ContentItem.ReviewStatusRunning,
            CanSchedule: scheduleBlockedReason is null,
            CanPublish: item.CanPublishCurrentRevision(),
            ScheduleBlockedReason: scheduleBlockedReason);
    }

    private static string? ScheduleBlockedReason(ContentItem item)
    {
        if (!item.CanScheduleCurrentRevision()) return "content.item_not_schedulable";
        return string.IsNullOrWhiteSpace(item.ApprovalMode)
            || item.PublishingPolicyVersionApplied is null
            || item.ApprovedRevision != item.ContentRevision
            ? "content.approval_context_missing"
            : null;
    }

    private static string ResolvePublishingApprovalStatus(ContentItem item)
    {
        if (item.Status == "rejected")
            return "rejected";
        if (item.ApprovedRevision == item.ContentRevision)
            return "approved";
        if (item.AgentReviewedRevision == item.ContentRevision
            && item.AgentReviewStatus is ContentItem.ReviewStatusPassed
                or ContentItem.ReviewStatusRejected
                or ContentItem.ReviewStatusNeedsHuman
                or ContentItem.ReviewStatusFailed)
        {
            return "pending";
        }

        return "not_ready";
    }

    // Định nghĩa nằm ở domain để AgentService (tool content.list) dùng chung đúng một quy tắc.
    private static string ResolveWorkflowState(ContentItem item) => item.ResolveWorkflowState();

    private static async Task CancelStaleScheduleIntentsAsync(
        AppDbContext db,
        Guid contentItemId,
        int previousRevision,
        DateTimeOffset at,
        CancellationToken ct)
    {
        var stale = await db.ContentSchedules
            .Where(s => s.ContentItemId == contentItemId
                && s.ContentRevision == previousRevision
                && (s.Status == ContentSchedule.StatusPending
                    || s.Status == ContentSchedule.StatusHeld
                    || s.Status == ContentSchedule.StatusFailed))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var schedule in stale)
            schedule.Cancel(at, "stale_content_revision");
    }

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
            schedule.EngagementSyncedAt,
            schedule.RetryCount,
            schedule.LastError);

    internal static IReadOnlyList<ContentCalendarItemDto> BuildCalendarRows(
        IReadOnlyList<ContentSchedule> schedules,
        IReadOnlyDictionary<Guid, ContentItem> itemsById) =>
        schedules
            .Where(s => s.Status is not (ContentSchedule.StatusPosted or ContentSchedule.StatusCanceled)
                && itemsById.TryGetValue(s.ContentItemId, out var item)
                && !string.Equals(item.Status, "published", StringComparison.OrdinalIgnoreCase))
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
                    s.CommentCount,
                    s.RetryCount,
                    s.LastError,
                    s.RequiresInstagramTargetReselection());
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

    private static string ItemAssetUrl(Guid itemId, Guid assetId) =>
        $"/api/content/items/{itemId:D}/assets/{assetId:D}";

    internal static string? ResolveAssetContentType(ReadOnlySpan<byte> bytes, string? suppliedContentType)
    {
        var normalized = suppliedContentType?
            .Split(';', 2, StringSplitOptions.TrimEntries)[0]
            .Trim()
            .ToLowerInvariant();
        normalized = normalized == "image/jpg" ? "image/jpeg" : normalized;

        if (normalized is "image/png" or "image/jpeg" or "image/gif" or "image/webp")
            return LooksLikeAllowedImage(bytes, normalized) ? normalized : null;

        return string.IsNullOrWhiteSpace(normalized) || normalized == "application/octet-stream"
            ? DetectImageContentType(bytes)
            : null;
    }

    private static string? DetectImageContentType(ReadOnlySpan<byte> bytes)
    {
        foreach (var contentType in AllowedAssetContentTypes)
        {
            if (LooksLikeAllowedImage(bytes, contentType)) return contentType;
        }
        return null;
    }

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

    private static string ResolveAssetExtension(string contentType) => contentType switch
    {
        "image/gif" => ".gif",
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        _ => string.Empty,
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

    private static List<RawTrendDto> ParseRawTrends(IEnumerable<string> briefs) =>
        briefs
            .SelectMany(ParseRawTrends)
            .GroupBy(item => $"{item.Topic}{item.Source}{item.Metric}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.SourceScore).First())
            .OrderByDescending(item => item.SourceScore)
            .ThenBy(item => item.Topic, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<RawTrendDto> ParseRawTrends(string? brief)
    {
        if (string.IsNullOrWhiteSpace(brief)) return [];
        var jsonStart = brief.IndexOf('\n');
        if (jsonStart < 0) return [];
        try
        {
            var raw = JsonSerializer.Deserialize<List<Clawbot.Agents.Core.Research.RawTrend>>(
                brief[(jsonStart + 1)..],
                WebJsonOptions);
            return raw?.Select(item => new RawTrendDto(
                item.Topic,
                item.Source,
                item.Metric,
                item.SourceScore,
                item.ContentIdeas.ToList())).ToList() ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

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

    private static bool IsReviewTaskUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 } sqlException
        && sqlException.Message.Contains(
            "UX_content_review_tasks_item_revision",
            StringComparison.Ordinal);

    private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
        Results.Json(
            new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier },
            statusCode: statusCode);

    private static IResult UnsupportedPlatform(HttpContext http) =>
        Error(
            http,
            StatusCodes.Status400BadRequest,
            "content.platform_unsupported",
            "platform must be facebook, zalo, or instagram.");

    private sealed record GenerateInput(Guid? BriefId, string Platform, string Brief, IResult? Error);

    internal sealed record ScheduleResolution(DateTimeOffset ScheduledAt, string? ErrorCode, string? Message);

    private sealed record ContentApiError(string ErrorCode, string Message, string RequestId);
}
