using System.Globalization;
using Clawbot.Agents.Contracts.Docs;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Documents;
using Clawbot.Api.Middleware;
using Clawbot.Application.Abstractions;
using Clawbot.Domain.Documents;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocuments(this IEndpointRouteBuilder app)
    {
// SPEC-11 §6a: reads need docs:read, mutations (incl. generate) docs:write.
        var grp = app.MapGroup("/api/docs").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapPost("/generate", GenerateAsync).RequirePermission("docs:write");

        grp.MapGet("/templates", ListTemplatesAsync).RequirePermission("docs:read");
        grp.MapPost("/templates", CreateTemplateAsync).RequirePermission("docs:write");
        grp.MapPut("/templates/{id:guid}", UpdateTemplateAsync).RequirePermission("docs:write");
        grp.MapDelete("/templates/{id:guid}", DeleteTemplateAsync).RequirePermission("docs:write");

grp.MapGet("/generated", ListGeneratedAsync).RequirePermission("docs:read");
        grp.MapGet("/{id:guid}/download", DownloadAsync);

        return app;
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
            return Results.Problem(statusCode: StatusCodes.Status410Gone, detail: "LiÃªn káº¿t táº£i tÃ i liá»‡u Ä‘Ã£ háº¿t háº¡n (7 ngÃ y).");

        doc.MarkOpened(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Redirect(doc.FileUrl);
    }

    private static async Task<IResult> GenerateAsync(
        GenerateDocumentRequest body,
        ITenantAccessor tenants,
        DocsAgent.DocsAgentClient grpc,
        AppDbContext db,
        IEmailSender email,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.TemplateCode))
            return Results.BadRequest(new { error = "templateCode required" });

        var req = new DocGenerateRequest
        {
            TenantId = tenant.TenantId.ToString(),
            ContactId = body.ContactId?.ToString() ?? string.Empty,
            TemplateCode = body.TemplateCode,
            SentVia = body.SentVia ?? string.Empty,
        };
        if (body.Vars is not null)
        {
            foreach (var kv in body.Vars)
                req.Vars[kv.Key] = kv.Value;
        }

        try
        {
            var resp = await grpc.GenerateAsync(req, cancellationToken: ct);

            // Docs-1: gated send. Email goes via SMTP (config-gated, no-op when unset); Zalo send
            // is pending the Pancake outbound spike, so it is recorded but not dispatched here.
            if (!string.IsNullOrWhiteSpace(body.SentVia)
                && string.Equals(body.SentVia, "email", StringComparison.OrdinalIgnoreCase))
            {
                await TrySendByEmailAsync(db, email, clock, Guid.Parse(resp.DocumentId), resp.FileUrl, ct).ConfigureAwait(false);
            }

            return Results.Ok(new GenerateDocumentResponse(
                Guid.Parse(resp.DocumentId), resp.FileUrl, resp.FileHash, resp.SizeBytes, resp.LatencyMs));
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

    private static async Task<bool> TrySendByEmailAsync(
        AppDbContext db, IEmailSender email, IClock clock, Guid documentId, string fileUrl, CancellationToken ct)
    {
        var doc = await db.GeneratedDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct).ConfigureAwait(false);
        if (doc?.ContactId is null) return false;

        var recipient = await db.Contacts
            .Where(c => c.Id == doc.ContactId)
            .Select(c => c.Email)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(recipient)) return false;

        var expiry = doc.ExpiresAt?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "7 ngÃ y";
        await email.SendAsync(recipient, "TÃ i liá»‡u tá»« Há»c BÃ¡",
            $"Xin chÃ o, tÃ i liá»‡u cá»§a báº¡n Ä‘Ã£ sáºµn sÃ ng: {fileUrl}\nLiÃªn káº¿t cÃ³ hiá»‡u lá»±c Ä‘áº¿n {expiry}.", ct)
            .ConfigureAwait(false);

        doc.MarkSent("email", clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
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


