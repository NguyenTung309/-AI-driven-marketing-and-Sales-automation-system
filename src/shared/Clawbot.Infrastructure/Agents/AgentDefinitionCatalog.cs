using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

// V2 sub-agent catalog backed by agent_definitions (data-defined personas).
public sealed class AgentDefinitionCatalog(AppDbContext db) : IAgentDefinitionCatalog
{
    private readonly AppDbContext _db = db;

    public async Task<IReadOnlyList<AgentDefinitionCatalogEntry>> ListAsync(Guid tenantId, CancellationToken ct = default)
    {
        var rows = await _db.AgentDefinitions
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
                d.InputSchemaJson,
                d.IsOrchestratable,
                d.KbModuleCode,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows
            .Select(d => new AgentDefinitionCatalogEntry(
                d.Id,
                d.Code,
                ShortNameFor(d.Code),
                d.DisplayName,
                d.AgentType,
                string.IsNullOrWhiteSpace(d.PersonaPrompt) ? $"Run {d.DisplayName}." : d.PersonaPrompt,
                string.IsNullOrWhiteSpace(d.InputSchemaJson) ? "{}" : d.InputSchemaJson,
                d.IsOrchestratable,
                string.IsNullOrWhiteSpace(d.KbModuleCode) ? null : d.KbModuleCode))
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
