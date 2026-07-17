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
    private static readonly string[] DefaultKitTemplateCodes = ["ONBOARDING-KIT", "BROCHURE-HSK", "SLIDE-DEMO-5"];
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
        CancellationToken ct)
    {
        _ = tenants.Require();
        var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(d => d.Id == id, ct).ConfigureAwait(false);
        if (doc is null) return Results.NotFound();
        if (doc.IsExpired(clock.UtcNow))
            return Results.Problem(statusCode: StatusCodes.Status410Gone, detail: "Document download link has expired.");

        doc.MarkOpened(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Redirect(doc.FileUrl);
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

        var jobId = await jobs.LaunchAsync(
            DocsGenerateJobHandler.JobType,
            $"Sinh tài liệu {body.TemplateCode.Trim()}",
            new DocsGenerateJobPayload(body.TemplateCode.Trim(), body.ContactId, body.Vars, body.SentVia),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    // Bộ tài liệu: luồng nặng nhất (nhiều doc/1 lần) — job có tiến độ theo từng doc.
    private static async Task<IResult> GenerateKitAsync(
        GenerateDocumentKitRequest body,
        ITenantAccessor tenants,
        IJobLauncher jobs,
        HttpContext http,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var templateCodes = NormalizeKitTemplateCodes(body.TemplateCodes);
        if (templateCodes.Length == 0)
            return Results.BadRequest(new { error = "templateCodes required" });

        var jobId = await jobs.LaunchAsync(
            DocsKitJobHandler.JobType,
            $"Sinh bộ {templateCodes.Length} tài liệu",
            new DocsKitJobPayload(templateCodes, body.ContactId, body.Vars, body.SentVia),
            CurrentUserId(http),
            ct: ct).ConfigureAwait(false);

        return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/agents?job={jobId}" });
    }

    private static Guid? CurrentUserId(HttpContext http) =>
        Guid.TryParse(http.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id)
        && id != Guid.Empty
            ? id
            : null;

    private static string[] NormalizeKitTemplateCodes(IReadOnlyList<string>? templateCodes)
    {
        var values = templateCodes is { Count: > 0 } ? templateCodes : DefaultKitTemplateCodes;
        return values
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
            var delivered = await delivery.TrySendAsync(tenantId, documentId, sentVia, ct).ConfigureAwait(false);
            if (!delivered)
                throw new InvalidOperationException($"Document {documentId} was generated but could not be delivered via {sentVia}.");
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
        var items = await query
            .OrderBy(t => t.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new DocumentTemplateDto(t.Id, t.Code, t.DocType, t.TemplateHtml, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
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
        var tpl = DocumentTemplate.Create(tenant.TenantId, body.Code, docType, body.TemplateHtml, clock.UtcNow);
        db.DocumentTemplates.Add(tpl);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Results.Created($"/api/docs/templates/{tpl.Id}",
            new DocumentTemplateDto(tpl.Id, tpl.Code, tpl.DocType, tpl.TemplateHtml, tpl.CreatedAt, tpl.UpdatedAt));
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
        var tpl = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct).ConfigureAwait(false);
        if (tpl is null) return Results.NotFound();

        var entry = db.Entry(tpl);
        entry.Property("DocType").CurrentValue = string.IsNullOrWhiteSpace(body.DocType) ? tpl.DocType : body.DocType;
        entry.Property("TemplateHtml").CurrentValue = body.TemplateHtml;
        entry.Property("UpdatedAt").CurrentValue = clock.UtcNow;
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
