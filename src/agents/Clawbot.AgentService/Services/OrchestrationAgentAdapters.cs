using System.Globalization;
using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;

namespace Clawbot.AgentService.Services;

/// <summary>
/// Orchestration adapter for the lead agent. Reuses <see cref="LeadAgentRunner"/> (the same core
/// logic the gRPC service uses) so a planner step can score or create leads.
/// </summary>
public sealed class LeadOrchestrationAdapter(LeadAgentRunner runner) : AgentAdapterBase("lead-agent")
{
    private readonly LeadAgentRunner _runner = runner;

    protected override async Task<string> ExecuteCoreAsync(AgentTask task, CancellationToken ct)
    {
        var input = task.Input;
        var operation = (AgentTaskInput.OptionalString(input, "operation") ?? "score").ToLowerInvariant();

        if (operation == "create")
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
