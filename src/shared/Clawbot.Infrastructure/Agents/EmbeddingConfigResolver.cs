using Clawbot.Agents.Core.Rag;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

public sealed class EmbeddingConfigResolver(
    AppDbContext db,
    IEncryptor encryptor) : IEmbeddingConfigResolver
{
    public async Task<ResolvedEmbeddingConfig?> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) return null;

        var cfg = await db.EmbeddingConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.IsActive)
            .OrderByDescending(c => c.UpdatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (cfg is null) return null;

        if (cfg.Provider.Equals("hash", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedEmbeddingConfig(
                "hash", cfg.ModelId, null, null, cfg.Dimension, "tenant-db", cfg.DisplayName);
        }

        if (string.IsNullOrWhiteSpace(cfg.ApiKeyEncrypted)) return null;

        string apiKey;
        try
        {
            apiKey = encryptor.Decrypt(cfg.ApiKeyEncrypted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }

        return new ResolvedEmbeddingConfig(
            cfg.Provider,
            cfg.ModelId,
            apiKey,
            cfg.BaseUrl,
            cfg.Dimension,
            "tenant-db",
            cfg.DisplayName);
    }
}
