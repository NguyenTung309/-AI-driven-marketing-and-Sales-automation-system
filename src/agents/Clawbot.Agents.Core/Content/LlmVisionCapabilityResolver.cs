using System.Collections.Concurrent;

namespace Clawbot.Agents.Core.Content;

// Phase 2.6: nullable supports_vision override > known first-party registry > unknown.
// Never label available from provider name alone. openai-compatible gateways stay unknown unless overridden.

public enum LlmVisionCapability
{
    Available = 0,
    Unavailable = 1,
    Unknown = 2,
}

public interface ILlmVisionCapabilityResolver
{
    LlmVisionCapability ResolveFromConfig(
        string provider,
        string modelId,
        bool? supportsVisionOverride);
}

public sealed class LlmVisionCapabilityResolver : ILlmVisionCapabilityResolver
{
    // Conservative allow/deny for maintained first-party models. Prefix match is case-insensitive.
    private static readonly (string Provider, string ModelPrefix, bool SupportsVision)[] Registry =
    [
        ("openai", "gpt-4o", true),
        ("openai", "gpt-4.1", true),
        ("openai", "gpt-4-turbo", true),
        ("openai", "gpt-4-vision", true),
        ("openai", "o1", true),
        ("openai", "o3", true),
        ("openai", "o4", true),
        ("openai", "gpt-3.5", false),
        ("openai-responses", "gpt-4o", true),
        ("openai-responses", "gpt-4.1", true),
        ("openai-responses", "gpt-4-turbo", true),
        ("openai-responses", "o1", true),
        ("openai-responses", "o3", true),
        ("openai-responses", "o4", true),
        ("openai-responses", "gpt-3.5", false),
        ("anthropic", "claude-opus-4", true),
        ("anthropic", "claude-sonnet-4", true),
        ("anthropic", "claude-haiku-4", true),
        ("anthropic", "claude-3-5", true),
        ("anthropic", "claude-3-7", true),
        ("anthropic", "claude-3-opus", true),
        ("anthropic", "claude-3-sonnet", true),
        ("anthropic", "claude-3-haiku", true),
        ("anthropic", "claude-3", true),
    ];

    // Explicit interface impl so public static helper can share the same name for unit tests.
    LlmVisionCapability ILlmVisionCapabilityResolver.ResolveFromConfig(
        string provider,
        string modelId,
        bool? supportsVisionOverride) =>
        ResolveStatic(provider, modelId, supportsVisionOverride);

    public static LlmVisionCapability ResolveFromConfig(
        string provider,
        string modelId,
        bool? supportsVisionOverride) =>
        ResolveStatic(provider, modelId, supportsVisionOverride);

    private static LlmVisionCapability ResolveStatic(
        string provider,
        string modelId,
        bool? supportsVisionOverride)
    {
        if (supportsVisionOverride is true)
            return LlmVisionCapability.Available;
        if (supportsVisionOverride is false)
            return LlmVisionCapability.Unavailable;

        var normalizedProvider = (provider ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedModel = (modelId ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedProvider.Length == 0 || normalizedModel.Length == 0)
            return LlmVisionCapability.Unknown;

        // Custom/openai-compatible gateways are not trusted without explicit override.
        if (normalizedProvider is "openai-compatible" or "openai_compatible")
            return LlmVisionCapability.Unknown;

        foreach (var (registryProvider, modelPrefix, supportsVision) in Registry)
        {
            if (!string.Equals(normalizedProvider, registryProvider, StringComparison.Ordinal))
                continue;
            if (!normalizedModel.StartsWith(modelPrefix, StringComparison.Ordinal))
                continue;
            return supportsVision ? LlmVisionCapability.Available : LlmVisionCapability.Unavailable;
        }

        return LlmVisionCapability.Unknown;
    }
}

public sealed class LlmVisionCapabilityCache
{
    private readonly ConcurrentDictionary<string, LlmVisionCapability> _entries = new(StringComparer.Ordinal);

    public bool TryGet(
        Guid tenantId,
        string agentCode,
        Guid llmConfigId,
        DateTimeOffset configUpdatedAt,
        out LlmVisionCapability capability) =>
        _entries.TryGetValue(BuildKey(tenantId, agentCode, llmConfigId, configUpdatedAt), out capability);

    public void Set(
        Guid tenantId,
        string agentCode,
        Guid llmConfigId,
        DateTimeOffset configUpdatedAt,
        LlmVisionCapability capability) =>
        _entries[BuildKey(tenantId, agentCode, llmConfigId, configUpdatedAt)] = capability;

    public void Invalidate(Guid llmConfigId)
    {
        foreach (var key in _entries.Keys)
        {
            if (key.Contains(llmConfigId.ToString("N"), StringComparison.Ordinal))
                _entries.TryRemove(key, out _);
        }
    }

    private static string BuildKey(
        Guid tenantId,
        string agentCode,
        Guid llmConfigId,
        DateTimeOffset configUpdatedAt) =>
        $"{tenantId:N}|{agentCode}|{llmConfigId:N}|{configUpdatedAt.UtcTicks}";
}
