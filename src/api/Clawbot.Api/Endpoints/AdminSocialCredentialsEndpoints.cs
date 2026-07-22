using System.Text.Json;
using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Endpoints;

public sealed record SocialCredentialDto(
    string Provider,
    bool Enabled,
    string Endpoint,
    string PageId,
    string OaId,
    bool HasPageAccessToken,
    bool HasOaAccessToken,
    DateTimeOffset? UpdatedAt);

// Secret semantics: null = keep stored value, empty string = clear.
public sealed record UpdateSocialCredentialRequest(
    bool? Enabled = null,
    string? Endpoint = null,
    string? PageId = null,
    string? PageAccessToken = null,
    string? OaId = null,
    string? OaAccessToken = null);

// Zalo OA and optional standalone Instagram credentials are managed here. Facebook uses Meta OAuth.
public static class AdminSocialCredentialsEndpoints
{
    private static readonly string[] Providers = ["zalo", "instagram"];
    private const int MaxFieldChars = 512;
    private const int MaxEndpointChars = 2048;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAdminSocialCredentials(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/social-credentials")
            .RequireAuthorization()
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("", ListAsync).RequirePermission("system:config");
        grp.MapPut("/{provider}", UpdateAsync).RequirePermission("system:config");
        return app;
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        IEncryptor encryptor,
        IOptions<GraphPublisherOptions> options,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var rows = await db.SocialCredentials.AsNoTracking()
            .Where(c => Providers.Contains(c.Provider) && c.DeletedAt == null && c.PageId == null)
            .ToListAsync(ct).ConfigureAwait(false);

        var items = Providers
            .Select(provider =>
            {
                var row = rows.Find(r => string.Equals(r.Provider, provider, StringComparison.OrdinalIgnoreCase));
                var stored = row is null ? null : Decrypt(encryptor, row.CredentialsEncrypted);
                var effective = stored ?? Defaults(options.Value, provider);
                return ToDto(provider, effective, row?.UpdatedAt);
            })
            .ToList();

        return Results.Ok(new { items });
    }

    private static async Task<IResult> UpdateAsync(
        string provider,
        UpdateSocialCredentialRequest body,
        AppDbContext db,
        IEncryptor encryptor,
        IOptions<GraphPublisherOptions> options,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tenant = tenants.Require();
        var normalized = provider.Trim().ToLowerInvariant();
        if (!Providers.Contains(normalized))
            return Error(http, StatusCodes.Status400BadRequest, "admin.social_provider_invalid", "provider must be zalo or instagram; Facebook uses Meta OAuth.");
        if (TooLong(body.PageId) || TooLong(body.PageAccessToken) || TooLong(body.OaId) || TooLong(body.OaAccessToken))
            return Error(http, StatusCodes.Status400BadRequest, "admin.social_field_too_long", $"fields must be at most {MaxFieldChars} characters.");
        if (normalized == "instagram" && HasInstagramForeignFields(body))
            return Error(http, StatusCodes.Status400BadRequest, "admin.instagram_fields_invalid", "Instagram accepts only enabled, pageId, and pageAccessToken.");
        if (normalized == "zalo"
            && body.Endpoint is not null
            && !string.IsNullOrWhiteSpace(body.Endpoint)
            && !IsValidHttpsUrl(body.Endpoint))
        {
            return Error(http, StatusCodes.Status400BadRequest, "admin.social_endpoint_invalid", "endpoint must be an absolute https URL.");
        }

        var row = await db.SocialCredentials
            .FirstOrDefaultAsync(c => c.Provider == normalized && c.DeletedAt == null && c.PageId == null, ct)
            .ConfigureAwait(false);
        var stored = row is null ? null : Decrypt(encryptor, row.CredentialsEncrypted);
        if (normalized == "instagram"
            && row is not null
            && stored is null
            && !IsFullInstagramReplacement(body))
        {
            return Error(http, StatusCodes.Status400BadRequest, "admin.instagram_credentials_invalid", "Replace all Instagram credential fields before enabling the standalone override.");
        }

        var current = stored ?? Defaults(options.Value, normalized);
        var merged = normalized == "instagram"
            ? MergeInstagram(current, body)
            : MergeZalo(current, body);
        if (normalized == "instagram" && !IsValidInstagram(merged))
            return Error(http, StatusCodes.Status400BadRequest, "admin.instagram_credentials_invalid", "Enabled Instagram credentials require a numeric user ID and access token.");

        var encrypted = encryptor.Encrypt(JsonSerializer.Serialize(merged, JsonOpts));
        var now = clock.UtcNow;
        if (row is null)
        {
            db.SocialCredentials.Add(SocialCredential.Create(tenant.TenantId, normalized, encrypted, now));
        }
        else
        {
            row.UpdateCredentials(encrypted, now);
            row.Activate(now);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToDto(normalized, merged, now));
    }

