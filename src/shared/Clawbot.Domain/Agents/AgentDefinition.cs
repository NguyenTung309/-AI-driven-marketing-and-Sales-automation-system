using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentDefinition : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AgentType { get; private set; } = string.Empty;
    public string PersonaPrompt { get; private set; } = string.Empty;
    public string AllowedToolsJson { get; private set; } = "[]";
    public string InputSchemaJson { get; private set; } = "{}";
    public string OutputSchemaJson { get; private set; } = "{}";
    public string MemoryScope { get; private set; } = "none";
    public string? KbModuleCode { get; private set; }
    public Guid? LlmConfigId { get; private set; }
    public bool IsOrchestratable { get; private set; } = true;
    public int Version { get; private set; } = 1;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private AgentDefinition() { }

    public static AgentDefinition Create(
        Guid tenantId,
        string code,
        string displayName,
        string agentType,
        string personaPrompt,
        DateTimeOffset createdAt,
        string allowedToolsJson = "[]",
        string inputSchemaJson = "{}",
        string outputSchemaJson = "{}",
        string memoryScope = "none",
        Guid? llmConfigId = null,
        bool isOrchestratable = true,
        string? kbModuleCode = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code.Trim(),
            DisplayName = displayName.Trim(),
            AgentType = agentType.Trim().ToLowerInvariant(),
            PersonaPrompt = personaPrompt.Trim(),
            AllowedToolsJson = string.IsNullOrWhiteSpace(allowedToolsJson) ? "[]" : allowedToolsJson,
            InputSchemaJson = string.IsNullOrWhiteSpace(inputSchemaJson) ? "{}" : inputSchemaJson,
            OutputSchemaJson = string.IsNullOrWhiteSpace(outputSchemaJson) ? "{}" : outputSchemaJson,
            MemoryScope = string.IsNullOrWhiteSpace(memoryScope) ? "none" : memoryScope.Trim().ToLowerInvariant(),
            KbModuleCode = NormalizeKbModuleCode(kbModuleCode),
            LlmConfigId = llmConfigId,
            IsOrchestratable = isOrchestratable,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void UpdateDefinition(
        string displayName,
        string agentType,
        string personaPrompt,
        string allowedToolsJson,
        string inputSchemaJson,
        string outputSchemaJson,
        string memoryScope,
        Guid? llmConfigId,
        bool isOrchestratable,
        DateTimeOffset updatedAt,
        string? kbModuleCode = null)
    {
        DisplayName = displayName.Trim();
        AgentType = agentType.Trim().ToLowerInvariant();
        PersonaPrompt = personaPrompt.Trim();
        AllowedToolsJson = string.IsNullOrWhiteSpace(allowedToolsJson) ? "[]" : allowedToolsJson;
        InputSchemaJson = string.IsNullOrWhiteSpace(inputSchemaJson) ? "{}" : inputSchemaJson;
        OutputSchemaJson = string.IsNullOrWhiteSpace(outputSchemaJson) ? "{}" : outputSchemaJson;
        MemoryScope = string.IsNullOrWhiteSpace(memoryScope) ? "none" : memoryScope.Trim().ToLowerInvariant();
        KbModuleCode = NormalizeKbModuleCode(kbModuleCode);
        LlmConfigId = llmConfigId;
        IsOrchestratable = isOrchestratable;
        Version++;
        UpdatedAt = updatedAt;
    }

    // Narrow repair for the seeder: apply the catalog's tool grants to an existing row without rewriting the
    // persona/schemas (the full UpdateDefinition would). Used so agents seeded before tools were assigned get them.
    public void SetAllowedTools(string allowedToolsJson, DateTimeOffset updatedAt)
    {
        AllowedToolsJson = string.IsNullOrWhiteSpace(allowedToolsJson) ? "[]" : allowedToolsJson;
        UpdatedAt = updatedAt;
    }

    public void Archive(DateTimeOffset updatedAt)
    {
        DeletedAt = updatedAt;
        IsOrchestratable = false;
        UpdatedAt = updatedAt;
    }

    private static string? NormalizeKbModuleCode(string? kbModuleCode) =>
        string.IsNullOrWhiteSpace(kbModuleCode) ? null : kbModuleCode.Trim();
}
