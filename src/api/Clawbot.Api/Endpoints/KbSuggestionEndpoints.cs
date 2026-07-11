using System.Security.Claims;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.KnowledgeBase;
using Clawbot.Infrastructure.Learning;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Clawbot.Api.Endpoints;

public sealed record KbSuggestionDto(
    Guid Id,
    string Op,
    Guid? TargetKbModuleId,
    string? TargetModuleName,
    string Title,
    string ContentMd,
    string Rationale,
    string EvidenceJson,
    string? ReviewerVerdict,
    string? ReviewerNotes,
    decimal? AccuracyBefore,
    decimal? AccuracyAfter,
    string Status,
    string? ApprovalMode,
    string? RejectedReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);

public sealed record ApproveKbSuggestionRequest(string? ContentMd);

public sealed record RejectKbSuggestionRequest(string Reason);

// ai-self-learning-memory 1.6: duyệt tay đề xuất tri thức. Nhánh auto nằm trong KnowledgeDistillationJob —
// endpoint chỉ phục vụ người (approve có thể sửa content trước khi deploy; reject bắt buộc lý do).
public static class KbSuggestionEndpoints
{
    public static IEndpointRouteBuilder MapKbSuggestions(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/kb/suggestions").RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);
        group.MapGet("/", ListAsync).RequirePermission("kb:read");
        group.MapPost("/{id:guid}/approve", ApproveAsync).RequirePermission("kb:write");
        group.MapPost("/{id:guid}/reject", RejectAsync).RequirePermission("kb:write");
        return app;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var query = db.KbSuggestions.Where(s => s.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(s => s.Status == status);

        var suggestions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var moduleIds = suggestions
            .Where(s => s.TargetKbModuleId is not null)
            .Select(s => s.TargetKbModuleId!.Value)
            .Distinct()
            .ToList();
        var moduleNames = moduleIds.Count == 0
            ? []
            : await db.KbModules
                .Where(m => m.TenantId == tenantId && moduleIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id, m => m.Name, ct);

        var dtos = suggestions
            .Select(s => ToDto(s, s.TargetKbModuleId is { } mid ? moduleNames.GetValueOrDefault(mid) : null))
            .ToList();
        return Results.Ok(dtos);
    }

    private static async Task<IResult> ApproveAsync(
        Guid id,
        ApproveKbSuggestionRequest? body,
        ClaimsPrincipal user,
        AppDbContext db,
        ITenantAccessor tenants,
        KbSuggestionMaterializer materializer,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var userId = CurrentUserId(user);
        if (userId is null) return Results.Unauthorized();

        var suggestion = await db.KbSuggestions
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (suggestion is null) return Results.NotFound();
        if (suggestion.Status != KbSuggestion.StatusPending)
            return Results.Conflict(new { error = "suggestion_already_decided", status = suggestion.Status });

        suggestion.Approve(clock.UtcNow, userId, KbSuggestion.ApprovalModeHuman, body?.ContentMd);
        await db.SaveChangesAsync(ct);

        try
        {
            var version = await materializer.MaterializeAsync(suggestion, ct);
            return Results.Ok(new { suggestion.Id, suggestion.Status, versionId = version.Id });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Approve đã ghi; deploy fail để người thấy lỗi và bấm lại từ màn KB (version thường đã tạo draft).
            return Results.Problem(title: "kb_suggestion_deploy_failed", detail: ex.Message, statusCode: 502);
        }
    }

    private static async Task<IResult> RejectAsync(
        Guid id,
        RejectKbSuggestionRequest body,
        ClaimsPrincipal user,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var userId = CurrentUserId(user);
        if (userId is null) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(body.Reason))
            return Results.BadRequest(new { error = "reject_reason_required" });

        var suggestion = await db.KbSuggestions
            .FirstOrDefaultAsync(s => s.Id == id && s.TenantId == tenantId, ct);
        if (suggestion is null) return Results.NotFound();
        if (suggestion.Status != KbSuggestion.StatusPending)
            return Results.Conflict(new { error = "suggestion_already_decided", status = suggestion.Status });

        suggestion.Reject(clock.UtcNow, userId.Value, body.Reason.Trim());
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { suggestion.Id, suggestion.Status });
    }

    private static KbSuggestionDto ToDto(KbSuggestion s, string? moduleName) =>
        new(s.Id, s.Op, s.TargetKbModuleId, moduleName, s.Title, s.ContentMd, s.Rationale, s.EvidenceJson,
            s.ReviewerVerdict, s.ReviewerNotes, s.AccuracyBefore, s.AccuracyAfter,
            s.Status, s.ApprovalMode, s.RejectedReason, s.CreatedAt, s.DecidedAt);

    private static Guid? CurrentUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var parsed) && parsed != Guid.Empty ? parsed : null;
    }
}
