using System.Data;
using Clawbot.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Clawbot.Infrastructure.Content;

public sealed record ContentWorkflowRuntimeGateSnapshot(
    bool PublicationPaused,
    int MinimumWriterVersion,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    string? Notes);

public interface IContentWorkflowRuntimeGate
{
    Task<ContentWorkflowRuntimeGateSnapshot> GetAsync(CancellationToken cancellationToken = default);

    Task<bool> IsPublicationPausedAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads singleton dbo.content_workflow_runtime_gate with short cache so Hangfire publish loops
/// fail closed without a DB hit every schedule row.
/// </summary>
public sealed partial class ContentWorkflowRuntimeGate(
    AppDbContext db,
    IMemoryCache cache,
    ILogger<ContentWorkflowRuntimeGate> logger) : IContentWorkflowRuntimeGate
{
    public const string CacheKey = "content.workflow.runtime_gate";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(15);

    public async Task<ContentWorkflowRuntimeGateSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue(CacheKey, out ContentWorkflowRuntimeGateSnapshot? cached) && cached is not null)
            return cached;

        ContentWorkflowRuntimeGateSnapshot snapshot;
        try
        {
            // Non-relational providers (unit tests) have no gate table — stay permissive like pre-cutover.
            if (!db.Database.IsRelational())
            {
                snapshot = Permissive("non_relational_provider");
            }
            else
            {
                var connection = db.Database.GetDbConnection();
                if (connection.State != ConnectionState.Open)
                    await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT TOP (1)
                        publication_paused,
                        minimum_writer_version,
                        updated_at,
                        updated_by,
                        notes
                    FROM dbo.content_workflow_runtime_gate
                    WHERE id = 1
                    """;

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    snapshot = Permissive("gate_missing_permissive");
                }
                else
                {
                    snapshot = new ContentWorkflowRuntimeGateSnapshot(
                        PublicationPaused: reader.GetBoolean(0),
                        MinimumWriterVersion: reader.GetInt32(1),
                        UpdatedAt: reader.GetFieldValue<DateTimeOffset>(2),
                        UpdatedBy: reader.IsDBNull(3) ? null : reader.GetString(3),
                        Notes: reader.IsDBNull(4) ? null : reader.GetString(4));
                }
            }
        }
        catch (Exception ex) when (IsMissingGate(ex))
        {
            // Expand/bridge phase: table not applied yet → do not block publication.
            LogGateMissing(logger, ex);
            snapshot = Permissive("gate_missing_exception_permissive");
        }
        catch (Exception ex)
        {
            // Gate exists but is unreadable during cutover uncertainty → pause publication.
            LogGateReadFailed(logger, ex);
            snapshot = new ContentWorkflowRuntimeGateSnapshot(
                PublicationPaused: true,
                MinimumWriterVersion: int.MaxValue,
                UpdatedAt: DateTimeOffset.UtcNow,
                UpdatedBy: null,
                Notes: "gate_read_failed_fail_closed");
        }

        cache.Set(CacheKey, snapshot, CacheTtl);
        return snapshot;
    }

    public async Task<bool> IsPublicationPausedAsync(CancellationToken cancellationToken = default)
    {
        var gate = await GetAsync(cancellationToken).ConfigureAwait(false);
        return gate.PublicationPaused;
    }

    private static ContentWorkflowRuntimeGateSnapshot Permissive(string notes) =>
        new(false, 0, DateTimeOffset.UtcNow, null, notes);

    private static bool IsMissingGate(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SqlException sql && sql.Number == 208)
                return true;

            var message = current.Message;
            if (message.Contains("content_workflow_runtime_gate", StringComparison.OrdinalIgnoreCase)
                && (message.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("no such table", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    [LoggerMessage(
        EventId = 5601,
        Level = LogLevel.Debug,
        Message = "content_workflow_runtime_gate missing; staying publication-permissive for expand/bridge")]
    private static partial void LogGateMissing(ILogger logger, Exception ex);

    [LoggerMessage(
        EventId = 5602,
        Level = LogLevel.Warning,
        Message = "content_workflow_runtime_gate read failed; failing closed with publication paused")]
    private static partial void LogGateReadFailed(ILogger logger, Exception ex);
}