    private static GraphChannelOptions Defaults(GraphPublisherOptions options, string provider) =>
        provider switch
        {
            "zalo" => options.Zalo,
            "instagram" => new GraphChannelOptions(),
            _ => new GraphChannelOptions(),
        };

    private static GraphChannelOptions MergeInstagram(
        GraphChannelOptions current,
        UpdateSocialCredentialRequest update) =>
        new()
        {
            Enabled = update.Enabled ?? current.Enabled,
            PageId = MergeText(current.PageId, update.PageId),
            PageAccessToken = MergeText(current.PageAccessToken, update.PageAccessToken),
        };

    private static GraphChannelOptions MergeZalo(
        GraphChannelOptions current,
        UpdateSocialCredentialRequest update) =>
        new()
        {
            Enabled = update.Enabled ?? current.Enabled,
            Endpoint = MergeText(current.Endpoint, update.Endpoint),
            PageId = MergeText(current.PageId, update.PageId),
            PageAccessToken = MergeText(current.PageAccessToken, update.PageAccessToken),
            OaId = MergeText(current.OaId, update.OaId),
            OaAccessToken = MergeText(current.OaAccessToken, update.OaAccessToken),
        };

    private static bool HasInstagramForeignFields(UpdateSocialCredentialRequest update) =>
        !string.IsNullOrWhiteSpace(update.Endpoint)
        || !string.IsNullOrWhiteSpace(update.OaId)
        || !string.IsNullOrWhiteSpace(update.OaAccessToken);

    private static bool IsFullInstagramReplacement(UpdateSocialCredentialRequest update) =>
        update.Enabled.HasValue
        && update.PageId is not null
        && update.PageAccessToken is not null;

    private static bool IsValidInstagram(GraphChannelOptions value) =>
        !value.Enabled
        || (!string.IsNullOrWhiteSpace(value.PageId)
            && value.PageId.All(char.IsAsciiDigit)
            && !string.IsNullOrWhiteSpace(value.PageAccessToken));

    private static GraphChannelOptions? Decrypt(IEncryptor encryptor, string encrypted)
    {
        if (string.IsNullOrEmpty(encrypted))
            return null;

        try
        {
            return JsonSerializer.Deserialize<GraphChannelOptions>(encryptor.Decrypt(encrypted), JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static SocialCredentialDto ToDto(string provider, GraphChannelOptions value, DateTimeOffset? updatedAt) =>
        new(
            provider,
            value.Enabled,
            value.Endpoint,
            value.PageId,
            value.OaId,
            !string.IsNullOrWhiteSpace(value.PageAccessToken),
            !string.IsNullOrWhiteSpace(value.OaAccessToken),
            updatedAt);

    // null = keep current; empty/whitespace = clear; value = replace.
    private static string MergeText(string current, string? update) =>
        update is null ? current : (string.IsNullOrWhiteSpace(update) ? string.Empty : update.Trim());

    private static bool TooLong(string? value) => value is { Length: > MaxFieldChars };

    private static bool IsValidHttpsUrl(string url) =>
        url.Length <= MaxEndpointChars
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
        Results.Json(
            new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier },
            statusCode: statusCode);
}
