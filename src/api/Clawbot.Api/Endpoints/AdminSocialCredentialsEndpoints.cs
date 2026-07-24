using Clawbot.Api.Auth;
using Clawbot.Api.Middleware;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Content.Publishing;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record SocialCredentialDto(
    string Provider,
    string ResolutionState,
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
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        var tenant = tenants.Require();
        var rows = await db.SocialCredentials.AsNoTracking()
            .Where(c => c.TenantId == tenant.TenantId
                && Providers.Contains(c.Provider)
                && c.DeletedAt == null
                && c.PageId == null)
            .ToListAsync(ct).ConfigureAwait(false);

        var items = new List<SocialCredentialDto>(Providers.Length);
        foreach (var provider in Providers)
        {
            var row = rows.Find(r => string.Equals(r.Provider, provider, StringComparison.OrdinalIgnoreCase));
            if (provider == "instagram")
            {
                if (row is null)
                {
                    items.Add(ToDto(provider, "absent", new GraphChannelOptions(), updatedAt: null));
                    continue;
                }

                if (!row.IsActive)
                {
                    items.Add(ToDto(provider, "invalid", new GraphChannelOptions(), row.UpdatedAt));
                    continue;
                }

                var decoded = SocialCredentialEnvelopeCodec.Decode(
                    encryptor,
                    tenant.TenantId,
                    provider,
                    row.PageId,
                    row.CredentialsEncrypted);
                if (decoded.Status == SocialCredentialEnvelopeStatus.Invalid || decoded.Options is null)
                {
                    items.Add(ToDto(provider, "invalid", new GraphChannelOptions(), row.UpdatedAt));
                    continue;
                }

                if (!IsValidInstagram(decoded.Options))
                {
                    items.Add(ToDto(provider, "invalid", new GraphChannelOptions(), row.UpdatedAt));
                    continue;
                }

                items.Add(ToDto(provider, InstagramResolutionState(decoded.Options), decoded.Options, row.UpdatedAt));
                continue;
            }

            // Zalo: global OA configuration is never reported as tenant state, so an absent row stays empty.
            var stored = row is null || !row.IsActive
                ? null
                : DecodeStored(encryptor, tenant.TenantId, provider, row);
            var resolutionState = row is null ? "absent" : stored is null ? "invalid" : "resolved";
            items.Add(ToDto(provider, resolutionState, stored ?? new GraphChannelOptions(), row?.UpdatedAt));
        }

        return Results.Ok(new { items });
    }

    private static async Task<IResult> UpdateAsync(
        string provider,
        UpdateSocialCredentialRequest body,
        AppDbContext db,
        IEncryptor encryptor,
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
        if (normalized == "instagram"
            && body.PageId is { } instagramUserId
            && instagramUserId.Trim().Length > ContentSchedule.MaxProviderTargetIdLength)
        {
            return Error(
                http,
                StatusCodes.Status400BadRequest,
                "admin.instagram_user_id_too_long",
                $"Instagram user ID must be at most {ContentSchedule.MaxProviderTargetIdLength} characters.");
        }
        if (normalized == "zalo"
            && body.Endpoint is not null
            && !string.IsNullOrWhiteSpace(body.Endpoint)
            && !IsValidHttpsUrl(body.Endpoint))
        {
            return Error(http, StatusCodes.Status400BadRequest, "admin.social_endpoint_invalid", "endpoint must be an absolute https URL.");
        }

        var row = await db.SocialCredentials.AsNoTracking()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenant.TenantId
                    && c.Provider == normalized
                    && c.DeletedAt == null
                    && c.PageId == null,
                ct)
            .ConfigureAwait(false);
        GraphChannelOptions? stored;
        if (row is null || !row.IsActive)
        {
            stored = null;
        }
        else if (normalized == "zalo" && CanReplaceZaloWithoutDecrypting(body))
        {
            stored = null;
        }
        else
        {
            stored = DecodeStored(encryptor, tenant.TenantId, normalized, row);
        }

        if (normalized == "instagram"
            && row is not null
            && stored is null
            && !CanRepairInvalidInstagram(body))
        {
            return Error(http, StatusCodes.Status400BadRequest, "admin.instagram_credentials_invalid", "Disable the standalone override or replace all Instagram credential fields.");
        }
        if (normalized == "zalo"
            && row is not null
            && stored is null
            && !CanRepairInvalidZalo(body))
        {
            return Error(http, StatusCodes.Status400BadRequest, "admin.zalo_credentials_invalid", "Disable and clear the Zalo override or replace all Zalo credential fields.");
        }

        // Merge only over the tenant's own stored credentials. Global publisher configuration must never
        // become a tenant credential, otherwise any admin could adopt the shared OA token by omitting fields.
        var current = stored ?? new GraphChannelOptions();
        var merged = normalized == "instagram"
            ? MergeInstagram(current, body)
            : MergeZalo(current, body);
        if (normalized == "instagram" && !IsValidInstagram(merged))
            return Error(http, StatusCodes.Status400BadRequest, "admin.instagram_credentials_invalid", "Enabled Instagram credentials require a numeric user ID and access token.");
        if (normalized == "zalo" && !IsValidZalo(merged))
            return Error(http, StatusCodes.Status400BadRequest, "admin.zalo_credentials_invalid", "Enabled Zalo credentials require an HTTPS endpoint, OA ID, and access token.");

        var encrypted = SocialCredentialEnvelopeCodec.Encode(
            encryptor,
            tenant.TenantId,
            normalized,
            row?.PageId,
            merged);
        var now = clock.UtcNow;
        if (row is null)
        {
            db.SocialCredentials.Add(SocialCredential.Create(tenant.TenantId, normalized, encrypted, now));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        else
        {
            var updatedRows = await CompareAndSwapExistingAsync(
                db,
                tenant.TenantId,
                normalized,
                row,
                encrypted,
                now,
                ct).ConfigureAwait(false);
            if (updatedRows != 1)
            {
                return Error(
                    http,
                    StatusCodes.Status409Conflict,
                    "admin.social_credential_conflict",
                    "Social credentials changed concurrently. Reload and try again.");
            }
        }

        var resolutionState = normalized == "instagram"
            ? InstagramResolutionState(merged)
            : "resolved";
        return Results.Ok(ToDto(normalized, resolutionState, merged, now));
    }

    private static Task<int> CompareAndSwapExistingAsync(
        AppDbContext db,
        Guid tenantId,
        string provider,
        SocialCredential observed,
        string encrypted,
        DateTimeOffset updatedAt,
        CancellationToken ct) =>
        db.SocialCredentials.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenantId
                && row.Id == observed.Id
                && row.Provider == provider
                && row.PageId == observed.PageId
                && row.IsActive == observed.IsActive
                && row.DeletedAt == observed.DeletedAt
                && row.UpdatedAt == observed.UpdatedAt
                && row.CredentialsEncrypted == observed.CredentialsEncrypted)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(row => row.CredentialsEncrypted, encrypted)
                    .SetProperty(row => row.IsActive, true)
                    .SetProperty(row => row.UpdatedAt, updatedAt),
                ct);

    // Stored credentials are only usable when the envelope is bound to this tenant, provider, and row target.
    private static GraphChannelOptions? DecodeStored(
        IEncryptor encryptor,
        Guid tenantId,
        string provider,
        SocialCredential row)
    {
        var decoded = SocialCredentialEnvelopeCodec.Decode(
            encryptor,
            tenantId,
            provider,
            row.PageId,
            row.CredentialsEncrypted);
        return decoded.Status == SocialCredentialEnvelopeStatus.Resolved ? decoded.Options : null;
    }

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
        update.Endpoint is not null
        || update.OaId is not null
        || update.OaAccessToken is not null;

    private static bool CanRepairInvalidInstagram(UpdateSocialCredentialRequest update) =>
        update.Enabled == false || IsFullInstagramReplacement(update);

    private static bool IsFullInstagramReplacement(UpdateSocialCredentialRequest update) =>
        update.Enabled.HasValue
        && update.PageId is not null
        && update.PageAccessToken is not null;

    private static bool CanRepairInvalidZalo(UpdateSocialCredentialRequest update) =>
        update.Enabled == false || IsFullZaloReplacement(update);

    private static bool CanReplaceZaloWithoutDecrypting(UpdateSocialCredentialRequest update) =>
        IsFullZaloReplacement(update) || IsExplicitZaloClearAndDisable(update);

    private static bool IsFullZaloReplacement(UpdateSocialCredentialRequest update) =>
        update.Enabled.HasValue
        && update.Endpoint is not null
        && update.OaId is not null
        && update.OaAccessToken is not null;

    private static bool IsExplicitZaloClearAndDisable(UpdateSocialCredentialRequest update) =>
        update.Enabled == false
        && IsExplicitClear(update.Endpoint)
        && IsExplicitClear(update.OaId)
        && IsExplicitClear(update.OaAccessToken);

    private static bool IsExplicitClear(string? value) =>
        value is not null && string.IsNullOrWhiteSpace(value);

    private static bool IsValidInstagram(GraphChannelOptions value) =>
        !value.Enabled
        || (!string.IsNullOrWhiteSpace(value.PageId)
            && value.PageId.All(char.IsAsciiDigit)
            && !string.IsNullOrWhiteSpace(value.PageAccessToken));

    private static bool IsValidZalo(GraphChannelOptions value) =>
        !value.Enabled
        || (!string.IsNullOrWhiteSpace(value.Endpoint)
            && IsValidHttpsUrl(value.Endpoint)
            && !string.IsNullOrWhiteSpace(value.OaId)
            && !string.IsNullOrWhiteSpace(value.OaAccessToken));

    private static string InstagramResolutionState(GraphChannelOptions value) =>
        !value.Enabled ? "disabled" : IsValidInstagram(value) ? "resolved" : "invalid";

    private static SocialCredentialDto ToDto(
        string provider,
        string resolutionState,
        GraphChannelOptions value,
        DateTimeOffset? updatedAt) =>
        new(
            provider,
            resolutionState,
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
