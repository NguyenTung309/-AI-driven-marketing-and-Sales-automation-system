using Clawbot.Agents.Core.Content;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

// Explicit tenant/item filters — Hangfire/gRPC scopes have no ambient HTTP tenant.
public sealed class EfContentAssetRepository(AppDbContext db) : IContentAssetRepository
{
    private readonly AppDbContext _db = db;

    public async Task<IReadOnlyList<ContentAsset>> ListReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        CancellationToken cancellationToken)
    {
        return await _db.ContentAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId
                && a.ContentItemId == contentItemId
                && a.Status == ContentAsset.StatusReady)
            .OrderBy(a => a.SortOrder)
            .ThenBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ContentAsset?> FindReadyAsync(
        Guid tenantId,
        Guid contentItemId,
        Guid assetId,
        CancellationToken cancellationToken)
    {
        return await _db.ContentAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.Id == assetId
                    && a.TenantId == tenantId
                    && a.ContentItemId == contentItemId
                    && a.Status == ContentAsset.StatusReady,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
