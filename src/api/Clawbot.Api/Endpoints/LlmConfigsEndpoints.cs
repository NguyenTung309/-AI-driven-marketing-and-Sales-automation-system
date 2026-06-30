using System.ClientModel;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
public static partial class LlmConfigsEndpoints
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
        IConfiguration config,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiKey)) return Results.BadRequest(new { error = "api_key_required" });
        if (Validate(req.Provider, req.ModelId, req.BaseUrl, req.InputUsdPer1M, req.OutputUsdPer1M, req.TimeoutSeconds, req.MaxOutputTokens, AllowPrivateBaseUrls(config, env)) is { } err)
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
            outputUsdPer1M: req.OutputUsdPer1M,
            timeoutSeconds: req.TimeoutSeconds,
            maxOutputTokens: req.MaxOutputTokens);

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
        IConfiguration config,
        IHostEnvironment env,
        CancellationToken ct)
    {
        if (Validate(req.Provider, req.ModelId, req.BaseUrl, req.InputUsdPer1M, req.OutputUsdPer1M, req.TimeoutSeconds, req.MaxOutputTokens, AllowPrivateBaseUrls(config, env)) is { } err)
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
        row.UpdateConnection(provider, modelId, baseUrl, Trimmed(req.DisplayName), now, req.TimeoutSeconds, req.MaxOutputTokens);
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
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.ApiKey)) return Results.BadRequest(new { error = "api_key_required" });
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();

        row.RotateApiKey(encryptor.Encrypt(req.ApiKey), clock.UtcNow);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        LogLlmConfigKeyRotated(
            loggerFactory.CreateLogger(nameof(LlmConfigsEndpoints)), row.Id, row.Provider, row.ModelId, row.BaseUrl,
            MaskSecret(req.ApiKey), SecretHash(req.ApiKey), req.ApiKey.Trim().Length);
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
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();
        if (string.IsNullOrWhiteSpace(row.ApiKeyEncrypted))
            return Results.Ok(new TestLlmConfigResponse(false, 0, "llm_config_requires_key_rotation"));

        var sw = Stopwatch.StartNew();
        var logger = loggerFactory.CreateLogger(nameof(LlmConfigsEndpoints));
        string? apiKey = null;
        try
        {
            apiKey = encryptor.Decrypt(row.ApiKeyEncrypted);
            var resolved = new ResolvedLlmConfig(
                row.Provider, row.ModelId, apiKey, row.BaseUrl,
                row.InputUsdPer1M, row.OutputUsdPer1M, row.TimeoutSeconds, row.MaxOutputTokens);
            var client = factory.Create(resolved);
            await client.CompleteAsync("You are a connection test. Reply with 'ok'.", Array.Empty<ChatTurn>(), "ping", ct)
                .ConfigureAwait(false);
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(true, sw.ElapsedMilliseconds));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            var error = SafeTestConnectionError(ex);
            LogLlmConfigTestFailed(
                logger, row.Id, row.Provider, row.ModelId, row.BaseUrl, MaskSecret(apiKey), SecretHash(apiKey), apiKey?.Trim().Length ?? 0, TestConnectionStatus(ex), error);
            if (apiKey is not null && IsOpenAiProvider(row.Provider) && row.BaseUrl is not null && TestConnectionStatus(ex) is 401 or 403)
                await LogOpenAiCompatibleProbeAsync(logger, row, apiKey, ct).ConfigureAwait(false);
            return Results.Ok(new TestLlmConfigResponse(false, sw.ElapsedMilliseconds, error));
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
        c.InputUsdPer1M, c.OutputUsdPer1M, c.CreatedAt, c.UpdatedAt, c.TimeoutSeconds, c.MaxOutputTokens);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static string MaskSecret(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return "<empty>";
        var trimmed = secret.Trim();
        if (trimmed.Length <= 10) return $"***{trimmed[^2..]}";
        return $"{trimmed[..6]}...{trimmed[^4..]}";
    }

    internal static string SecretHash(string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) return "<empty>";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim()));
        return Convert.ToHexString(bytes)[..12];
    }

    internal static int TestConnectionStatus(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: { } status } => (int)status,
        ClientResultException cre => cre.Status,
        _ => 0
    };

    private static bool IsOpenAiProvider(string provider) =>
        provider.Equals("openai", StringComparison.OrdinalIgnoreCase)
        || provider.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeBearerToken(string apiKey)
    {
        const string bearerPrefix = "Bearer ";
        var trimmed = apiKey.Trim();
        return trimmed.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[bearerPrefix.Length..].TrimStart()
            : trimmed;
    }

    private static async Task LogOpenAiCompatibleProbeAsync(ILogger logger, LlmConfig row, string apiKey, CancellationToken ct)
    {
        const int responseLimit = 512;
        var url = row.BaseUrl!.TrimEnd('/') + "/chat/completions";
        var body = JsonSerializer.Serialize(new
        {
            model = row.ModelId,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new[] { new { type = "text", text = "ping" } }
                }
            }
        });

        try
        {
            var http = LlmBaseUrlGuard.CreateGuardedHttpClient(new Uri(row.BaseUrl, UriKind.Absolute), timeoutSeconds: row.TimeoutSeconds ?? 120);
            using var req = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NormalizeBearerToken(apiKey));
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);
            var response = await res.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (response.Length > responseLimit) response = response[..responseLimit];
            LogOpenAiCompatibleProbe(logger, url, MaskSecret(apiKey), SecretHash(apiKey), apiKey.Trim().Length, body, (int)res.StatusCode, response);
        }
        catch (Exception probeEx) when (probeEx is not OperationCanceledException)
        {
            LogOpenAiCompatibleProbeFailed(logger, url, probeEx.GetType().Name);
        }
    }

    [LoggerMessage(EventId = 7200, Level = LogLevel.Information,
        Message = "LLM config key rotated: configId={ConfigId} provider={Provider} model={Model} baseUrl={BaseUrl} key={KeyHint} keyHash={KeyHash} keyLength={KeyLength}")]
    private static partial void LogLlmConfigKeyRotated(
        ILogger logger,
        Guid configId,
        string provider,
        string model,
        string? baseUrl,
        string keyHint,
        string keyHash,
        int keyLength);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning,
        Message = "LLM config test failed: configId={ConfigId} provider={Provider} model={Model} baseUrl={BaseUrl} key={KeyHint} keyHash={KeyHash} keyLength={KeyLength} status={Status} error={Error}")]
    private static partial void LogLlmConfigTestFailed(
        ILogger logger,
        Guid configId,
        string provider,
        string model,
        string? baseUrl,
        string keyHint,
        string keyHash,
        int keyLength,
        int status,
        string error);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning,
        Message = "OpenAI-compatible direct probe: url={Url} key={KeyHint} keyHash={KeyHash} keyLength={KeyLength} body={Body} status={Status} response={Response}")]
    private static partial void LogOpenAiCompatibleProbe(
        ILogger logger,
        string url,
        string keyHint,
        string keyHash,
        int keyLength,
        string body,
        int status,
        string response);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Warning,
        Message = "OpenAI-compatible direct probe failed before response: url={Url} exception={ExceptionType}")]
    private static partial void LogOpenAiCompatibleProbeFailed(
        ILogger logger,
        string url,
        string exceptionType);

    internal static string SafeTestConnectionError(Exception ex) => ex switch
    {
        TimeoutException or TaskCanceledException => "llm_connection_timeout",
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "llm_connection_auth_failed",
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => "llm_connection_rate_limited",
        HttpRequestException { StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.UnprocessableEntity } => "llm_connection_invalid_request",
        HttpRequestException { StatusCode: null } => "llm_connection_unreachable",
        HttpRequestException => "llm_connection_upstream_error",
        ClientResultException { Status: 401 or 403 } => "llm_connection_auth_failed",
        ClientResultException { Status: 429 } => "llm_connection_rate_limited",
        ClientResultException { Status: 400 or 404 or 422 } => "llm_connection_invalid_request",
        ClientResultException => "llm_connection_upstream_error",
        NotSupportedException => "llm_connection_provider_unsupported",
        _ => "llm_connection_test_failed"
    };

    // D10 — make the per-provider baseUrl suffix difference invisible to the admin. The OpenAI SDK
    // appends only `/chat/completions` (endpoint must already carry `/v1`), while AnthropicChatClient
    // appends `/v1/messages` to a bare host. Normalize so the admin enters the host the same way for both.
    internal static string? NormalizeBaseUrl(string provider, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var url = baseUrl.Trim().TrimEnd('/');
        return provider switch
        {
            "openai" => NormalizeOpenAiBaseUrl(url, forceV1: true),
            "openai-compatible" => NormalizeOpenAiBaseUrl(url, forceV1: false),
            "anthropic" => url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url[..^"/v1".Length] : url,
            _ => url,
        };
    }

    private static string NormalizeOpenAiBaseUrl(string url, bool forceV1)
    {
        const string chatCompletionsPath = "/chat/completions";
        if (url.EndsWith(chatCompletionsPath, StringComparison.OrdinalIgnoreCase))
            url = url[..^chatCompletionsPath.Length];
        return forceV1 && !url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url + "/v1" : url;
    }

    // Request timeout bounds: 1s floor, 600s (10 min) ceiling. null → global default.
    private const int MaxTimeoutSeconds = 600;
    // Output-token cap bounds: 1 floor, 200k ceiling. null → provider default (3000).
    private const int MaxOutputTokensCeiling = 200_000;

    // Boundary validation: provider enum, https-only baseUrl (SSRF guard), non-negative cost rates, timeout/token ranges.
    private static string? Validate(
        string? provider, string? modelId, string? baseUrl,
        decimal? inputRate, decimal? outputRate, int? timeoutSeconds, int? maxOutputTokens,
        bool allowPrivateBaseUrls = false)
    {
        if (string.IsNullOrWhiteSpace(provider) || !AllowedProviders.Contains(provider.Trim()))
            return "invalid_provider";
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Trim().Length > 128)
            return "invalid_model_id";
        if (!string.IsNullOrWhiteSpace(baseUrl) && !IsAllowedBaseUrl(baseUrl.Trim(), allowPrivateBaseUrls))
            return "invalid_base_url";
        if (inputRate is < 0m || outputRate is < 0m)
            return "invalid_rate";
        if (timeoutSeconds is < 1 or > MaxTimeoutSeconds)
            return "invalid_timeout";
        if (maxOutputTokens is < 1 or > MaxOutputTokensCeiling)
            return "invalid_max_output_tokens";
        return null;
    }

    internal static bool AllowPrivateBaseUrls(IConfiguration config, IHostEnvironment env) =>
        env.IsDevelopment() && config.GetValue<bool>($"{LlmBaseUrlOptions.SectionName}:AllowPrivate");

    internal static bool IsAllowedBaseUrl(string baseUrl, bool allowPrivateBaseUrls = false) =>
        LlmBaseUrlGuard.IsAllowedBaseUrl(baseUrl, allowPrivateBaseUrls);

    internal static bool AreBoundAgentModelsCompatible(
        string provider,
        string configModel,
        IEnumerable<string> boundAgentModels) =>
        AgentsEndpoints.IsModelCompatibleWithProvider(provider, configModel)
        && boundAgentModels.All(model => AgentsEndpoints.IsModelCompatibleWithProvider(
            provider,
            string.IsNullOrWhiteSpace(model) ? configModel : model));
}
