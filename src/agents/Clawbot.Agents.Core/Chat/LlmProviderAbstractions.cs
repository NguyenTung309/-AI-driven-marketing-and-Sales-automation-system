namespace Clawbot.Agents.Core.Chat;

// A fully-resolved, decrypted provider config for a single agent call.
// The API key is plaintext here (decrypted at resolve time) and must never be logged or persisted.
public sealed record ResolvedLlmConfig(
    string Provider,
    string Model,
    string ApiKey,
    string? BaseUrl,
    decimal? InputUsdPer1M,
    decimal? OutputUsdPer1M,
    int? TimeoutSeconds = null,
    int? MaxOutputTokens = null);

// Resolves the LLM config bound to an agent (by code) for a tenant.
// Throws LlmConfigNotConfiguredException when unbound or inactive (D1 — no fallback).
public interface ILlmConfigResolver
{
    Task<ResolvedLlmConfig> ResolveAsync(Guid tenantId, string agentCode, CancellationToken ct = default);
}

// Builds a provider-specific IClaudeChatClient bound to a resolved config.
public interface ILlmChatClientFactory
{
    IClaudeChatClient Create(ResolvedLlmConfig config);
}

// Thrown when an agent has no active LlmConfig bound. Surfaced to API as `llm_config_not_configured`.
public sealed class LlmConfigNotConfiguredException(Guid tenantId, string agentCode)
    : InvalidOperationException(
        $"llm_config_not_configured: agent '{agentCode}' (tenant {tenantId}) has no active LLM provider config bound.")
{
    public Guid TenantId { get; } = tenantId;
    public string AgentCode { get; } = agentCode;
}
