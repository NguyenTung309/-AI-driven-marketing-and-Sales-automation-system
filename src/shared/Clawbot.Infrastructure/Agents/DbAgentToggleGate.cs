using Clawbot.Agents.Core.Chat;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Agents;

/// <summary>
/// Reads <c>AgentConfig.Status</c> for the tenant's agent of the given type.
/// No config row → enabled (default). Row present → enabled only when status is "running".
/// Singleton-safe via a per-call scope.
/// </summary>
public sealed class DbAgentToggleGate(IServiceScopeFactory scopeFactory) : IAgentToggleGate
{
    public async Task<bool> IsAutoActionEnabledAsync(Guid tenantId, string agentType, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var status = await db.AgentConfigs
            .IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && a.DeletedAt == null && (a.AgentType == agentType || a.Code == agentType))
            .Select(a => a.Status)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        return status is null || string.Equals(status, "running", StringComparison.OrdinalIgnoreCase);
    }
}
