using System.Globalization;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Leads;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

/// <summary>
/// Orchestration adapter for the lead agent.
/// Operations: list/find_cold, score, create, batch_score.
/// </summary>
public sealed class LeadOrchestrationAdapter(
    LeadAgentRunner runner,
    LeadBatchRescorer batchRescorer,
    AppDbContext db) : AgentAdapterBase("lead-agent")
{
    private const int DefaultListLimit = 20;
    private const int MaxListLimit = 100;

    private readonly LeadAgentRunner _runner = runner;
    private readonly LeadBatchRescorer _batchRescorer = batchRescorer;
    private readonly AppDbContext _db = db;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = (AgentTaskInput.OptionalString(input, "operation")
            ?? InferOperation(input, task.Description)).ToLowerInvariant();

        if (operation is "list" or "find_cold" or "query" or "find")
            return await ListLeadsAsync(input, task.Description, ct).ConfigureAwait(false);

        if (operation is "create")
        {
            var result = await _runner.CreateWithSkillsAsync(new LeadCreateInput(
                AgentTaskInput.RequiredGuid(input, "tenant_id"),
                AgentTaskInput.RequiredGuid(input, "contact_id"),
                AgentTaskInput.RequiredString(input, "source_platform"),
                AgentTaskInput.OptionalString(input, "display_name"),
                AgentTaskInput.OptionalString(input, "phone"),
                AgentTaskInput.OptionalString(input, "email"),
                AgentTaskInput.OptionalString(input, "locale"),
                AgentTaskInput.OptionalString(input, "country"),
                AgentTaskInput.OptionalString(input, "note")), ct).ConfigureAwait(false);
            return Json(result);
        }

        if (operation is "batch_score" or "rescore" or "score_all" or "prioritize")
        {
            var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");
            var topN = OptionalInt(input, "topN")
                ?? OptionalInt(input, "top_n")
                ?? 5;
            if (topN > 50) topN = 5;
            var batch = await _batchRescorer.RescoreTenantAsync(tenantId, topN, ct).ConfigureAwait(false);
            return Json(batch);
        }

        // Single-lead score: requires lead_id. Do not silently batch-rescore list goals.
        if (AgentTaskInput.OptionalGuid(input, "lead_id") is null)
            throw new ArgumentException(
                "lead_id required for score. Use operation=list to query cold/warm leads, or operation=batch_score to rescore.");

        var features = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (AgentTaskInput.OptionalString(input, "event_code") is { } eventCode)
            features["event_code"] = eventCode;
        if (AgentTaskInput.OptionalString(input, "platform") is { } platform)
            features["platform"] = platform;

        var score = await _runner.ScoreAsync(
            AgentTaskInput.RequiredGuid(input, "tenant_id"),
            AgentTaskInput.RequiredGuid(input, "lead_id"),
            features, ct).ConfigureAwait(false);
        return Json(score);
    }

    private async Task<string> ListLeadsAsync(
        IReadOnlyDictionary<string, string> input,
        string description,
        CancellationToken ct)
    {
        var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");
        var limit = OptionalInt(input, "topN")
            ?? OptionalInt(input, "top_n")
            ?? OptionalInt(input, "limit")
            ?? DefaultListLimit;
        if (limit < 1) limit = DefaultListLimit;
        if (limit > MaxListLimit) limit = MaxListLimit;

        var stageFilter = NormalizeStageFilter(
            AgentTaskInput.OptionalString(input, "stage")
            ?? InferStageFromDescription(description));

        var inactiveDays = OptionalInt(input, "inactive_days")
            ?? OptionalInt(input, "inactiveDays");
        DateTimeOffset? inactiveBefore = inactiveDays is > 0
            ? DateTimeOffset.UtcNow.AddDays(-inactiveDays.Value)
            : null;

        var query = _db.Leads.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.DeletedAt == null);

        if (stageFilter is { Count: > 0 })
            query = query.Where(l => stageFilter.Contains(l.Stage));

        if (inactiveBefore is { } before)
            query = query.Where(l => (l.LastActivityAt ?? l.CreatedAt) < before);

        // SQLite test provider cannot ORDER BY DateTimeOffset; order by Score in SQL then stage/recency in-memory.
        // Cap over-fetch so cold/warm still surface when mixed with hot/customer.
        var raw = await query
            .OrderByDescending(l => l.Score)
            .Take(Math.Min(MaxListLimit, Math.Max(limit * 5, 50)))
            .Select(l => new
            {
                l.Id,
                l.ContactId,
                l.OwnerUserId,
                l.Score,
                l.Stage,
                l.SourcePlatform,
                l.LastActivityAt,
                l.CreatedAt,
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var leads = raw
            .OrderBy(l => l.Stage == "cold" ? 0 : l.Stage == "warm" ? 1 : l.Stage == "hot" ? 2 : 3)
            .ThenBy(l => l.LastActivityAt ?? l.CreatedAt)
            .ThenByDescending(l => l.Score)
            .Take(limit)
            .ToList();

        var contactIds = leads.Where(l => l.ContactId is not null).Select(l => l.ContactId!.Value).Distinct().ToList();
        var names = contactIds.Count == 0
            ? new Dictionary<Guid, string?>()
            : await _db.Contacts.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(c => c.TenantId == tenantId && contactIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => (string?)c.DisplayName, ct)
                .ConfigureAwait(false);

        var items = leads.Select(l => new
        {
            lead_id = l.Id,
            contact_id = l.ContactId,
            owner_user_id = l.OwnerUserId,
            score = l.Score,
            stage = l.Stage,
            source_platform = l.SourcePlatform,
            last_activity_at = l.LastActivityAt,
            created_at = l.CreatedAt,
            contact_name = l.ContactId is { } cid && names.TryGetValue(cid, out var n) ? n : null,
        }).ToList();

        var leadIds = items.Select(i => i.lead_id.ToString("D")).ToArray();

        return Json(new
        {
            operation = "list",
            total = items.Count,
            stages = stageFilter,
            inactive_days = inactiveDays,
            lead_ids = leadIds,
            items,
        });
    }

    private static List<string>? NormalizeStageFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ["cold", "warm", "hot"];

        var stages = raw.Split([',', '|', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .Where(s => s is "cold" or "warm" or "hot" or "customer" or "lost")
            .Distinct()
            .ToList();
        return stages.Count == 0 ? ["cold", "warm", "hot"] : stages;
    }

    private static string? InferStageFromDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var d = description.ToLowerInvariant();
        if (d.Contains("cold", StringComparison.Ordinal) || d.Contains("lạnh", StringComparison.Ordinal) || d.Contains("lanh", StringComparison.Ordinal))
            return "cold";
        if (d.Contains("warm", StringComparison.Ordinal) || d.Contains("ấm", StringComparison.Ordinal))
            return "warm";
        if (d.Contains("hot", StringComparison.Ordinal) || d.Contains("nóng", StringComparison.Ordinal))
            return "hot";
        return null;
    }

    private static string InferOperation(IReadOnlyDictionary<string, string> input, string description)
    {
        if (input.ContainsKey("leadCount") || input.ContainsKey("criteria") || input.ContainsKey("topN") || input.ContainsKey("top_n"))
        {
            if (LooksLikeList(description))
                return "list";
            return "batch_score";
        }

        if (LooksLikeList(description))
            return "list";

        if (AgentTaskInput.OptionalGuid(input, "lead_id") is null)
            return "list";

        return "score";
    }

    private static bool LooksLikeList(string description)
    {
        if (string.IsNullOrWhiteSpace(description)) return false;
        var d = description.ToLowerInvariant();
        return d.Contains("list", StringComparison.Ordinal)
            || d.Contains("find", StringComparison.Ordinal)
            || d.Contains("xác định", StringComparison.Ordinal)
            || d.Contains("xac dinh", StringComparison.Ordinal)
            || d.Contains("cold", StringComparison.Ordinal)
            || d.Contains("lạnh", StringComparison.Ordinal)
            || d.Contains("lanh", StringComparison.Ordinal)
            || d.Contains("query", StringComparison.Ordinal)
            || d.Contains("danh sách", StringComparison.Ordinal)
            || d.Contains("danh sach", StringComparison.Ordinal)
            || d.Contains("ưu tiên", StringComparison.Ordinal)
            || d.Contains("uu tien", StringComparison.Ordinal);
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>
/// Orchestration adapter for the report agent.
/// </summary>
public sealed class ReportOrchestrationAdapter(ReportAgentRunner runner) : AgentAdapterBase("report-agent")
{
    private readonly ReportAgentRunner _runner = runner;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = (AgentTaskInput.OptionalString(input, "operation") ?? "snapshot").ToLowerInvariant();
        var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");

        if (operation == "anomaly")
        {
            var points = await _runner.DetectAnomalyAsync(
                tenantId,
                AgentTaskInput.RequiredString(input, "platform"),
                AgentTaskInput.RequiredString(input, "metric"),
                OptionalDouble(input, "z_threshold") ?? 0d,
                OptionalInt(input, "lookback_days") ?? 0, ct).ConfigureAwait(false);
            return Json(points.Select(p => new
            {
                date = ReportAgentRunner.FormatDate(p.At),
                value = p.Value,
                zScore = p.ZScore,
                isAnomaly = p.IsAnomaly,
            }));
        }

        if (operation == "forecast")
        {
            var points = await _runner.ForecastAsync(
                tenantId,
                AgentTaskInput.RequiredString(input, "platform"),
                AgentTaskInput.RequiredString(input, "metric"),
                OptionalInt(input, "horizon_days") ?? 0, ct).ConfigureAwait(false);
            return Json(points.Select(p => new
            {
                date = ReportAgentRunner.FormatDate(p.At),
                value = p.Forecast,
                lowerBound = p.LowerBound,
                upperBound = p.UpperBound,
            }));
        }

        var rows = await _runner.DailySnapshotAsync(
            tenantId, AgentTaskInput.RequiredString(input, "date"), ct).ConfigureAwait(false);
        return Json(rows);
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static double? OptionalDouble(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
