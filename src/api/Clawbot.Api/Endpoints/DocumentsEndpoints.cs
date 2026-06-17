using Clawbot.Agents.Contracts.Docs;
using Clawbot.Api.Contracts.Documents;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class DocumentsEndpoints
{
    private static readonly string[] DefaultKitTemplateCodes = ["ONBOARDING-KIT", "BROCHURE-HSK", "SLIDE-DEMO-5"];
    private static readonly byte[] TransparentGif =
        Convert.FromBase64String("R0lGODlhAQABAPAAAP///wAAACH5BAAAAAAALAAAAAABAAEAAAICRAEAOw==");

    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/docs").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapPost("/generate", GenerateAsync);
        grp.MapPost("/generate-kit", GenerateKitAsync);

        grp.MapGet("/templates", ListTemplatesAsync);
        grp.MapPost("/templates", CreateTemplateAsync);
        grp.MapPut("/templates/{id:guid}", UpdateTemplateAsync);
        grp.MapDelete("/templates/{id:guid}", DeleteTemplateAsync);

        grp.MapGet("/generated", ListGeneratedAsync);
        grp.MapGet("/{id:guid}/download", DownloadAsync);

        app.MapGet("/api/docs/{id:guid}/open.gif", OpenBeaconAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        return app;
    }

    // Docs-1: anonymous 1x1 beacon for email/Zalo read receipts.
    private static async Task<IResult> OpenBeaconAsync(
        Guid id,
        Clawbot.Api.Services.DocumentOpenReceiptService receipts,
        HttpContext http,
        CancellationToken ct)
    {
        await receipts.RecordOpenAsync(id, ct).ConfigureAwait(false);
        http.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.Expires = "0";
        return Results.File(TransparentGif, "image/gif");
    }

    // Docs-1: serve a generated document link, enforcing the 7-day expiry (410 Gone past it).
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
            return Results.Problem(statusCode: StatusCodes.Status410Gone, detail: "Liên kết tải tài liệu đã hết hạn (7 ngày).");

        doc.MarkOpened(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Redirect(doc.FileUrl);
    }

    private static async Task<IResult> GenerateAsync(
        GenerateDocumentRequest body,
        ITenantAccessor tenants,
        DocsAgent.DocsAgentClient grpc,
        Clawbot.Api.Services.DocumentDeliveryService delivery,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.TemplateCode))
            return Results.BadRequest(new { error = "templateCode required" });

        try
        {
            var response = await GenerateOneAsync(
                tenant.TenantId,
                body.TemplateCode,
                body.ContactId,
                body.Vars,
                body.SentVia,
                grpc,
                delivery,
                ct).ConfigureAwait(false);
            return Results.Ok(response);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Results.NotFound(new { error = ex.Status.Detail });
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return Results.BadRequest(new { error = ex.Status.Detail });
        }
    }

    private static async Task<IResult> GenerateKitAsync(
        GenerateDocumentKitRequest body,
        ITenantAccessor tenants,
        DocsAgent.DocsAgentClient grpc,
        Clawbot.Api.Services.DocumentDeliveryService delivery,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var templateCodes = NormalizeKitTemplateCodes(body.TemplateCodes);
        if (templateCodes.Length == 0)
            return Results.BadRequest(new { error = "templateCodes required" });

        try
        {
            var documents = new List<GenerateDocumentResponse>(templateCodes.Length);
            foreach (var templateCode in templateCodes)
            {
                documents.Add(await GenerateOneAsync(
                    tenant.TenantId,
                    templateCode,
                    body.ContactId,
                    body.Vars,
                    body.SentVia,
                    grpc,
                    delivery,
                    ct).ConfigureAwait(false));
            }

            return Results.Ok(new GenerateDocumentKitResponse(
                documents,
                documents.Sum(d => d.SizeBytes),
                documents.Sum(d => d.LatencyMs)));
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return Results.NotFound(new { error = ex.Status.Detail });
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.InvalidArgument)
        {
            return Results.BadRequest(new { error = ex.Status.Detail });
        }
    }

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

    private static async Task<GenerateDocumentResponse> GenerateOneAsync(
        Guid tenantId,
        string templateCode,
        Guid? contactId,
        IReadOnlyDictionary<string, string>? vars,
        string? sentVia,
        DocsAgent.DocsAgentClient grpc,
        Clawbot.Api.Services.DocumentDeliveryService delivery,
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
            await delivery.TrySendAsync(documentId, sentVia, ct).ConfigureAwait(false);
        }

        return new GenerateDocumentResponse(documentId, resp.FileUrl, resp.FileHash, resp.SizeBytes, resp.LatencyMs);
    }

    private static async Task<IResult> ListTemplatesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var items = await db.DocumentTemplates.AsNoTracking()
            .Where(t => t.DeletedAt == null)
            .OrderBy(t => t.Code)
            .Select(t => new DocumentTemplateDto(t.Id, t.Code, t.DocType, t.TemplateHtml, t.CreatedAt, t.UpdatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
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
        Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        _ = tenants.Require();
        var tpl = await db.DocumentTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null, ct).ConfigureAwait(false);
        if (tpl is null) return Results.NotFound();

        db.Entry(tpl).Property("DeletedAt").CurrentValue = clock.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ListGeneratedAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var items = await db.GeneratedDocuments.AsNoTracking()
            .OrderByDescending(d => d.CreatedAt)
            .Take(100)
            .Select(d => new GeneratedDocumentDto(
                d.Id, d.TemplateId, d.ContactId, d.FileUrl, d.FileHash, d.SentVia, d.SentAt, d.OpenedAt, d.CreatedAt, d.ExpiresAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }
}
