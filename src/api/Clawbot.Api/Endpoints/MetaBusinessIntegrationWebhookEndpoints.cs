using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Integrations.Meta;
using Clawbot.Infrastructure.Jobs;
using Clawbot.Infrastructure.Persistence;
using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record MetaBusinessIntegrationChange(string Field, string BusinessManagerId);

public static class MetaBusinessIntegrationWebhookEndpoints
{
    private const int MaxPayloadBytes = 1024 * 1024;
    private static readonly HashSet<string> SupportedFields =
    [
        MetaBusinessIntegrationWebhookJob.InstallField,
        MetaBusinessIntegrationWebhookJob.UpdateField,
        MetaBusinessIntegrationWebhookJob.UninstallField,
    ];

    public static IEndpointRouteBuilder MapMetaBusinessIntegrationWebhooks(this IEndpointRouteBuilder app)
    {
        const string path = "/webhooks/meta/business-integration";
        app.MapGet(path, VerifyAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.WebhookPolicy);
        app.MapPost(path, ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(RateLimitingExtensions.WebhookPolicy);
        return app;
    }

    private static async Task<IResult> VerifyAsync(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IMetaGraphConfigurationResolver configurations,
        CancellationToken ct)
    {
        var candidates = await configurations.GetWebhookCandidatesAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);

        return string.Equals(mode, "subscribe", StringComparison.Ordinal)
               && !string.IsNullOrEmpty(challenge)
               && candidates.Any(candidate => FixedTimeEquals(verifyToken, candidate.Options.WebhookVerifyToken))
            ? Results.Text(challenge, "text/plain", Encoding.UTF8)
            : Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request,
        AppDbContext db,
        IBackgroundJobClient jobs,
        IMetaGraphConfigurationResolver configurations,
        CancellationToken ct)
    {
        var candidates = await configurations.GetWebhookCandidatesAsync(ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        if (request.ContentLength is > MaxPayloadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        await using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, ct).ConfigureAwait(false);
        if (body.Length > MaxPayloadBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        var payload = body.ToArray();

        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        var signedCandidates = candidates
            .Where(candidate => IsValidSignature(payload, signature, candidate.Options.AppSecret))
            .ToList();
        if (signedCandidates.Count == 0)
            return Results.StatusCode(StatusCodes.Status401Unauthorized);

        IReadOnlyList<MetaGraphConfigurationCandidate> matchedConfigurations;
        IReadOnlyList<MetaBusinessIntegrationChange> changes;
        try
        {
            var applicationIds = ParseApplicationIds(payload);
            matchedConfigurations = MatchConfigurations(signedCandidates, applicationIds);
            if (matchedConfigurations.Count == 0)
                return Results.StatusCode(StatusCodes.Status401Unauthorized);
            var matchedAppId = matchedConfigurations[0].Options.AppId;
            changes = ParseChanges(payload, matchedAppId);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { code = "meta.webhook_payload_invalid" });
        }

        if (changes.Count == 0)
            return Results.Ok(new { received = 0 });

        var businessIds = changes.Select(x => x.BusinessManagerId).Distinct(StringComparer.Ordinal).ToList();
        var connectionQuery = db.MetaConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => businessIds.Contains(x.ClientBusinessId));
        var matchedTenantIds = matchedConfigurations
            .Where(x => x.TenantId.HasValue)
            .Select(x => x.TenantId!.Value)
            .Distinct()
            .ToList();
        if (!matchedConfigurations.Any(x => x.TenantId is null))
        {
            connectionQuery = connectionQuery.Where(x => matchedTenantIds.Contains(x.TenantId));
        }
        else
        {
            var tenantOverrides = await db.SocialCredentials
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.Provider == MetaGraphConfigurationStore.Provider
                    && x.PageId == null
                    && x.IsActive
                    && x.DeletedAt == null)
                .Select(x => x.TenantId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false);
            connectionQuery = connectionQuery.Where(x =>
                matchedTenantIds.Contains(x.TenantId) || !tenantOverrides.Contains(x.TenantId));
        }
        var connections = await connectionQuery
            .Select(x => new { x.TenantId, x.ClientBusinessId })
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var tenantsByBusiness = connections
            .GroupBy(x => x.ClientBusinessId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Select(row => row.TenantId).Distinct().ToList(), StringComparer.Ordinal);

