using Clawbot.Agents.Contracts.Docs;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Documents;
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
        var grp = app.MapGroup("/api/docs");

        grp.MapPost("/generate", GenerateAsync).RequirePermission("docs:write");

        grp.MapGet("/templates", ListTemplatesAsync).RequirePermission("docs:read");
        grp.MapPost("/templates", CreateTemplateAsync).RequirePermission("docs:write");
        grp.MapPut("/templates/{id:guid}", UpdateTemplateAsync).RequirePermission("docs:write");
        grp.MapDelete("/templates/{id:guid}", DeleteTemplateAsync).RequirePermission("docs:write");

        grp.MapGet("/generated", ListGeneratedAsync).RequirePermission("docs:read");

        return app;
    }

    private static async Task<IResult> GenerateAsync(
        GenerateDocumentRequest body,
        ITenantAccessor tenants,
        DocsAgent.DocsAgentClient grpc,
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
                d.Id, d.TemplateId, d.ContactId, d.FileUrl, d.FileHash, d.SentVia, d.SentAt, d.OpenedAt, d.CreatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }
}
