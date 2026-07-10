using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

// Mirror of EfOrchestrationApprovalResolver for the content review-gate tenant flag.
public sealed class EfContentReviewPolicyResolver(AppDbContext db) : IContentReviewPolicyResolver
{
    private readonly AppDbContext _db = db;

    public async Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return false;
        return await _db.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => (bool?)t.RequireContentReview)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false) ?? false;
    }
}
