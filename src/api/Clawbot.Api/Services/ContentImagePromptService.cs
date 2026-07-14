using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Skills.Content;
using Clawbot.Api.Contracts.Content;
using Clawbot.SharedKernel.Multitenancy;

namespace Clawbot.Api.Services;

public sealed class ContentImagePromptService(
    IImagePromptGenerator generator,
    ILlmCallScope llmScope,
    ITenantAccessor tenants)
{
    private const string AgentCode = "content-agent";

    private static readonly HashSet<string> SupportedPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "facebook",
        "instagram",
        "tiktok",
        "youtube",
        "zalo",
    };

    private readonly IImagePromptGenerator _generator = generator;
    private readonly ILlmCallScope _llmScope = llmScope;
    private readonly ITenantAccessor _tenants = tenants;

    // Đường HTTP: tenant lấy từ request context.
    public Task<GenerateImagePromptResponse> GenerateAsync(
        GenerateImagePromptRequest request,
        CancellationToken ct = default) =>
        GenerateAsync(_tenants.Require().TenantId, request, ct);

    // Đường job nền: KHÔNG có HTTP context nên ITenantAccessor.Require() sẽ throw — tenant phải truyền vào.
    public async Task<GenerateImagePromptResponse> GenerateAsync(
        Guid tenantId,
        GenerateImagePromptRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var _llm = _llmScope.Begin(tenantId, AgentCode);

        var brief = request.Brief?.Trim();
        if (string.IsNullOrWhiteSpace(brief))
            throw new ArgumentException("brief required", nameof(request));

        var platform = request.Platform?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(platform) || !SupportedPlatforms.Contains(platform))
            throw new ArgumentException("unsupported platform", nameof(request));

        var style = string.IsNullOrWhiteSpace(request.Style)
            ? "brand-safe education marketing"
            : request.Style.Trim();
        var brandTokens = request.BrandTokens?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList() ?? [];

        var result = await _generator.GenerateAsync(
            new ImagePromptRequest(brief, platform, style, brandTokens),
            ct).ConfigureAwait(false);

        return new GenerateImagePromptResponse(
            result.Prompt,
            result.NegativePrompt,
            result.Hints);
    }
}
