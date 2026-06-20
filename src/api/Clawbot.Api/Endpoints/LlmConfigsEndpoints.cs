using System.Diagnostics;
using System.Net;
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
        new(StringComparer.OrdinalIgnoreCase) { "anthropic", "openai" };

    private const int MinTokens = 128;
    private const int MaxTokens = 32_000;
    private const int TestPingMaxTokens = 16;

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
        if (Validate(req.Provider, req.ModelId, req.BaseUrl, req.MaxTokens, req.Temperature, req.InputUsdPer1M, req.OutputUsdPer1M) is { } err)
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
            maxTokens: req.MaxTokens,
            temperature: req.Temperature,
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
        if (Validate(req.Provider, req.ModelId, req.BaseUrl, req.MaxTokens, req.Temperature, req.InputUsdPer1M, req.OutputUsdPer1M) is { } err)
            return Results.BadRequest(new { error = err });

        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NotFound();

        var now = clock.UtcNow;
        var provider = req.Provider.Trim().ToLowerInvariant();
        row.UpdateConnection(provider, req.ModelId.Trim(), NormalizeBaseUrl(provider, req.BaseUrl), Trimmed(req.DisplayName), now);
        row.UpdateDefaults(req.MaxTokens, req.Temperature, now);
        row.UpdateRates(req.InputUsdPer1M, req.OutputUsdPer1M, now);

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

        if (active) row.Activate(clock.UtcNow);
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

        var resolved = new ResolvedLlmConfig(
            row.Provider, row.ModelId, encryptor.Decrypt(row.ApiKeyEncrypted), row.BaseUrl,
            MaxTokens: TestPingMaxTokens, row.Temperature, row.InputUsdPer1M, row.OutputUsdPer1M);

        var sw = Stopwatch.StartNew();
        try
        {
            var client = factory.Create(resolved);
            await client.CompleteAsync("You are a connection test. Reply with 'ok'.", Array.Empty<ChatTurn>(), "ping", ct)
                .ConfigureAwait(false);
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(true, sw.ElapsedMilliseconds));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            return Results.Ok(new TestLlmConfigResponse(false, sw.ElapsedMilliseconds, ex.Message));
        }
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, ITenantAccessor tenants, CancellationToken ct)
    {
        var row = await FindAsync(db, tenants, id, ct).ConfigureAwait(false);
        if (row is null) return Results.NoContent();

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
        c.BaseUrl, c.IsActive, c.MaxTokens, c.Temperature,
        c.InputUsdPer1M, c.OutputUsdPer1M, c.CreatedAt, c.UpdatedAt);

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // D10 — make the per-provider baseUrl suffix difference invisible to the admin. The OpenAI SDK
    // appends only `/chat/completions` (endpoint must already carry `/v1`), while AnthropicChatClient
    // appends `/v1/messages` to a bare host. Normalize so the admin enters the host the same way for both.
    internal static string? NormalizeBaseUrl(string provider, string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) return null;
        var url = baseUrl.Trim().TrimEnd('/');
        return provider switch
        {
            "openai" => url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url : url + "/v1",
            "anthropic" => url.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? url[..^"/v1".Length] : url,
            _ => url,
        };
    }

    // Boundary validation: provider enum, https-only baseUrl (SSRF guard), numeric clamps.
    private static string? Validate(
        string? provider, string? modelId, string? baseUrl,
        int? maxTokens, decimal? temperature, decimal? inputRate, decimal? outputRate)
    {
        if (string.IsNullOrWhiteSpace(provider) || !AllowedProviders.Contains(provider.Trim()))
            return "invalid_provider";
        if (string.IsNullOrWhiteSpace(modelId) || modelId.Trim().Length > 128)
            return "invalid_model_id";
        if (!string.IsNullOrWhiteSpace(baseUrl) && !IsAllowedBaseUrl(baseUrl.Trim()))
            return "invalid_base_url";
        if (maxTokens is { } mt && (mt < MinTokens || mt > MaxTokens))
            return "invalid_max_tokens";
        if (temperature is { } t && (t < 0m || t > 2m))
            return "invalid_temperature";
        if (inputRate is < 0m || outputRate is < 0m)
            return "invalid_rate";
        return null;
    }

    internal static bool IsAllowedBaseUrl(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        return !IsPrivateHost(uri);
    }

    private static bool IsPrivateHost(Uri uri)
    {
        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var ip)) return false; // DNS names allowed; only block literal private IPs

        if (IPAddress.IsLoopback(ip)) return true;
        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] switch
            {
                10 => true,
                127 => true,
                169 when bytes[1] == 254 => true,                  // link-local
                172 when bytes[1] is >= 16 and <= 31 => true,
                192 when bytes[1] == 168 => true,
                _ => false,
            };
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
    }
}
