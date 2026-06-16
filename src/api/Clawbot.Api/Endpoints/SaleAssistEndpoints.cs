using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Domain.SaleAssist;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Clawbot.Api.Endpoints;

public static class SaleAssistEndpoints
{
    public static IEndpointRouteBuilder MapSaleAssist(this IEndpointRouteBuilder app)
    {
        // SPEC-11 §6a: entire sale-assist surface requires sale-assist:use.
        var grp = app.MapGroup("/api/sale-assist").RequirePermission("sale-assist:use");

        grp.MapPost("/draft", DraftAsync);
        grp.MapPost("/summary", SummarizeAsync);

        grp.MapGet("/quick-replies", ListQuickRepliesAsync);
        grp.MapPost("/quick-replies", CreateQuickReplyAsync);
        grp.MapPut("/quick-replies/{id:guid}", UpdateQuickReplyAsync);
        grp.MapDelete("/quick-replies/{id:guid}", DeleteQuickReplyAsync);

        return app;
    }

    private static async Task<IResult> DraftAsync(
        SaleAssistDraftRequest body,
        ITenantAccessor tenants,
        SaleAssistAgent.SaleAssistAgentClient grpc,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await grpc.DraftAsync(new DraftRequest
        {
            TenantId = tenant.TenantId.ToString(),
            ConversationId = body.ConversationId.ToString(),
            SaleUserId = string.Empty,
        }, cancellationToken: ct);
        sw.Stop();
        return Results.Ok(new SaleAssistDraftResponse(resp.DraftText, resp.SuggestedAction, resp.LeadScore, sw.ElapsedMilliseconds));
    }

    private static async Task<IResult> SummarizeAsync(
        SaleAssistSummaryRequest body,
        ITenantAccessor tenants,
        SaleAssistAgent.SaleAssistAgentClient grpc,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var resp = await grpc.SummarizeAsync(new SummarizeRequest
        {
            TenantId = tenant.TenantId.ToString(),
            ConversationId = body.ConversationId.ToString(),
        }, cancellationToken: ct);
        sw.Stop();
        return Results.Ok(new SaleAssistSummaryResponse(resp.Summary, sw.ElapsedMilliseconds));
    }

    private static async Task<IResult> ListQuickRepliesAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var items = await db.QuickReplyTemplates.AsNoTracking()
            .OrderBy(q => q.Code)
            .Select(q => new QuickReplyDto(q.Id, q.Code, q.Category, q.Body, q.Platforms))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(items);
    }

    private static async Task<IResult> CreateQuickReplyAsync(
        CreateQuickReplyRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Body))
            return Results.BadRequest(new { error = "code and body required" });

        var existing = await db.QuickReplyTemplates.AnyAsync(q => q.Code == body.Code, ct).ConfigureAwait(false);
        if (existing) return Results.Conflict(new { error = "code already exists" });

        var tpl = QuickReplyTemplate.Create(tenant.TenantId, body.Code, body.Body, clock.UtcNow);
        db.QuickReplyTemplates.Add(tpl);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/sale-assist/quick-replies/{tpl.Id}",
            new QuickReplyDto(tpl.Id, tpl.Code, tpl.Category, tpl.Body, tpl.Platforms));
    }

    private static async Task<IResult> UpdateQuickReplyAsync(
        Guid id,
        UpdateQuickReplyRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var tpl = await db.QuickReplyTemplates.FirstOrDefaultAsync(q => q.Id == id, ct).ConfigureAwait(false);
        if (tpl is null) return Results.NotFound();
        var entry = db.Entry(tpl);
        entry.Property("Body").CurrentValue = body.Body;
        entry.Property("Category").CurrentValue = body.Category;
        entry.Property("Platforms").CurrentValue = body.Platforms;
        entry.Property("UpdatedAt").CurrentValue = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteQuickReplyAsync(
        Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        _ = tenants.Require();
        var tpl = await db.QuickReplyTemplates.FirstOrDefaultAsync(q => q.Id == id, ct).ConfigureAwait(false);
        if (tpl is null) return Results.NotFound();
        db.QuickReplyTemplates.Remove(tpl);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }
}

