namespace Clawbot.Agents.Core.Orchestrator;

/// <summary>
/// Orchestrator-side default tool grants by agent identity.
/// When <c>agent_definitions.allowed_tools_json</c> is empty/[] (seed bug or text-only admin choice
/// that still needs hands), the worker fills tools from this map so sub-agents can act.
/// Explicit non-empty grants always win (admin/product intent).
/// </summary>
public static class AgentToolDefaults
{
    // Keys: agent_definitions.code, shortName, and agent_type (all case-insensitive).
    // Values: ToolRegistry tool names (adapter Name or explicit IAgentTool.Name).
    private static readonly Dictionary<string, string[]> ByAgentKey =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["lead-agent"] = ["lead-agent"],
            ["lead"] = ["lead-agent"],

            ["sale-assist-agent"] = ["sale-assist"],
            ["sale-assist"] = ["sale-assist"],
            ["sale_assist"] = ["sale-assist"],

            ["content-agent"] = ["content-agent"],
            ["content"] = ["content-agent"],

            ["research-agent"] = ["research-agent", "web.search"],
            ["research"] = ["research-agent", "web.search"],

            ["docs-agent"] = ["docs-agent"],
            ["docs"] = ["docs-agent"],

            ["report-agent"] = ["report-agent"],
            ["report"] = ["report-agent"],

            ["chat-agent"] = ["chat-agent"],
            ["chat"] = ["chat-agent"],

            // Phase 4.9: canonical content.review only; no autonomous schedule/publish grants.
            // content.list đi kèm vì content.review đòi content_id cụ thể: thiếu tool tra cứu thì reviewer
            // chỉ có thể kết luận "cần cung cấp content_id" và task luôn thất bại. content.list là read-only.
            ["reviewer-agent"] = ["content.list", "content.review"],
            ["reviewer"] = ["content.list", "content.review"],

            ["publisher-agent"] = [],
            ["publisher"] = [],

            // Text summary only — no default tools by design.
            ["reporter-agent"] = [],
            ["reporter"] = [],
        };

    /// <summary>
    /// Resolve tool names for a catalog entry: explicit AllowedToolsJson if non-empty,
    /// otherwise defaults for Code / ShortName / AgentType.
    /// </summary>
    public static string[] ResolveToolNames(AgentDefinitionCatalogEntry definition)
    {
        var explicitNames = ParseJsonArray(definition.AllowedToolsJson);
        if (explicitNames.Length > 0)
            return explicitNames;

        return ResolveDefaultToolNames(definition.Code, definition.ShortName, definition.AgentType);
    }

    /// <summary>Default tool names for an agent identity when grants are empty.</summary>
    public static string[] ResolveDefaultToolNames(string? code, string? shortName = null, string? agentType = null)
    {
        if (TryGet(code, out var byCode))
            return byCode;
        if (TryGet(shortName, out var byShort))
            return byShort;
        if (TryGet(agentType, out var byType))
            return byType;
        return [];
    }

    private static bool TryGet(string? key, out string[] tools)
    {
        tools = [];
        if (string.IsNullOrWhiteSpace(key))
            return false;
        return ByAgentKey.TryGetValue(key.Trim(), out tools!);
    }

    private static string[] ParseJsonArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string[]>(json.Trim())?
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToArray() ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }
}
