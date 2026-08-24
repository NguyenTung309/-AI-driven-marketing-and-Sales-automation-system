namespace Clawbot.Agents.Core.Chat;

// A fully-resolved, decrypted provider config for a single agent call.
// The API key is plaintext here (decrypted at resolve time) and must never be logged or persisted.
// Phase 2.12: ConfigId/UpdatedAt/SupportsVision support review-path vision capability resolution.
public sealed record ResolvedLlmConfig(
    string Provider,
    string Model,
    string ApiKey,
    string? BaseUrl,
    decimal? InputUsdPer1M,
    decimal? OutputUsdPer1M,
    int? TimeoutSeconds = null,
    int? MaxOutputTokens = null,
    Guid? ConfigId = null,
    DateTimeOffset? ConfigUpdatedAt = null,
    bool? SupportsVision = null,
    // Model gốc khai trên LlmConfig (không qua override của binding) — dùng để retry khi model
    // override bị provider chốt unavailable.
    string? ConfigModelId = null);

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

// Provider chốt lỗi CẤP MODEL (model không tồn tại / hết kênh phục vụ) — khác hẳn lỗi auth/hạ tầng
// chung. Caller dùng tín hiệu này để fallback về model chuẩn của config thay vì ghi failed vô định
// (bug 2026-08-23: model override cũ trên binding sống sót qua lần rebind -> 503 model_not_found
// -> review kẹt "review_terminal_incomplete").
public sealed class LlmModelUnavailableException(string model, int statusCode, string detail)
    : Exception(FormattableString.Invariant($"llm_model_unavailable:{model} (HTTP {statusCode})"))
{
    public const int DetailMaxLength = 300;

    public string Model { get; } = model;
    public int StatusCode { get; } = statusCode;
    public string Detail { get; } =
        detail.Length <= DetailMaxLength ? detail : detail[..DetailMaxLength];
}

// Nhận diện lỗi cấp model từ body provider. Chỉ tin MARKER trong body (one-api/new-api trả
// "model_not_found"/"无可用渠道", gateway trả "Không có kênh khả dụng cho model ..."), đừng đoán
// theo mã HTTP — 5xx vẫn thường là sự cố hạ tầng thông thường.
public static class LlmModelAvailability
{
    public static bool IsUnavailable(string? responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
            return false;

        var lower = responseBody.ToLowerInvariant();
        return lower.Contains("model_not_found", StringComparison.Ordinal)
            || lower.Contains("model not found", StringComparison.Ordinal)
            || lower.Contains("no available channel", StringComparison.Ordinal)
            || lower.Contains("không có kênh khả dụng", StringComparison.Ordinal)
            || lower.Contains("无可用渠道", StringComparison.Ordinal)
            || (lower.Contains("model", StringComparison.Ordinal)
                && lower.Contains("does not exist", StringComparison.Ordinal));
    }
}
