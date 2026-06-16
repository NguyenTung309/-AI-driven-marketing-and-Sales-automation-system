using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public static class ContactsEndpoints
{
    public static IEndpointRouteBuilder MapContacts(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/contacts").RequireAuthorization().RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapPost("/merge", MergeContactsAsync);

        return grp;
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
