using System.Diagnostics;
using Clawbot.Agents.Core.Chat;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Llm;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// Per-tenant LLM provider configuration (Anthropic / OpenAI-compatible).
// Credentials are encrypted at rest (IEncryptor) and never returned (masked via HasApiKey).
public static class LlmConfigsEndpoints
{
    private static readonly HashSet<string> AllowedProviders =
        new(StringComparer.OrdinalIgnoreCase) { "anthropic", "openai", "openai-compatible" };

    public static IEndpointRouteBuilder MapLlmConfigs(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/llm-configs")
            .RequirePermission("llm-configs:manage")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapPost("/", CreateAsync);
        grp.MapPut("/{id:guid}", UpdateAsync);
        grp.MapPost("/{id:guid}/rotate-key", RotateKeyAsync);
        grp.MapPost("/{id:guid}/activate", ActivateAsync);
        grp.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        grp.MapPost("/{id:guid}/test", TestAsync);
        grp.MapDelete("/{id:guid}", DeleteAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        var rows = await db.LlmConfigs
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows.Select(Map));
    }

    private static async Task<IResult> CreateAsync(
        CreateLlmConfigRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiKey)) return Results.BadRequest(new { error = "api_key_required" });
        if (Validate(req.Provider, req.ModelId, req.BaseUrl, req.InputUsdPer1M, req.OutputUsdPer1M) is { } err)
            return Results.BadRequest(new { error = err });

        var tenantId = tenants.Require().TenantId;
        var now = clock.UtcNow;
        var provider = req.Provider.Trim().ToLowerInvariant();
        var row = LlmConfig.Create(
            tenantId,
            provider,
            req.ModelId.Trim(),
            encryptor.Encrypt(req.ApiKey),
            now,
            baseUrl: NormalizeBaseUrl(provider, req.BaseUrl),
            displayName: Trimmed(req.DisplayName),
            inputUsdPer1M: req.InputUsdPer1M,
            outputUsdPer1M: req.OutputUsdPer1M);

        db.LlmConfigs.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/llm-configs/{row.Id}", Map(row));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateLlmConfigRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        CancellationToken ct)
    {
        if (Validate(req.Provider, req.ModelId, req.BaseUrl, req.InputUsdPer1M, req.OutputUsdPer1M) is { } err)
            return Results.BadRequest(new { error = err });

        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();

        var now = clock.UtcNow;
        var provider = req.Provider.Trim().ToLowerInvariant();
        var modelId = req.ModelId.Trim();
        var boundAgentModels = await db.AgentConfigs
            .Where(a => a.TenantId == row.TenantId && a.LlmConfigId == row.Id && a.DeletedAt == null)
            .Select(a => a.Model)
            .ToListAsync(ct).ConfigureAwait(false);
        if (!AreBoundAgentModelsCompatible(provider, modelId, boundAgentModels))
            return Results.BadRequest(new { error = "model_provider_mismatch" });

        var baseUrl = NormalizeBaseUrl(provider, req.BaseUrl);
        var credentialEndpointChanged = !string.Equals(row.Provider, provider, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(row.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
        row.UpdateConnection(provider, modelId, baseUrl, Trimmed(req.DisplayName), now);
        row.UpdateRates(req.InputUsdPer1M, req.OutputUsdPer1M, now);
        if (credentialEndpointChanged)
            row.RequireKeyRotation(now);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Map(row));
    }

    private static async Task<IResult> RotateKeyAsync(
        Guid id,
        RotateLlmKeyRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiKey)) return Results.BadRequest(new { error = "api_key_required" });
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();

        row.RotateApiKey(encryptor.Encrypt(req.ApiKey), clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static Task<IResult> ActivateAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
        => SetActiveAsync(id, true, db, tenants, clock, ct);

    private static Task<IResult> DeactivateAsync(Guid id, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
        => SetActiveAsync(id, false, db, tenants, clock, ct);

    private static async Task<IResult> SetActiveAsync(Guid id, bool active, AppDbContext db, ITenantAccessor tenants, IClock clock, CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();

        if (active)
        {
            if (string.IsNullOrWhiteSpace(row.ApiKeyEncrypted))
                return Results.BadRequest(new { error = "llm_config_requires_key_rotation" });
            row.Activate(clock.UtcNow);
        }
        else row.Deactivate(clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Map(row));
    }

    // Minimal 1-shot ping (tiny max-tokens) to validate key/baseUrl/model before activation/binding.
    private static async Task<IResult> TestAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        ILlmChatClientFactory factory,
        CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(row.ApiKeyEncrypted))
            return Results.Ok(new TestLlmConfigResponse(false, 0, "llm_config_requires_key_rotation"));

        var sw = Stopwatch.StartNew();
        try
        {
            var resolved = new ResolvedLlmConfig(
                row.Provider, row.ModelId, encryptor.Decrypt(row.ApiKeyEncrypted), row.BaseUrl,
                row.InputUsdPer1M, row.OutputUsdPer1M);
            var client = factory.Create(resolved);
            await client.CompleteAsync("You are a connection test. Reply with 'ok'.", Array.Empty<ChatTurn>(), "ping", ct)
                .ConfigureAwait(false);
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(true, sw.ElapsedMilliseconds));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(false, sw.ElapsedMilliseconds, SafeTestConnectionError(ex)));
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NoContent();

        var isBound = await db.AgentConfigs
            .AnyAsync(a => a.TenantId == row.TenantId && a.LlmConfigId == row.Id && a.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (isBound) return Results.BadRequest(new { error = "llm_config_in_use" });

        db.LlmConfigs.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<LlmConfig?> FindAsync(AppDbContext db, ITenantAccessor tenants, Guid id, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        return await db.LlmConfigs.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct).ConfigureAwait(false);
    }

    private static LlmConfigDto Map(LlmConfig c) => new(
        c.Id, c.Provider, c.ModelId, c.DisplayName,
        HasApiKey: !string.IsNullOrEmpty(c.ApiKeyEncrypted),
        c.BaseUrl, c.IsActive,
        c.InputUsdPer1M, c.OutputUsdPer1M, c.CreatedAt, c.UpdatedAt);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string SafeTestConnectionError(Exception _) =>
        "llm_connection_test_failed";

    // D10 — make the per-provider baseUrl suffix difference invisible to the admin. The OpenAI SDK
    // appends only `/chat/completions` (endpoint must already carry `/v1`), while AnthropicChatClient
    // appends `/v1/messages` to a bare host. Normalize so the admin enters the host the same way for both.
    internal static string? NormalizeBaseUrl(string provider, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var url = baseUrl.Trim().TrimEnd('/');
        return provider switch
        {
            "openai" or "openai-compatible" => url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url : url + "/v1",
            "anthropic" => url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url[..^"/v1".Length] : url,
            _ => url,
        };
    }

    // Boundary validation: provider enum, https-only baseUrl (SSRF guard), non-negative cost rates.
    private static string? Validate(
        string? provider, string? modelId, string? baseUrl,
        decimal? inputRate, decimal? outputRate)
    {
        if (string.IsNullOrWhiteSpace(provider) || !AllowedProviders.Contains(provider.Trim()))
            return "invalid_provider";
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Trim().Length > 128)
            return "invalid_model_id";
        if (!string.IsNullOrWhiteSpace(baseUrl) && !IsAllowedBaseUrl(baseUrl.Trim()))
            return "invalid_base_url";
        if (inputRate is < 0m || outputRate is < 0m)
            return "invalid_rate";
        return null;
    }

    internal static bool IsAllowedBaseUrl(string baseUrl) =>
        LlmBaseUrlGuard.IsAllowedBaseUrl(baseUrl);

    internal static bool AreBoundAgentModelsCompatible(
        string provider,
        string configModel,
        IEnumerable<string> boundAgentModels) =>
        AgentsEndpoints.IsModelCompatibleWithProvider(provider, configModel)
        && boundAgentModels.All(model => AgentsEndpoints.IsModelCompatibleWithProvider(
            provider,
            string.IsNullOrWhiteSpace(model) ? configModel : model));
}