        var actions = new Dictionary<Guid, string>();
        foreach (var change in changes)
        {
            if (!tenantsByBusiness.TryGetValue(change.BusinessManagerId, out var tenantIds))
                continue;
            foreach (var tenantId in tenantIds)
            {
                if (!actions.TryGetValue(tenantId, out var current)
                    || string.Equals(change.Field, MetaBusinessIntegrationWebhookJob.UninstallField, StringComparison.Ordinal)
                    || !string.Equals(current, MetaBusinessIntegrationWebhookJob.UninstallField, StringComparison.Ordinal))
                {
                    actions[tenantId] = change.Field;
                }
            }
        }

        foreach (var (tenantId, field) in actions)
        {
            jobs.Enqueue<MetaBusinessIntegrationWebhookJob>(job =>
                job.RunAsync(tenantId, field, CancellationToken.None));
        }

        return Results.Ok(new { received = actions.Count });
    }

    internal static bool IsValidSignature(byte[] payload, string? signatureHeader, string appSecret)
    {
        if (payload.Length == 0
            || string.IsNullOrWhiteSpace(signatureHeader)
            || string.IsNullOrWhiteSpace(appSecret)
            || !signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            return false;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(signatureHeader[7..]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var expected = hmac.ComputeHash(payload);
        return provided.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    internal static IReadOnlyList<MetaBusinessIntegrationChange> ParseChanges(byte[] payload, string expectedAppId)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !string.Equals(ScalarString(root, "object"), "application", StringComparison.Ordinal)
            || !root.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            return [];

        var changes = new List<MetaBusinessIntegrationChange>();
        foreach (var entry in entries.EnumerateArray())
        {
            if (!string.Equals(ScalarString(entry, "id"), expectedAppId, StringComparison.Ordinal)
                || !entry.TryGetProperty("changes", out var entryChanges)
                || entryChanges.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var change in entryChanges.EnumerateArray())
            {
                var field = ScalarString(change, "field");
                if (field is null
                    || !SupportedFields.Contains(field)
                    || !change.TryGetProperty("value", out var value))
                    continue;
                var businessId = ScalarString(value, "business_manager_id");
                if (!string.IsNullOrWhiteSpace(businessId))
                    changes.Add(new MetaBusinessIntegrationChange(field, businessId));
            }
        }

        return changes
            .DistinctBy(x => (x.Field, x.BusinessManagerId))
            .ToList();
    }

    internal static IReadOnlySet<string> ParseApplicationIds(byte[] payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !string.Equals(ScalarString(root, "object"), "application", StringComparison.Ordinal)
            || !root.TryGetProperty("entry", out var entries)
            || entries.ValueKind != JsonValueKind.Array)
            return new HashSet<string>(StringComparer.Ordinal);

        return entries.EnumerateArray()
            .Select(entry => ScalarString(entry, "id"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static IReadOnlyList<MetaGraphConfigurationCandidate> MatchConfigurations(
        IReadOnlyList<MetaGraphConfigurationCandidate> candidates,
        IReadOnlySet<string> applicationIds)
    {
        var appId = applicationIds.FirstOrDefault(id =>
            candidates.Any(candidate => string.Equals(candidate.Options.AppId, id, StringComparison.Ordinal)));
        return string.IsNullOrWhiteSpace(appId)
            ? []
            : candidates
                .Where(candidate => string.Equals(candidate.Options.AppId, appId, StringComparison.Ordinal))
                .ToList();
    }

    private static string? ScalarString(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (string.IsNullOrEmpty(left))
            return false;
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
