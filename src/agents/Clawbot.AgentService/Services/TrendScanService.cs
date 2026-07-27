using System.Security.Cryptography;
using System.Text.Json;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using Clawbot.SharedKernel.Security;
using Clawbot.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using CoreResearch = Clawbot.Agents.Core.Research;

namespace Clawbot.AgentService.Services;

public interface ITenantTrendScanner
{
    Task<IReadOnlyList<CoreResearch.ScoredTrend>> ScanAndPersistAsync(Guid tenantId, string weekOf, CancellationToken ct = default);
}

// One place for the full tenant trend scan: resolve per-tenant source settings (encrypted
// social_credentials row, provider="trends"), scan, and upsert the week's trend briefs.
// Used by the gRPC WeeklyTrends endpoint (manual scan + Hangfire weekly job) and by
// AgentScheduleRunner for "[trend-scan]" schedules.
public sealed class TrendScanService(
    AppDbContext db,
    IEncryptor encryptor,
    CoreResearch.IResearchAgent agent,
    IClock clock) : ITenantTrendScanner
{
    private const string DefaultGeo = "VN";
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly AppDbContext _db = db;
    private readonly IEncryptor _encryptor = encryptor;
    private readonly CoreResearch.IResearchAgent _agent = agent;
    private readonly IClock _clock = clock;

    public async Task<IReadOnlyList<CoreResearch.ScoredTrend>> ScanAndPersistAsync(
        Guid tenantId,
        string weekOf,
        CancellationToken ct = default)
    {
        var settings = await LoadSettingsAsync(tenantId, ct).ConfigureAwait(false);
        var keywords = await LoadKeywordsAsync(tenantId, ct).ConfigureAwait(false);
        var geo = string.IsNullOrWhiteSpace(settings?.Geo) ? DefaultGeo : settings!.Geo!.Trim().ToUpperInvariant();
        var trends = await _agent.ScanAsync(
            new CoreResearch.ResearchScanRequest(tenantId, geo, keywords, ToOverrides(settings)),
            ct).ConfigureAwait(false);

        await UpsertTrendBriefsAsync(tenantId, weekOf, trends, ct).ConfigureAwait(false);
        // C2: đánh thức các lịch event-trigger "khi quét xu hướng xong" (vd. tự soạn content từ trend mới).
        await Clawbot.Infrastructure.Agents.ScheduleEventDispatcher.FireAsync(
            _db, tenantId, Clawbot.SharedKernel.Orchestration.ScheduleEventKeys.TrendsScanned, _clock.UtcNow, ct).ConfigureAwait(false);
        return trends;
    }

    private async Task<ContentTrendSettings?> LoadSettingsAsync(Guid tenantId, CancellationToken ct)
    {
        var encrypted = await _db.SocialCredentials.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.TenantId == tenantId
                && c.Provider == ContentTrendSettings.CredentialProvider
                && c.DeletedAt == null
                && c.IsActive)
            .Select(c => c.CredentialsEncrypted)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(encrypted))
            return null;

        try
        {
            return JsonSerializer.Deserialize<ContentTrendSettings>(_encryptor.Decrypt(encrypted), JsonOpts);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static CoreResearch.TrendOverrides ToOverrides(ContentTrendSettings? settings) =>
        new(
            ToOverride(settings?.Google),
            new CoreResearch.TrendSourceOverride(Enabled: false),
            new CoreResearch.TrendSourceOverride(Enabled: false));

    private static CoreResearch.TrendSourceOverride? ToOverride(ContentTrendSourceSetting? setting) =>
        setting is null ? null : new CoreResearch.TrendSourceOverride(setting.Enabled, setting.ApiKey, setting.Url);

    private async Task<IReadOnlyList<string>> LoadKeywordsAsync(Guid tenantId, CancellationToken ct)
    {
        var modules = await _db.KbModules.IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.DeletedAt == null && m.Status == "active")
            .Select(m => new { m.Code, m.Name, m.Description })
            .ToListAsync(ct).ConfigureAwait(false);

        return modules
            .SelectMany(m => ExtractKeywords(m.Code, m.Name, m.Description))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task UpsertTrendBriefsAsync(
        Guid tenantId,
        string weekOf,
        IReadOnlyList<CoreResearch.ScoredTrend> trends,
        CancellationToken ct)
    {
        if (trends.Count == 0)
            return;

        var weekPrefix = $"[trend:{weekOf}]";
        var existing = await _db.ContentBriefs.IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId && b.Brief.StartsWith(weekPrefix))
            .ToListAsync(ct).ConfigureAwait(false);
        var existingByKey = existing
            .Select(b => new { Brief = b, Parsed = ParseTrendBrief(b.Brief) })
            .Where(x => x.Parsed is not null)
            .GroupBy(x => TrendKey(x.Brief.Platform, x.Parsed!.Topic), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.Brief.UpdatedAt).First().Brief,
                StringComparer.OrdinalIgnoreCase);

        var now = _clock.UtcNow;
        foreach (var trend in trends)
        {
            var brief = new ContentTrendBrief(
                weekOf,
                trend.Topic,
                trend.Source,
                trend.Metric,
                trend.RelevanceScore,
                trend.ContentIdeas);
            var body = ContentTrendBriefFormatter.Format(brief);
            var key = TrendKey(trend.Source, trend.Topic);

            if (existingByKey.TryGetValue(key, out var current))
            {
                current.Update(trend.Source, body, now);
                current.MarkStatus("pending", now);
                continue;
            }

            _db.ContentBriefs.Add(ContentBrief.Create(tenantId, trend.Source, body, createdBy: null, createdAt: now));
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static ContentTrendBrief? ParseTrendBrief(string brief) =>
        ContentTrendBriefFormatter.TryParse(brief, out var parsed) ? parsed : null;

    private static string TrendKey(string source, string topic) =>
        $"{source.Trim().ToLowerInvariant()}::{topic.Trim().ToLowerInvariant()}";

    private static IEnumerable<string> ExtractKeywords(params string?[] fields)
    {
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
                continue;

            var trimmed = field.Trim();
            yield return trimmed;

            foreach (var token in trimmed.Split(
                [' ', ',', ';', '.', ':', '/', '\\', '|', '-', '_', '(', ')', '[', ']'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token.Length >= 2)
                    yield return token;
            }
        }
    }
}
