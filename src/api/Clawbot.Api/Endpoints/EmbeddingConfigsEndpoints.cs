using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Llm;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Llm;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using OpenAI;
using OpenAI.Embeddings;

namespace Clawbot.Api.Endpoints;

public static class EmbeddingConfigsEndpoints
{
    private static readonly HashSet<string> AllowedProviders =
        new(StringComparer.OrdinalIgnoreCase) { "openai", "openai-compatible", "hash" };

    public static IEndpointRouteBuilder MapEmbeddingConfigs(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/embedding-configs/status", StatusAsync)
            .RequirePermission("kb:read")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        var grp = app.MapGroup("/api/embedding-configs")
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
        var rows = await db.EmbeddingConfigs
            .Where(c => c.TenantId == tenantId)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(rows.Select(Map));
    }

    private static async Task<IResult> StatusAsync(IEmbeddingProvider embedder, ITenantAccessor tenants, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        if (embedder is ConfiguredEmbeddingProvider configured)
        {
            var cfg = await configured.ResolveConfigAsync(tenantId, ct).ConfigureAwait(false);
            return Results.Ok(new EmbeddingStatusResponse(
                Configured: !cfg.IsFallback,
                cfg.Provider,
                cfg.ModelId,
                cfg.Dimension,
                cfg.Source,
                cfg.IsFallback,
                cfg.DisplayName));
        }

        return Results.Ok(new EmbeddingStatusResponse(true, "unknown", "unknown", embedder.Dimension, "runtime", false));
    }

    private static async Task<IResult> CreateAsync(
        CreateEmbeddingConfigRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        IClock clock,
        IConfiguration config,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (Validate(req.Provider, req.ModelId, req.Dimension, req.ApiKey, req.BaseUrl, isCreate: true, AllowPrivateBaseUrls(config, env)) is { } err)
            return Results.BadRequest(new { error = err });

        var tenantId = tenants.Require().TenantId;
        var now = clock.UtcNow;
        var provider = req.Provider.Trim().ToLowerInvariant();
        var row = EmbeddingConfig.Create(
            tenantId,
            provider,
            NormalizeModelId(provider, req.ModelId),
            provider == "hash" ? string.Empty : encryptor.Encrypt(req.ApiKey!),
            provider == "hash" ? 384 : req.Dimension,
            now,
            NormalizeBaseUrl(provider, req.BaseUrl),
            Trimmed(req.DisplayName));

        await DeactivateOthersAsync(db, tenantId, null, now, ct).ConfigureAwait(false);
        db.EmbeddingConfigs.Add(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Created($"/api/embedding-configs/{row.Id}", Map(row));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateEmbeddingConfigRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        IConfiguration config,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (Validate(req.Provider, req.ModelId, req.Dimension, null, req.BaseUrl, isCreate: false, AllowPrivateBaseUrls(config, env)) is { } err)
            return Results.BadRequest(new { error = err });

        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();

        var now = clock.UtcNow;
        var provider = req.Provider.Trim().ToLowerInvariant();
        var baseUrl = NormalizeBaseUrl(provider, req.BaseUrl);
        var credentialEndpointChanged = !string.Equals(row.Provider, provider, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(row.BaseUrl, baseUrl, StringComparison.OrdinalIgnoreCase);
        row.UpdateConnection(provider, NormalizeModelId(provider, req.ModelId), baseUrl, Trimmed(req.DisplayName), req.Dimension, now);
        if (provider == "hash") row.RotateApiKey(string.Empty, now);
        else if (credentialEndpointChanged) row.RequireKeyRotation(now);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Map(row));
    }

    private static async Task<IResult> RotateKeyAsync(
        Guid id,
        RotateEmbeddingKeyRequest req,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        IClock clock,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiKey)) return Results.BadRequest(new { error = "api_key_required" });
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();
        if (row.Provider.Equals("hash", StringComparison.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "hash_provider_has_no_key" });

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

        var now = clock.UtcNow;
        if (active)
        {
            if (!row.Provider.Equals("hash", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(row.ApiKeyEncrypted))
                return Results.BadRequest(new { error = "embedding_config_requires_key_rotation" });
            await DeactivateOthersAsync(db, row.TenantId, row.Id, now, ct).ConfigureAwait(false);
            row.Activate(now);
        }
        else row.Deactivate(now);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(Map(row));
    }

