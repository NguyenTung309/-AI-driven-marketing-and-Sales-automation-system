using System.Globalization;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Infrastructure.Leads;

namespace Clawbot.AgentService.Services;

/// <summary>
/// Orchestration adapter for the lead agent. Supports single-lead score/create and
/// tenant-wide batch rescore (weekly "Chấm điểm khách tiềm năng" jobs).
/// </summary>
public sealed class LeadOrchestrationAdapter(
    LeadAgentRunner runner,
    LeadBatchRescorer batchRescorer) : AgentAdapterBase("lead-agent")
{
    private readonly LeadAgentRunner _runner = runner;
    private readonly LeadBatchRescorer _batchRescorer = batchRescorer;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = (AgentTaskInput.OptionalString(input, "operation") ?? InferOperation(input)).ToLowerInvariant();

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

        // Single-lead score: requires lead_id. If missing, fall back to batch (planner often omits ids).
        if (AgentTaskInput.OptionalGuid(input, "lead_id") is null)
        {
            var tenantId = AgentTaskInput.RequiredGuid(input, "tenant_id");
            var topN = OptionalInt(input, "topN") ?? OptionalInt(input, "top_n") ?? 5;
            var batch = await _batchRescorer.RescoreTenantAsync(tenantId, topN, ct).ConfigureAwait(false);
            return Json(batch);
        }

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

    private static string InferOperation(IReadOnlyDictionary<string, string> input)
    {
        if (input.ContainsKey("leadCount") || input.ContainsKey("criteria") || input.ContainsKey("topN") || input.ContainsKey("top_n"))
            return "batch_score";
        if (AgentTaskInput.OptionalGuid(input, "lead_id") is null)
            return "batch_score";
        return "score";
    }

    private static int? OptionalInt(IReadOnlyDictionary<string, string> input, string key) =>
        input.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>
/// Orchestration adapter for the report agent. Reuses <see cref="ReportAgentRunner"/> so a planner
/// step can pull a daily snapshot, detect anomalies, or forecast a KPI series.
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
