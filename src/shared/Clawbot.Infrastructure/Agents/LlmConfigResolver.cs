using Clawbot.Agents.Core.Chat;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Infrastructure.Agents;

// Loads an agent's bound LlmConfig, validates it is active, decrypts the key, and computes the
// effective model. Singleton: opens a short-lived scope per resolve for DbContext access and uses
// explicit tenant filters (the gRPC AgentService path has no ITenantAccessor). No fallback (D1).
public sealed class LlmConfigResolver(IServiceScopeFactory scopeFactory, IEncryptor encryptor) : ILlmConfigResolver
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IEncryptor _encryptor = encryptor;

    public async Task<ResolvedLlmConfig> ResolveAsync(Guid tenantId, string agentCode, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var agent = await db.AgentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.Code == agentCode && a.DeletedAt == null)
            .Select(a => new { a.LlmConfigId, a.Model })
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (agent?.LlmConfigId is not { } configId)
            throw new LlmConfigNotConfiguredException(tenantId, agentCode);

        var cfg = await db.LlmConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.Id == configId && c.TenantId == tenantId && c.IsActive)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (cfg is null)
            throw new LlmConfigNotConfiguredException(tenantId, agentCode);

        // D2: the per-agent model string overrides the config's model when set.
        var effectiveModel = string.IsNullOrWhiteSpace(agent.Model) ? cfg.ModelId : agent.Model;
        string apiKey;
        try
        {
            apiKey = _encryptor.Decrypt(cfg.ApiKeyEncrypted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new LlmConfigNotConfiguredException(tenantId, agentCode);
        }

        return new ResolvedLlmConfig(
            cfg.Provider,
            effectiveModel,
            apiKey,
            cfg.BaseUrl,
            cfg.InputUsdPer1M,
            cfg.OutputUsdPer1M);
    }
}
