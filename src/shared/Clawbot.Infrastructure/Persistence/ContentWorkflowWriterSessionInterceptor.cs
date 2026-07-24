using System.Data.Common;
using Clawbot.Infrastructure.Content;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Clawbot.Infrastructure.Persistence;

/// <summary>
/// Phase 6.1 bridge: every SQL Server connection used by AppDbContext sets SESSION_CONTEXT writer version
/// so SQL runtime-gate triggers can reject old/no-version writers after minimum is raised.
/// </summary>
public sealed class ContentWorkflowWriterSessionInterceptor(
    IOptions<ContentWorkflowWriterOptions> options) : DbConnectionInterceptor
{
    private readonly ContentWorkflowWriterOptions _options = options.Value;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        StampWriterVersion(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await StampWriterVersionAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    private void StampWriterVersion(DbConnection connection)
    {
        if (connection is not SqlConnection sql)
            return;

        var key = string.IsNullOrWhiteSpace(_options.SessionContextKey)
            ? "clawbot_content_writer_version"
            : _options.SessionContextKey.Trim();
        var version = Math.Max(0, _options.Version);

        using var cmd = sql.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key, @value;";
        cmd.Parameters.Add(new SqlParameter("@key", key));
        cmd.Parameters.Add(new SqlParameter("@value", version));
        cmd.ExecuteNonQuery();
    }

    private async Task StampWriterVersionAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (connection is not SqlConnection sql)
            return;

        var key = string.IsNullOrWhiteSpace(_options.SessionContextKey)
            ? "clawbot_content_writer_version"
            : _options.SessionContextKey.Trim();
        var version = Math.Max(0, _options.Version);

        await using var cmd = sql.CreateCommand();
        cmd.CommandText = "EXEC sp_set_session_context @key, @value;";
        cmd.Parameters.Add(new SqlParameter("@key", key));
        cmd.Parameters.Add(new SqlParameter("@value", version));
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
