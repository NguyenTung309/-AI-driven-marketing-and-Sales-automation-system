using System.Text.Json;
using Clawbot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Clawbot.Infrastructure.Auth;

public sealed partial class PermissionResolver(
    AppDbContext db,
    IConnectionMultiplexer redis,
    ILogger<PermissionResolver> logger) : IPermissionResolver
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(600);

    private static string Key(Guid roleId) => $"perm:role:{roleId}";

    public async Task<IReadOnlySet<string>> GetPermissionsAsync(Guid roleId, CancellationToken ct = default)
    {
        if (roleId == Guid.Empty) return new HashSet<string>();

        // Redis miss/down must never block the request — fall back to the DB (NFR-02).
        try
        {
            var cached = await redis.GetDatabase().StringGetAsync(Key(roleId));
            if (cached.HasValue)
            {
                var codes = JsonSerializer.Deserialize<string[]>(cached.ToString()) ?? [];
                return codes.ToHashSet();
            }
        }
        catch (RedisException ex)
        {
            LogRedisUnavailable(logger, ex.Message);
        }

        var fromDb = await LoadFromDbAsync(roleId, ct);

        try
        {
            await redis.GetDatabase().StringSetAsync(
                Key(roleId), JsonSerializer.Serialize(fromDb.ToArray()), CacheTtl);
        }
        catch (RedisException ex)
        {
            LogRedisUnavailable(logger, ex.Message);
        }

        return fromDb;
    }

    public async Task InvalidateAsync(Guid roleId, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(Key(roleId));
        }
        catch (RedisException ex)
        {
            LogRedisUnavailable(logger, ex.Message);
        }
    }

    private async Task<HashSet<string>> LoadFromDbAsync(Guid roleId, CancellationToken ct)
    {
        var codes = await (
            from rp in db.RolePermissions
            join p in db.Permissions on rp.PermissionId equals p.Id
            where rp.RoleId == roleId
            select p.Code).ToListAsync(ct);
        return codes.ToHashSet();
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning,
        Message = "PermissionResolver: Redis unavailable, falling back to DB ({Reason})")]
    private static partial void LogRedisUnavailable(ILogger logger, string reason);
}
