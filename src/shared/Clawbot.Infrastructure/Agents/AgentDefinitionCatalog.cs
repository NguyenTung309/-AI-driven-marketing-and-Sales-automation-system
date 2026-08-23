using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

// V2 sub-agent catalog backed by agent_definitions (data-defined personas).
public sealed class AgentDefinitionCatalog(AppDbContext db, ITenantAccessor tenants) : IAgentDefinitionCatalog
{
    private readonly AppDbContext _db = db;
    private readonly ITenantAccessor _tenants = tenants;

    public async Task<IReadOnlyList<AgentDefinitionCatalogEntry>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        if (_tenants.Current is { TenantId: var ambientTenantId } && ambientTenantId != tenantId)
            return [];

        var rows = await _db.AgentDefinitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.DeletedAt == null && d.IsOrchestratable)
            .OrderBy(d => d.Code)
            .Select(d => new
            {
                d.Id,
                d.Code,
                d.DisplayName,
                d.AgentType,
                d.PersonaPrompt,
                d.SystemPrompt,
                d.InputSchemaJson,
                d.IsOrchestratable,
                d.KbModuleCode,
                d.AllowedToolsJson,
                d.LlmConfigId,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        // Only surface agents the planner can actually run: an agent resolves an LLM config from its
        // definition OR from an agents-table row (LlmConfigResolver checks both). Hiding unbindable agents
        // stops the planner from picking one that fails at runtime with llm_config_not_configured —
        // which otherwise triggers a re-plan and can exhaust the orchestrator's round budget (max_rounds).
        var agentBoundCodes = await _db.AgentConfigs
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.DeletedAt == null && a.LlmConfigId != null)
            .Select(a => a.Code)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var boundCodes = new HashSet<string>(agentBoundCodes, StringComparer.OrdinalIgnoreCase);

        return rows
            .Where(d => d.LlmConfigId != null || boundCodes.Contains(Clawbot.Agents.Core.AgentPromptPacks.NormalizeCode(d.Code)))
            .Select(d => new AgentDefinitionCatalogEntry(
                d.Id,
                d.Code,
                ShortNameFor(d.Code),
                d.DisplayName,
                d.AgentType,
                string.IsNullOrWhiteSpace(d.PersonaPrompt) ? $"Run {d.DisplayName}." : d.PersonaPrompt,
                string.IsNullOrWhiteSpace(d.InputSchemaJson) ? "{}" : d.InputSchemaJson,
                d.IsOrchestratable,
                string.IsNullOrWhiteSpace(d.KbModuleCode) ? null : d.KbModuleCode,
                string.IsNullOrWhiteSpace(d.AllowedToolsJson) ? "[]" : d.AllowedToolsJson,
                // Prompt chạy thật; rỗng thì worker lùi về PersonaPrompt như trước.
                string.IsNullOrWhiteSpace(d.SystemPrompt) ? null : d.SystemPrompt))
            .ToArray();
    }

    public async Task<AgentDefinitionCatalogEntry?> FindByCodeAsync(Guid tenantId, string code, CancellationToken ct = default)
    {
        var normalized = (code ?? string.Empty).Trim();
        if (normalized.Length == 0) return null;

        var entries = await ListAsync(tenantId, ct).ConfigureAwait(false);
        return entries.FirstOrDefault(e => string.Equals(e.Code, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static string ShortNameFor(string code)
    {
        if (code.EndsWith("-agent", StringComparison.OrdinalIgnoreCase))
            return code[..^"-agent".Length];

        var dash = code.IndexOf('-');
        return dash > 0 ? code[..dash] : code;
    }
}
