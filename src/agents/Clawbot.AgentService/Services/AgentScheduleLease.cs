using System.Data;
using System.Data.Common;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.AgentService.Services;

// A session-scoped SQL Server application lock fences live work from stale-run recovery.
// SQL releases it when the owning process/connection dies, unlike an in-memory mutex.
internal sealed class AgentScheduleLease : IAsyncDisposable
{
    private readonly AppDbContext _db;
    private readonly string _resource;
    private readonly bool _closeConnection;

    private AgentScheduleLease(AppDbContext db, string resource, bool closeConnection)
    {
        _db = db;
        _resource = resource;
        _closeConnection = closeConnection;
    }

    public static async Task<AgentScheduleLease?> TryAcquireAsync(
        AppDbContext db,
        Guid scheduleId,
        CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        var closeConnection = connection.State != ConnectionState.Open;
        if (closeConnection)
            await db.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        var resource = $"clawbot:schedule:{scheduleId:N}";
        try
        {
            var result = await ExecuteApplicationLockAsync(connection, resource, "sp_getapplock", ct).ConfigureAwait(false);
            if (result >= 0)
                return new AgentScheduleLease(db, resource, closeConnection);
        }
        catch
        {
            if (closeConnection)
                await db.Database.CloseConnectionAsync().ConfigureAwait(false);
            throw;
        }

        if (closeConnection)
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            var connection = _db.Database.GetDbConnection();
            if (connection.State == ConnectionState.Open)
                await ExecuteApplicationLockAsync(connection, _resource, "sp_releaseapplock", CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (_closeConnection)
                await _db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task<int> ExecuteApplicationLockAsync(
        DbConnection connection,
        string resource,
        string procedure,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = procedure == "sp_getapplock"
            ? "DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @result;"
            : "DECLARE @result int; EXEC @result = sys.sp_releaseapplock @Resource = @resource, @LockOwner = 'Session'; SELECT @result;";
        var resourceParameter = command.CreateParameter();
        resourceParameter.ParameterName = "@resource";
        resourceParameter.Value = resource;
        command.Parameters.Add(resourceParameter);

        var result = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return Convert.ToInt32(result, System.Globalization.CultureInfo.InvariantCulture);
    }
}
