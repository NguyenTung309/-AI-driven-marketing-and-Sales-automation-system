using Clawbot.Agents.Core.Learning;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Learning;

public sealed class EfAgentMemoryProvider(AppDbContext db) : IAgentMemoryProvider
{
    public async Task<IReadOnlyList<string>> GetTopFactsAsync(
        Guid tenantId,
        string agentCode,
        int topK,
        CancellationToken ct = default) =>
        await db.AgentMemories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.TenantId == tenantId && m.AgentCode == agentCode && m.IsActive)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(topK)
            .Select(m => m.Fact)
            .ToListAsync(ct).ConfigureAwait(false);
}