    private static async Task<IResult> TestAsync(
        Guid id,
        AppDbContext db,
        ITenantAccessor tenants,
        IEncryptor encryptor,
        IConfiguration config,
        IHostEnvironment env,
        CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();
        if (row.Provider.Equals("hash", StringComparison.OrdinalIgnoreCase))
            return Results.Ok(new TestLlmConfigResponse(true, 0));
        if (string.IsNullOrWhiteSpace(row.ApiKeyEncrypted))
            return Results.Ok(new TestLlmConfigResponse(false, 0, "embedding_config_requires_key_rotation"));

        var sw = Stopwatch.StartNew();
        try
        {
            var opts = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(row.BaseUrl))
            {
                var endpoint = new Uri(row.BaseUrl, UriKind.Absolute);
                opts.Endpoint = endpoint;
                opts.Transport = new HttpClientPipelineTransport(
                    LlmBaseUrlGuard.CreateGuardedHttpClient(endpoint, AllowPrivateBaseUrls(config, env)));
            }
            var client = new EmbeddingClient(row.ModelId, new ApiKeyCredential(encryptor.Decrypt(row.ApiKeyEncrypted)), opts);
            await client.GenerateEmbeddingAsync("ping", new EmbeddingGenerationOptions { Dimensions = row.Dimension }, ct)
                .ConfigureAwait(false);
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(true, sw.ElapsedMilliseconds));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(false, sw.ElapsedMilliseconds, "embedding_connection_test_failed"));
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NoContent();
        db.EmbeddingConfigs.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.NoContent();
    }

    private static async Task<EmbeddingConfig?> FindAsync(AppDbContext db, ITenantAccessor tenants, Guid id, CancellationToken ct)
    {
        var tenantId = tenants.Require().TenantId;
        return await db.EmbeddingConfigs.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct).ConfigureAwait(false);
    }

    private static async Task DeactivateOthersAsync(AppDbContext db, Guid tenantId, Guid? exceptId, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.EmbeddingConfigs
            .Where(c => c.TenantId == tenantId && c.IsActive && c.Id != exceptId)
            .ToListAsync(ct).ConfigureAwait(false);
        foreach (var row in rows) row.Deactivate(now);
    }

    private static EmbeddingConfigDto Map(EmbeddingConfig c) => new(
        c.Id, c.Provider, c.ModelId, c.DisplayName,
        HasApiKey: !string.IsNullOrEmpty(c.ApiKeyEncrypted),
        c.BaseUrl, c.Dimension, c.IsActive, c.CreatedAt, c.UpdatedAt);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeModelId(string provider, string modelId) =>
        provider.Equals("hash", StringComparison.OrdinalIgnoreCase) ? "hash-384" : modelId.Trim();

    internal static string? NormalizeBaseUrl(string provider, string? baseUrl)
    {
        if (provider.Equals("hash", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(baseUrl)) return null;
        var url = baseUrl.Trim().TrimEnd('/');
        return url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url : url + "/v1";
    }

    private const int MinDimension = 64;
    private const int MaxDimension = 4096;

    private static string? Validate(
        string? provider,
        string? modelId,
        int dimension,
        string? apiKey,
        string? baseUrl,
        bool isCreate,
        bool allowPrivateBaseUrls = false)
    {
        if (string.IsNullOrWhiteSpace(provider) || !AllowedProviders.Contains(provider.Trim())) return "invalid_provider";
        var normalizedProvider = provider.Trim().ToLowerInvariant();
        if (dimension is < MinDimension or > MaxDimension) return "invalid_dimension";
        if (normalizedProvider == "hash") return null;
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Trim().Length > 128) return "invalid_model_id";
        if (isCreate && string.IsNullOrWhiteSpace(apiKey)) return "api_key_required";
        if (!string.IsNullOrWhiteSpace(baseUrl) && !LlmBaseUrlGuard.IsAllowedBaseUrl(baseUrl.Trim(), allowPrivateBaseUrls))
            return "invalid_base_url";
        return null;
    }

    internal static bool AllowPrivateBaseUrls(IConfiguration config, IHostEnvironment env) =>
        env.IsDevelopment() && config.GetValue<bool>($"{LlmBaseUrlOptions.SectionName}:AllowPrivate");
}
