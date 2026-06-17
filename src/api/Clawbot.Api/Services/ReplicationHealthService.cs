using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Data;
using System.Data.Common;
using System.Globalization;

namespace Clawbot.Api.Services;

public sealed class ReplicationHealthService(IOptions<ReplicationOptions> options, IReplicationLagProbe lagProbe)
{
    private readonly ReplicationOptions _options = options.Value;
    private readonly IReplicationLagProbe _lagProbe = lagProbe;

    public async Task<ReplicationHealthReport> GetAsync(CancellationToken ct = default)
    {
        var currentRegion = NormalizeRegion(_options.CurrentRegion);
        var primaryRegion = NormalizeRegion(_options.PrimaryRegion);
        var configuredRegions = _options.Regions
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r => new ReplicationRegionStatus(
                NormalizeRegion(r.Name),
                NormalizeRole(r.Role),
                Math.Max(0, r.Priority),
                string.IsNullOrWhiteSpace(r.AppBaseUrl) ? null : r.AppBaseUrl.Trim()))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToList();

        if (!_options.Enabled)
        {
            return new ReplicationHealthReport(
                "disabled",
                currentRegion,
                primaryRegion,
                CurrentRole: "primary",
                WritesAllowed: true,
                ActiveRegions: configuredRegions.Count,
                ReplicaLagSeconds: null,
                Regions: configuredRegions,
                Checks: new[] { new ReplicationHealthCheck("replication_enabled", "disabled", "Multi-region replication is disabled for this deployment.") });
        }

        var checks = new List<ReplicationHealthCheck>
        {
            Check("replication_enabled", ok: true, "Multi-region replication is enabled."),
        };

        var current = configuredRegions.SingleOrDefault(r => r.Name == currentRegion);
        var primary = configuredRegions.SingleOrDefault(r => r.Name == primaryRegion);
        var primaryCount = configuredRegions.Count(r => r.Role == "primary");
        var topologyOk = configuredRegions.Count >= 2 && primaryCount == 1;
        checks.Add(Check("topology", topologyOk, $"{configuredRegions.Count} configured region(s), {primaryCount} primary region(s)."));
        checks.Add(Check("current_region", current is not null, current is null ? $"Current region '{currentRegion}' is not configured." : $"Current region '{currentRegion}' is configured."));
        checks.Add(Check("primary_region", primary is not null, primary is null ? $"Primary region '{primaryRegion}' is not configured." : $"Primary region '{primaryRegion}' is configured."));

        var currentIsPrimary = current is not null && current.Name == primaryRegion && current.Role == "primary";
        var writesAllowed = _options.AllowWrites && currentIsPrimary;
        checks.Add(Check(
            "write_guard",
            current is not null && (currentIsPrimary == writesAllowed),
            writesAllowed
                ? "This region is primary and may accept writes."
                : "This region is not primary; write traffic must be routed to the primary region."));

        int? replicaLagSeconds = null;
        if (current is not null && !currentIsPrimary)
        {
            var probe = await _lagProbe.ProbeAsync(ct).ConfigureAwait(false);
            replicaLagSeconds = probe.Lag is null ? null : (int)Math.Ceiling(probe.Lag.Value.TotalSeconds);
            checks.Add(Check(
                "replica_lag",
                probe.Status == "ok" && replicaLagSeconds <= _options.MaxReplicaLagSeconds,
                probe.Status == "ok"
                    ? $"Replica lag is {replicaLagSeconds}s; threshold is {_options.MaxReplicaLagSeconds}s."
                    : probe.Detail));
        }
        else
        {
            checks.Add(Check("replica_lag", ok: true, "Primary region does not require a replica lag probe."));
        }

        var status = checks.Any(c => c.Status == "degraded") ? "degraded" : "ok";
        return new ReplicationHealthReport(
            status,
            currentRegion,
            primaryRegion,
            current?.Role ?? "unknown",
            writesAllowed,
            configuredRegions.Count,
            replicaLagSeconds,
            configuredRegions,
            checks);
    }

    private static string NormalizeRegion(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "local" : value.Trim().ToLowerInvariant();

    private static string NormalizeRole(string? value)
    {
        var role = string.IsNullOrWhiteSpace(value) ? "secondary" : value.Trim().ToLowerInvariant();
        return role is "primary" or "secondary" ? role : "secondary";
    }

    private static ReplicationHealthCheck Check(string name, bool ok, string detail) =>
        new(name, ok ? "ok" : "degraded", detail);
}

public sealed class ReplicationOptions
{
    public const string SectionName = "Deployment:Replication";

    public bool Enabled { get; set; }
    public string CurrentRegion { get; set; } = "local";
    public string PrimaryRegion { get; set; } = "local";
    public bool AllowWrites { get; set; } = true;
    public int MaxReplicaLagSeconds { get; set; } = 30;
    public int LagProbeTimeoutSeconds { get; set; } = 5;
    public string LagProbeSql { get; set; } = string.Empty;
    public List<ReplicationRegionOptions> Regions { get; set; } = new();
}

public sealed class ReplicationRegionOptions
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "secondary";
    public int Priority { get; set; }
    public string AppBaseUrl { get; set; } = string.Empty;
}

public interface IReplicationLagProbe
{
    Task<ReplicationProbeResult> ProbeAsync(CancellationToken ct = default);
}

public sealed class SqlServerReplicationLagProbe(AppDbContext db, IOptions<ReplicationOptions> options) : IReplicationLagProbe
{
    private readonly AppDbContext _db = db;
    private readonly ReplicationOptions _options = options.Value;

    public async Task<ReplicationProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.LagProbeSql))
        {
            return ReplicationProbeResult.NotConfigured("Deployment:Replication:LagProbeSql is not configured.");
        }

        var connection = _db.Database.GetDbConnection();
        var closeAfter = connection.State == ConnectionState.Closed;
        if (closeAfter)
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = _options.LagProbeSql;
            command.CommandTimeout = Math.Max(1, _options.LagProbeTimeoutSeconds);
            var value = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (value is null || value is DBNull)
            {
                return ReplicationProbeResult.Unavailable("Lag probe returned no value.");
            }

            var seconds = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            return ReplicationProbeResult.Available(TimeSpan.FromSeconds(Math.Max(0, seconds)));
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or FormatException)
        {
            return ReplicationProbeResult.Unavailable($"Lag probe failed: {ex.Message}");
        }
        finally
        {
            if (closeAfter)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }
}

public sealed record ReplicationProbeResult(string Status, TimeSpan? Lag, string Detail)
{
    public static ReplicationProbeResult Available(TimeSpan lag) => new("ok", lag, $"Replica lag is {Math.Ceiling(lag.TotalSeconds)}s.");
    public static ReplicationProbeResult NotConfigured(string detail) => new("degraded", null, detail);
    public static ReplicationProbeResult Unavailable(string detail) => new("degraded", null, detail);
}

public sealed record ReplicationHealthReport(
    string Status,
    string CurrentRegion,
    string PrimaryRegion,
    string CurrentRole,
    bool WritesAllowed,
    int ActiveRegions,
    int? ReplicaLagSeconds,
    IReadOnlyList<ReplicationRegionStatus> Regions,
    IReadOnlyList<ReplicationHealthCheck> Checks);

public sealed record ReplicationRegionStatus(string Name, string Role, int Priority, string? AppBaseUrl);

public sealed record ReplicationHealthCheck(string Name, string Status, string Detail);
