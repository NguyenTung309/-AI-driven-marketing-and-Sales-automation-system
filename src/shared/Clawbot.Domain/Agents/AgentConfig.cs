using Clawbot.Domain.Common;

namespace Clawbot.Domain.Agents;

public sealed class AgentConfig : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AgentType { get; private set; } = string.Empty;       // chat|sale_assist|lead|content|docs|ads|report|research
    public string Model { get; private set; } = string.Empty;
    public string Status { get; private set; } = "stopped";              // running|stopped|error
    public string SkillFilesJson { get; private set; } = "[]";
    public string KbModulesJson { get; private set; } = "[]";
    public string ConfigJson { get; private set; } = "{}";
    public Guid? LlmConfigId { get; private set; }                       // bound LLM provider config (null → unconfigured)
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    private AgentConfig() { }

    public static AgentConfig Create(
        Guid tenantId,
        string code,
        string displayName,
        string agentType,
        string model,
        DateTimeOffset createdAt) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            DisplayName = displayName,
            AgentType = agentType,
            Model = model,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public void Start() { Status = "running"; }
    public void Stop()  { Status = "stopped"; }
    public void MarkError() { Status = "error"; }

    public void UpdateSettings(
        string displayName,
        string model,
        string skillFilesJson,
        string kbModulesJson,
        string configJson,
        DateTimeOffset updatedAt)
    {
        DisplayName = displayName;
        Model = model;
        SkillFilesJson = skillFilesJson;
        KbModulesJson = kbModulesJson;
        ConfigJson = configJson;
        UpdatedAt = updatedAt;
    }

    // Bind (or unbind) the LLM provider config this agent runs on.
    public void BindLlmConfig(Guid? llmConfigId, DateTimeOffset updatedAt)
    {
        LlmConfigId = llmConfigId;
        UpdatedAt = updatedAt;
    }
}
