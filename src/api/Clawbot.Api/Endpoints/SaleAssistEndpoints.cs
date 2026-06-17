using Clawbot.Agents.Contracts.SaleAssist;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
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
var grp = app.MapGroup("/api/sale-assist").RequirePermission("sale-assist:use").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapPost("/draft", DraftAsync);
        grp.MapPost("/draft-feedback", DraftFeedbackAsync);
        grp.MapPost("/summary", SummarizeAsync);

        grp.MapGet("/quick-replies", ListQuickRepliesAsync);
        grp.MapPost("/quick-replies", CreateQuickReplyAsync);
        grp.MapPut("/quick-replies/{id:guid}", UpdateQuickReplyAsync);
        grp.MapDelete("/quick-replies/{id:guid}", DeleteQuickReplyAsync);

        grp.MapGet("/daily-summary", DailySummaryAsync);
        grp.MapGet("/upsell-suggestions", UpsellSuggestionsAsync);
        grp.MapGet("/upsell", UpsellAsync);

        return app;
    }

    // SaleAssist-4: dynamic, contextual upsell for one conversation (hot-gated + Claude).
    private static async Task<IResult> UpsellAsync(
        Guid conversationId,
        ITenantAccessor tenants,
        SaleAssistAgent.SaleAssistAgentClient grpc,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        if (conversationId == Guid.Empty) return Results.BadRequest(new { error = "conversationId required" });

        var resp = await grpc.UpsellAsync(new UpsellRequest
        {
            TenantId = tenant.TenantId.ToString(),
            ConversationId = conversationId.ToString(),
        }, cancellationToken: ct);

        return Results.Ok(new SaleAssistUpsellResponse(resp.Eligible, resp.Suggestion, resp.Reason, resp.LeadScore));
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

    private static async Task<IResult> DraftFeedbackAsync(
        SaleAssistDraftFeedbackRequest body,
        ITenantAccessor tenants,
        SaleAssistDraftFeedbackService feedback,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        try
        {
            return Results.Ok(await feedback.RecordAsync(tenant.TenantId, body, ct).ConfigureAwait(false));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
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

    private static async Task<IResult> DailySummaryAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var today = DateTimeOffset.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var newLeads = await db.Leads
            .IgnoreQueryFilters()
            .CountAsync(l => l.TenantId == tenantId && l.CreatedAt >= today && l.CreatedAt < tomorrow, ct);

        var conversations = await db.Conversations
            .IgnoreQueryFilters()
            .CountAsync(c => c.TenantId == tenantId && c.CreatedAt >= today && c.CreatedAt < tomorrow, ct);

        var messagesSent = await db.Messages
            .IgnoreQueryFilters()
            .CountAsync(m => m.TenantId == tenantId && m.Direction == "out" && m.SentAt >= today && m.SentAt < tomorrow, ct);

        var hotLeads = await db.Leads
            .IgnoreQueryFilters()
            .CountAsync(l => l.TenantId == tenantId && l.Stage == "hot", ct);

        return Results.Ok(new
        {
            date = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            new_leads = newLeads,
            conversations,
            messages_sent = messagesSent,
            hot_leads = hotLeads,
        });
    }

    private static async Task<IResult> UpsellSuggestionsAsync(
        ITenantAccessor tenants,
        SaleAssistUpsellSuggestionService suggestions,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        return Results.Ok(await suggestions.GetSuggestionsAsync(tenantId, ct: ct).ConfigureAwait(false));
    }
}





