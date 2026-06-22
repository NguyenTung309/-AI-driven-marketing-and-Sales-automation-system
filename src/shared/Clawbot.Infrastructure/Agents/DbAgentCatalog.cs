using System.Text.Json;
using Clawbot.Agents.Core;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Infrastructure.Agents;

public sealed class DbAgentCatalog(AppDbContext db) : IAgentCatalog
{
    private readonly AppDbContext _db = db;

    public async Task<IReadOnlyList<AgentCatalogEntry>> ListAsync(CancellationToken ct = default)
    {
        var agents = await _db.AgentConfigs
            .AsNoTracking()
            .Where(agent => agent.DeletedAt == null && agent.Status == "running")
            .OrderBy(agent => agent.Code)
            .Select(agent => new
            {
                agent.Code,
                agent.DisplayName,
                agent.AgentType,
                agent.ConfigJson,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return agents
            .Select(agent =>
            {
                var metadata = OrchestrationMetadata.FromConfig(agent.ConfigJson, agent.DisplayName);
                return new AgentCatalogEntry(
                    agent.Code,
                    ShortNameFor(agent.Code, agent.AgentType),
                    agent.DisplayName,
                    agent.AgentType,
                    metadata.Description,
                    metadata.InputSchemaJson,
                    metadata.Orchestratable);
            })
            .Where(entry => entry.Orchestratable)
            .ToArray();
    }

    public async Task<AgentCatalogEntry> ResolveAsync(string name, CancellationToken ct = default)
    {
        var normalized = (name ?? string.Empty).Trim();
        if (normalized.Length == 0)
            throw new KeyNotFoundException("Agent '' is not available for orchestration.");

        var entries = await ListAsync(ct).ConfigureAwait(false);
        var match = entries.FirstOrDefault(entry =>
            string.Equals(entry.Code, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.ShortName, normalized, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(entry.AgentType, normalized, StringComparison.OrdinalIgnoreCase));

        return match ?? throw new KeyNotFoundException($"Agent '{name}' is not available for orchestration.");
    }

    private static string ShortNameFor(string code, string agentType)
    {
        if (code.EndsWith("-agent", StringComparison.OrdinalIgnoreCase))
            return code[..^"-agent".Length];

        return string.IsNullOrWhiteSpace(agentType) ? code : agentType;
    }

    private sealed record OrchestrationMetadata(string Description, string InputSchemaJson, bool Orchestratable)
    {
        private static readonly JsonSerializerOptions WebJsonOptions = new(JsonSerializerDefaults.Web);

        public static OrchestrationMetadata FromConfig(string configJson, string displayName)
        {
            var fallback = new OrchestrationMetadata($"Run {displayName}.", "{}", true);
            if (string.IsNullOrWhiteSpace(configJson))
                return fallback;

            try
            {
                using var doc = JsonDocument.Parse(configJson);
                if (!doc.RootElement.TryGetProperty("orchestration", out var orchestration))
                    return fallback;

                var description = ReadString(orchestration, "description") ?? fallback.Description;
                var inputSchema = ReadString(orchestration, "inputSchema") ?? fallback.InputSchemaJson;
                var orchestratable = ReadBool(orchestration, "orchestratable") ?? fallback.Orchestratable;
                return new OrchestrationMetadata(description, inputSchema, orchestratable);
            }
            catch (JsonException)
            {
                return fallback;
            }
        }

        private static string? ReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.String => property.GetString(),
                JsonValueKind.Object => JsonSerializer.Serialize(property, WebJsonOptions),
                _ => null,
            };
        }

        private static bool? ReadBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                return null;

            return property.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            };
        }
    }
}
