using System.Text.Json;
using Clawbot.Api.Auth;
using Clawbot.Api.Contracts.Content;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

// Per-tenant trend-scan configuration: source toggles/keys stored as one encrypted
// social_credentials row (provider = "trends"), plus the auto-scan schedule stored as an
// AgentSchedules row whose GoalTemplate is the "[trend-scan]" marker. AgentScheduleWorker
// executes that schedule via the direct scan path (no LLM orchestration).
public static class ContentTrendSettingsEndpoints
{
    private const string ScheduleName = "Quét xu hướng tự động";
    private const string ScheduleTimezoneId = "Asia/Ho_Chi_Minh";
    private const string DefaultGeo = "VN";
    private const string CadenceOff = "off";
    private const int MaxApiKeyChars = 256;
    private const int MaxUrlChars = 2048;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static void Map(RouteGroupBuilder grp)
    {
        ArgumentNullException.ThrowIfNull(grp);
        grp.MapGet("/trends/settings", GetSettingsAsync).RequirePermission("content:read");
        grp.MapPut("/trends/settings", UpdateSettingsAsync).RequirePermission("content:write");
    }

    private static async Task<IResult> GetSettingsAsync(
        AppDbContext db,
        IEncryptor encryptor,
        ITenantAccessor tenants,
        CancellationToken ct)
    {
        _ = tenants.Require();
        var settings = await LoadSettingsAsync(db, encryptor, ct).ConfigureAwait(false);
        var schedule = await LoadScheduleAsync(db, ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(settings, schedule));
    }

