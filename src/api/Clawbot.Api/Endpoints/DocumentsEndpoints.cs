using Clawbot.Agents.Contracts.Docs;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Documents;
using Clawbot.Api.Jobs;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Jobs;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class DocumentsEndpoints
{
    private static readonly Action<ILogger, Guid, string, Exception?> LogMissingStoredDocument =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(1, nameof(LogMissingStoredDocument)),
            "Generated document {DocumentId} is missing from storage (key {StorageKey})");

    private static readonly byte[] TransparentGif =
        Convert.FromBase64String("R0lGODlhAQABAPAAAP///wAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==");

    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/docs").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapPost("/generate", GenerateAsync).RequirePermission("docs:write");
        grp.MapPost("/generate-kit", GenerateKitAsync).RequirePermission("docs:write");

        grp.MapGet("/templates", ListTemplatesAsync).RequirePermission("docs:read");
        grp.MapPost("/templates", CreateTemplateAsync).RequirePermission("docs:write");
        grp.MapPut("/templates/{id:guid}", UpdateTemplateAsync).RequirePermission("docs:write");
        grp.MapDelete("/templates/{id:guid}", DeleteTemplateAsync).RequirePermission("docs:write");

        grp.MapGet("/generated", ListGeneratedAsync).RequirePermission("docs:read");
        grp.MapGet("/{id:guid}/download", DownloadAsync).RequirePermission("docs:read");

        app.MapGet("/api/docs/{id:guid}/open.gif", OpenBeaconAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        return app;
    }

    private static async Task<IResult> OpenBeaconAsync(
        Guid id,
        DocumentOpenReceiptService receipts,
        HttpContext http,
        CancellationToken ct)
    {
        await receipts.RecordOpenAsync(id, ct).ConfigureAwait(false);
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.Expires = "0";
        return Results.File(TransparentGif, "image/gif");
    }

    private static async Task<IResult> DownloadAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        Clawbot.Agents.Core.Docs.IDocumentStorage storage,
        Clawbot.Agents.Core.Docs.DocsStorageOptions storageOptions,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);
        if (doc is null) return Results.NotFound();
        if (doc.IsExpired(clock.UtcNow))
            return Results.Problem(statusCode: StatusCodes.Status410Gone, detail: "Document download link has expired.");

        // FileUrl tuyệt đối (presigned MinIO) thì redirect như cũ. Ngược lại đó là key nội bộ dạng
        // "/generated-docs/<file>.pdf" — không host nào phục vụ đường dẫn này, redirect rơi vào SPA và
        // react-router bắn "404 Not Found". Đọc bytes qua IDocumentStorage và stream thẳng, giữ nguyên cổng docs:read.
        if (Uri.TryCreate(doc.FileUrl, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            doc.MarkOpened(clock.UtcNow);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Redirect(doc.FileUrl);
        }

        var storageKey = ResolveStorageKey(doc.FileUrl, storageOptions.PublicBaseUrl);
        byte[] bytes;
        try
        {
            bytes = await storage.ReadAsync(storageKey, ct).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            LogMissingStoredDocument(
                loggerFactory.CreateLogger(typeof(DocumentsEndpoints)),
                doc.Id,
                storageKey,
                exception);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                detail: "Generated file is no longer available in document storage.");
        }

        doc.MarkOpened(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var fileName = Path.GetFileName(storageKey);
        // inline: iframe xem trước hiển thị luôn thay vì ép tải về.
        return Results.File(bytes, "application/pdf", fileName, enableRangeProcessing: true);
    }

    // FileUrl được lưu là PublicBaseUrl + "/" + key. Cắt prefix để lấy lại đúng key lúc save.
    private static string ResolveStorageKey(string fileUrl, string publicBaseUrl)
    {
        var trimmed = (fileUrl ?? string.Empty).Trim();
        var prefix = (publicBaseUrl ?? string.Empty).TrimEnd('/') + "/";
        if (prefix.Length > 1 && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return trimmed[prefix.Length..];
        return trimmed.TrimStart('/');
    }

    // Sinh tài liệu chạy ngầm — trả jobId ngay, thông báo khi xong (link /documents).
    private static async Task<IResult> GenerateAsync(
        GenerateDocumentRequest body,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.TemplateCode))
            return Results.BadRequest(new { error = "templateCode required" });

        var deliveryTarget = DocumentDeliveryTargetValidator.Validate(
            body.SentVia,
            body.ContactId,
            body.RecipientEmail);
        if (!deliveryTarget.IsValid)
            return Results.BadRequest(new { error = deliveryTarget.Error });

        var jobId = await jobs.LaunchAsync(
            DocsGenerateJobHandler.JobType,
            $"Sinh tài liệu {body.TemplateCode.Trim()}",
            new DocsGenerateJobPayload(
                body.TemplateCode.Trim(),
                body.ContactId,
                body.Vars,
                body.SentVia,
                deliveryTarget.RecipientEmail),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    // Bộ tài liệu: luồng nặng nhất (nhiều doc/1 lần) — job có tiến độ theo từng doc.
    private static async Task<IResult> GenerateKitAsync(
        GenerateDocumentKitRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var deliveryTarget = DocumentDeliveryTargetValidator.Validate(
            body.SentVia,
            body.ContactId,
            body.RecipientEmail);
        if (!deliveryTarget.IsValid)
            return Results.BadRequest(new { error = deliveryTarget.Error });

        var templateCodes = NormalizeKitTemplateCodes(body.TemplateCodes);
        if (templateCodes.Length == 0)
        {
            // Không chỉ định mã -> lấy toàn bộ mẫu hiện có của tenant (tối đa 10), thay cho danh sách hardcode cũ.
            templateCodes = await db.DocumentTemplates.AsNoTracking()
                .Where(t => t.DeletedAt == null)
                .OrderBy(t => t.Code)
                .Select(t => t.Code)
                .Take(10)
                .ToArrayAsync(ct).ConfigureAwait(false);
        }
        if (templateCodes.Length == 0)
            return Results.BadRequest(new { error = "Chưa có mẫu tài liệu nào để tạo bộ." });

        var jobId = await jobs.LaunchAsync(
            DocsKitJobHandler.JobType,
            $"Sinh bộ {templateCodes.Length} tài liệu",
            new DocsKitJobPayload(
                templateCodes,
                body.ContactId,
                body.Vars,
                body.SentVia,
                deliveryTarget.RecipientEmail),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

    private static List<TemplateFieldDto> MapFields(IReadOnlyList<TemplateField> fields) =>
        fields
            .Select(f => new TemplateFieldDto(f.Key, f.Label, f.Type, f.Required, f.Placeholder, f.Sample))
            .ToList();

    private static string SerializeFields(IReadOnlyList<TemplateFieldDto>? fields)
    {
        if (fields is null || fields.Count == 0)
            return "[]";
        var domain = fields
            .Where(f => !string.IsNullOrWhiteSpace(f.Key))
            .Select(f => new TemplateField(
                f.Key.Trim(),
                string.IsNullOrWhiteSpace(f.Label) ? f.Key.Trim() : f.Label.Trim(),
                TemplateField.NormalizeType(f.Type),
                f.Required,
                f.Placeholder,
                f.Sample))
            .ToList();
        return TemplateFieldSchema.Serialize(domain);
    }

    private static string[] NormalizeKitTemplateCodes(IReadOnlyList<string>? templateCodes)
    {
        if (templateCodes is not { Count: > 0 })
            return [];
        return templateCodes
            .Select(code => code.Trim())
            .Where(code => code.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }

    internal static async Task<GenerateDocumentResponse> GenerateOneAsync(
        Guid tenantId,
        string templateCode,
        Guid? contactId,
        IReadOnlyDictionary<string, string>? vars,
        string? sentVia,
        string? recipientEmail,
        DocsAgent.DocsAgentClient grpc,
        DocumentDeliveryService delivery,
        CancellationToken ct)
    {
        var req = new DocGenerateRequest
        {
            TenantId = tenantId.ToString(),
            ContactId = contactId?.ToString() ?? string.Empty,
            TemplateCode = templateCode,
            SentVia = sentVia ?? string.Empty,
        };
        if (vars is not null)
        {
            foreach (var kv in vars)
                req.Vars[kv.Key] = kv.Value;
        }

        var resp = await grpc.GenerateAsync(req, cancellationToken: ct);
        var documentId = Guid.Parse(resp.DocumentId);

        if (!string.IsNullOrWhiteSpace(sentVia))
        {
            var delivered = await delivery
                .TrySendAsync(tenantId, documentId, sentVia, recipientEmail, ct).ConfigureAwait(false);
            // Thông báo hiện thẳng lên UI nên phải nói được người dùng cần làm gì, không dùng câu kỹ thuật.
            if (!delivered)
                throw new InvalidOperationException(
                    "Đã tạo tài liệu nhưng chưa gửi được. Kiểm tra lại email người nhận, hoặc tải tài liệu ở Thư viện rồi gửi thủ công.");
        }

        return new GenerateDocumentResponse(documentId, resp.FileUrl, resp.FileHash, resp.SizeBytes, resp.LatencyMs);
    }

    private static async Task<IResult> ListTemplatesAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;
        var query = db.DocumentTemplates.AsNoTracking().Where(t => t.DeletedAt == null);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        // Parse fields_json xảy ra trong bộ nhớ (EF không phân giải JSON được), nên project ra bản ghi thô trước.
        var rows = await query
            .OrderBy(t => t.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new { t.Id, t.Code, t.DocType, t.TemplateHtml, t.FieldsJson, t.CreatedAt, t.UpdatedAt })
            .ToListAsync(ct).ConfigureAwait(false);
        var items = rows
            .Select(t => new DocumentTemplateDto(
                t.Id, t.Code, t.DocType, t.TemplateHtml,
                MapFields(TemplateFieldSchema.Parse(t.FieldsJson)), t.CreatedAt, t.UpdatedAt))
            .ToList();
        return Results.Ok(new { items, total, page, pageSize });
    }

    private static async Task<IResult> CreateTemplateAsync(
        CreateDocumentTemplateRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.TemplateHtml))
            return Results.BadRequest(new { error = "code and templateHtml required" });

        var exists = await db.DocumentTemplates
            .AnyAsync(t => t.Code == body.Code && t.DeletedAt == null, ct).ConfigureAwait(false);
        if (exists) return Results.Conflict(new { error = "code already exists" });

        var docType = string.IsNullOrWhiteSpace(body.DocType) ? "quote" : body.DocType;
        var fieldsJson = SerializeFields(body.Fields);
        var tpl = DocumentTemplate.Create(tenant.TenantId, body.Code, docType, body.TemplateHtml, clock.UtcNow, fieldsJson);
        db.DocumentTemplates.Add(tpl);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/docs/templates/{tpl.Id}",
            new DocumentTemplateDto(tpl.Id, tpl.Code, tpl.DocType, tpl.TemplateHtml,
                MapFields(TemplateFieldSchema.Parse(tpl.FieldsJson)), tpl.CreatedAt, tpl.UpdatedAt));
    }

    private static async Task<IResult> UpdateTemplateAsync(
        Guid id,
        UpdateDocumentTemplateRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        // Nội dung rỗng sẽ xóa trắng mẫu đang dùng — chặn như lúc tạo mới.
        if (string.IsNullOrWhiteSpace(body.TemplateHtml))
            return Results.BadRequest(new { error = "templateHtml required" });

        var tpl = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct).ConfigureAwait(false);
        if (tpl is null) return Results.NotFound();

        // Fields == null nghĩa là client không đụng tới schema trường -> giữ nguyên bản cũ.
        var fieldsJson = body.Fields is null ? null : SerializeFields(body.Fields);
        tpl.Update(body.DocType, body.TemplateHtml, fieldsJson, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteTemplateAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var tpl = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct).ConfigureAwait(false);
        if (tpl is null) return Results.NotFound();

        db.Entry(tpl).Property("DeletedAt").CurrentValue = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListGeneratedAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        _ = tenants.Require();
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 200) pageSize = 50;
        var query = db.GeneratedDocuments.AsNoTracking();
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(d => d.CreatedAt)
            .ThenByDescending(d => d.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new GeneratedDocumentDto(
                d.Id, d.TemplateId, d.ContactId, d.FileUrl, d.FileHash, d.SentVia, d.SentAt, d.OpenedAt, d.CreatedAt, d.ExpiresAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { items, total, page, pageSize });
    }
}
