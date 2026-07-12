using Clawbot.Api.Middleware;
using Clawbot.Api.Services;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ContactsEndpoints
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/contacts").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/{id:guid}/export.json", ExportDataAsync);
        grp.MapPost("/merge", MergeContactsAsync);

        // ai-self-learning-memory Lớp 2: facts AI ghi nhớ về khách (panel phải hội thoại).
        grp.MapGet("/{id:guid}/memories", ListMemoriesAsync);
        grp.MapDelete("/{id:guid}/memories", DeleteAllMemoriesAsync);
        grp.MapDelete("/{id:guid}/memories/{memoryId:guid}", DeleteMemoryAsync);

        return grp;
    }

    public sealed record ContactMemoryDto(
        Guid Id, string Fact, string Category, decimal Confidence, DateTimeOffset UpdatedAt);

    private static async Task<IResult> ListMemoriesAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var memories = await db.ContactMemories
            .Where(m => m.TenantId == tenantId && m.ContactId == id && m.IsActive)
            .OrderByDescending(m => m.UpdatedAt)
            .Select(m => new ContactMemoryDto(m.Id, m.Fact, m.Category, m.Confidence, m.UpdatedAt))
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(memories);
    }

    // Xóa theo yêu cầu khách: xóa CỨNG toàn bộ (kể cả bản superseded) — quyền được quên.
    private static async Task<IResult> DeleteAllMemoriesAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var removed = await db.ContactMemories
            .Where(m => m.TenantId == tenantId && m.ContactId == id)
            .ExecuteDeleteAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { removed });
    }

    // Gỡ 1 fact sai: hạ cờ (supersede không thay thế) — giữ vết cho debug, khác xóa cứng toàn bộ.
    private static async Task<IResult> DeleteMemoryAsync(
        Guid id,
        Guid memoryId,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var memory = await db.ContactMemories
            .FirstOrDefaultAsync(m => m.Id == memoryId && m.TenantId == tenantId && m.ContactId == id, ct)
            .ConfigureAwait(false);
        if (memory is null) return Results.NotFound();
        if (memory.IsActive) memory.Supersede(null, clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<IResult> ExportDataAsync(
        Guid id,
        ContactDataExportService service,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var result = await service.ExportAsync(tenantId, id, ct).ConfigureAwait(false);
        if (result is null)
            return Results.NotFound(new { error = "contact not found" });

        var json = JsonSerializer.Serialize(result.Export, ExportJsonOptions);
        return Results.File(Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", result.FileName);
    }

    private static async Task<IResult> MergeContactsAsync(
        MergeContactsRequest body,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;

        if (body.SourceId == body.TargetId)
            return Results.BadRequest(new { error = "source and target must be different" });

        var source = await db.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == body.SourceId && c.TenantId == tenantId, ct);
        var target = await db.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == body.TargetId && c.TenantId == tenantId, ct);

        if (source is null || target is null)
            return Results.NotFound(new { error = "one or both contacts not found" });

        // Transfer external IDs from source to target
        var sourceExtIds = await db.ContactExternalIds
            .Where(e => e.ContactId == body.SourceId)
            .ToListAsync(ct);

        var targetExtIds = await db.ContactExternalIds
            .Where(e => e.ContactId == body.TargetId)
            .Select(e => $"{e.Platform}:{e.ExternalId}")
            .ToListAsync(ct);

        var targetExtSet = new HashSet<string>(targetExtIds, StringComparer.OrdinalIgnoreCase);
        var transferred = 0;

        foreach (var ext in sourceExtIds)
        {
            var key = $"{ext.Platform}:{ext.ExternalId}";
            if (targetExtSet.Add(key))
            {
                db.Entry(ext).Property("ContactId").CurrentValue = body.TargetId;
                transferred++;
            }
            else
            {
                db.ContactExternalIds.Remove(ext);
            }
        }

        // Transfer conversations
        var sourceConvs = await db.Conversations.IgnoreQueryFilters()
            .Where(c => c.ContactId == body.SourceId && c.TenantId == tenantId)
            .ToListAsync(ct);

        foreach (var conv in sourceConvs)
        {
            db.Entry(conv).Property("ContactId").CurrentValue = body.TargetId;
        }

        // Transfer leads
        var sourceLeads = await db.Leads.IgnoreQueryFilters()
            .Where(l => l.ContactId == body.SourceId && l.TenantId == tenantId)
            .ToListAsync(ct);

        foreach (var lead in sourceLeads)
        {
            db.Entry(lead).Property("ContactId").CurrentValue = body.TargetId;
        }

        // Soft-delete source contact
        db.Entry(source).Property("DeletedAt").CurrentValue = clock.UtcNow;

        await db.SaveChangesAsync(ct);

        return Results.Ok(new
        {
            merged_into = body.TargetId,
            external_ids_transferred = transferred,
            conversations_transferred = sourceConvs.Count,
            leads_transferred = sourceLeads.Count,
        });
    }
}

public sealed record MergeContactsRequest(Guid SourceId, Guid TargetId);