    private static async Task<IResult> UpdateSettingsAsync(
        UpdateTrendSettingsRequest body,
        AppDbContext db,
        IEncryptor encryptor,
        ITenantAccessor tenants,
        IClock clock,
        HttpContext http,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var tenant = tenants.Require();

        if (body.Geo is not null && !IsValidGeo(body.Geo))
            return Error(http, StatusCodes.Status400BadRequest, "content.trend_settings_geo_invalid", "geo must be a 2-letter country code.");
        if (body.YouTube?.ApiKey is { Length: > MaxApiKeyChars })
            return Error(http, StatusCodes.Status400BadRequest, "content.trend_settings_key_invalid", $"apiKey must be at most {MaxApiKeyChars} characters.");
        if (!string.IsNullOrWhiteSpace(body.TikTok?.Url) && !IsValidPublicHttpsUrl(body.TikTok!.Url!))
            return Error(http, StatusCodes.Status400BadRequest, "content.trend_settings_url_invalid", "url must be an absolute public https URL.");
        if (!IsValidCadence(body.ScheduleCadence))
            return Error(http, StatusCodes.Status400BadRequest, "content.trend_settings_cadence_invalid", "scheduleCadence must be off, daily, weekly, or monthly.");

        var current = await LoadSettingsAsync(db, encryptor, ct).ConfigureAwait(false) ?? new ContentTrendSettings();
        var merged = Merge(current, body);

        var encrypted = encryptor.Encrypt(JsonSerializer.Serialize(merged, JsonOpts));
        var now = clock.UtcNow;
        var row = await db.SocialCredentials
            .FirstOrDefaultAsync(c => c.Provider == ContentTrendSettings.CredentialProvider && c.DeletedAt == null, ct)
            .ConfigureAwait(false);
        if (row is null)
        {
            db.SocialCredentials.Add(SocialCredential.Create(tenant.TenantId, ContentTrendSettings.CredentialProvider, encrypted, now));
        }
        else
        {
            row.UpdateCredentials(encrypted, now);
            row.Activate(now);
        }

        var schedule = await UpsertScheduleAsync(db, tenant.TenantId, body.ScheduleCadence, clock, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Results.Ok(ToResponse(merged, schedule));
    }

    private static async Task<ContentTrendSettings?> LoadSettingsAsync(AppDbContext db, IEncryptor encryptor, CancellationToken ct)
    {
        var encrypted = await db.SocialCredentials.AsNoTracking()
            .Where(c => c.Provider == ContentTrendSettings.CredentialProvider && c.DeletedAt == null && c.IsActive)
            .Select(c => c.CredentialsEncrypted)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(encrypted))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ContentTrendSettings>(encryptor.Decrypt(encrypted), JsonOpts);
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

    private static async Task<AgentSchedule?> LoadScheduleAsync(AppDbContext db, CancellationToken ct) =>
        await db.AgentSchedules.AsNoTracking()
            .Where(s => s.GoalTemplate == ContentTrendSettings.ScheduleGoalMarker && s.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    private static async Task<AgentSchedule?> UpsertScheduleAsync(
        AppDbContext db,
        Guid tenantId,
        string? cadence,
        IClock clock,
        CancellationToken ct)
    {
        var existing = await db.AgentSchedules
            .Where(s => s.GoalTemplate == ContentTrendSettings.ScheduleGoalMarker && s.DeletedAt == null)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (cadence is null)
            return existing;

        var now = clock.UtcNow;
        if (string.Equals(cadence.Trim(), CadenceOff, StringComparison.OrdinalIgnoreCase))
        {
            existing?.Pause(now);
            return existing;
        }

        var normalized = cadence.Trim().ToLowerInvariant();
        if (existing is null)
        {
            // NextRunAt = now: the worker picks it up within a minute, so enabling the schedule
            // also triggers the first scan immediately.
            var schedule = AgentSchedule.Create(
                tenantId,
                ScheduleName,
                ContentTrendSettings.ScheduleGoalMarker,
                normalized,
                cronExpression: null,
                ScheduleTimezoneId,
                nextRunAt: now,
                requiresApproval: false,
                createdAt: now);
            db.AgentSchedules.Add(schedule);
            return schedule;
        }

        existing.UpdateSchedule(
            existing.Name,
            existing.GoalTemplate,
            normalized,
            existing.CronExpression,
            existing.TimezoneId,
            nextRunAt: now,
            existing.RequiresApproval,
            existing.OverlapPolicy,
            existing.MisfirePolicy,
            existing.ApprovalPolicyJson,
            now);
        existing.Activate(now);
        return existing;
    }

    private static ContentTrendSettings Merge(ContentTrendSettings current, UpdateTrendSettingsRequest body) =>
        new(
            body.Geo is null ? current.Geo : body.Geo.Trim().ToUpperInvariant(),
            MergeSource(current.Google, body.Google, allowKey: false, allowUrl: false),
            MergeSource(current.YouTube, body.YouTube, allowKey: true, allowUrl: false),
            MergeSource(current.TikTok, body.TikTok, allowKey: false, allowUrl: true));

    private static ContentTrendSourceSetting? MergeSource(
        ContentTrendSourceSetting? current,
        UpdateTrendSourceSetting? update,
        bool allowKey,
        bool allowUrl)
    {
        if (update is null)
            return current;

        var apiKey = !allowKey ? null : update.ApiKey is null ? current?.ApiKey : NullIfEmpty(update.ApiKey);
        var url = !allowUrl ? null : update.Url is null ? current?.Url : NullIfEmpty(update.Url);
        return new ContentTrendSourceSetting(update.Enabled ?? current?.Enabled, apiKey, url);
    }

    private static TrendSettingsResponse ToResponse(ContentTrendSettings? settings, AgentSchedule? schedule)
    {
        var scheduleDto = schedule is null || !schedule.IsActive
            ? new TrendScheduleDto(CadenceOff, null, schedule?.LastRunAt)
            : new TrendScheduleDto(schedule.Cadence, schedule.NextRunAt, schedule.LastRunAt);

        return new TrendSettingsResponse(
            string.IsNullOrWhiteSpace(settings?.Geo) ? DefaultGeo : settings!.Geo!,
            new TrendSourceSettingDto(settings?.Google?.Enabled ?? true, HasApiKey: false, Url: null),
            new TrendSourceSettingDto(settings?.YouTube?.Enabled ?? true, !string.IsNullOrWhiteSpace(settings?.YouTube?.ApiKey), Url: null),
            new TrendSourceSettingDto(settings?.TikTok?.Enabled ?? false, HasApiKey: false, settings?.TikTok?.Url),
            scheduleDto);
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidGeo(string geo)
    {
        var trimmed = geo.Trim();
        return trimmed.Length == 2 && trimmed.All(char.IsAsciiLetter);
    }

    // Shape-level check only; DNS-level SSRF blocking happens at fetch time (LlmBaseUrlGuard in the sources).
    private static bool IsValidPublicHttpsUrl(string url) =>
        url.Length <= MaxUrlChars
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsValidCadence(string? cadence)
    {
        if (cadence is null)
            return true;

        var normalized = cadence.Trim().ToLowerInvariant();
        return normalized == CadenceOff || ContentTrendSettings.AllowedScheduleCadences.Contains(normalized);
    }

    private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
        Results.Json(
            new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier },
            statusCode: statusCode);
}
