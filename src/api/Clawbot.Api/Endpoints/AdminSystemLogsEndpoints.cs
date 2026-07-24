using Clawbot.Api.Auth;
using Clawbot.Api.Common.Pagination;
using Clawbot.Api.Middleware;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Endpoints;

public sealed record SystemLogListItemResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string Source,
    string? Category,
    string Message,
    int? StatusCode,
    string? Method,
    string? Path,
    double? ElapsedMs,
    string? TraceId,
    Guid? UserId);

public sealed record SystemLogDetailResponse(
    long Id,
    DateTimeOffset OccurredAt,
    string Level,
    string Source,
    string? Category,
    string Message,
    string? Exception,
    int? StatusCode,
    string? Method,
    string? Path,
    double? ElapsedMs,
    string? TraceId,
    Guid? TenantId,
    Guid? UserId,
    string? Properties);

public sealed record SystemLogSummaryResponse(int Errors24h, int Warnings24h);

public sealed record SystemLogCursorPage(
    IReadOnlyList<SystemLogListItemResponse> Items,
    string? NextCursor,
    int? Total,
    SystemLogSummaryResponse Summary);

public sealed record RequestStatsPointResponse(
    DateTimeOffset BucketHour,
    long Ok2xx,
    long Client4xx,
    long Server5xx);

public static class AdminSystemLogsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSystemLogs(this IEndpointRouteBuilder app)
    {
        var grp = app.MapGroup("/api/admin/system-logs")
            .RequireAuthorization()
            .RequirePermission("system.logs")
            .RequireRateLimiting(RateLimitingExtensions.GeneralPolicy);

        grp.MapGet("/", ListAsync);
        grp.MapGet("/stats/hourly", StatsHourlyAsync);
        grp.MapGet("/{id:long}", GetAsync);

        return app;
    }

    private static async Task<IResult> StatsHourlyAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        hours = Math.Clamp(hours, 1, 168);
        var from = clock.UtcNow.AddHours(-hours);

        var rows = await db.RequestStatsHourly.AsNoTracking()
            .Where(r => r.BucketHour >= from && (r.TenantId == tenantId || r.TenantId == Guid.Empty))
            .GroupBy(r => new { r.BucketHour, r.StatusClass })
            .Select(g => new { g.Key.BucketHour, g.Key.StatusClass, Count = g.Sum(x => x.Count) })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var byHour = rows
            .GroupBy(r => r.BucketHour)
            .OrderBy(g => g.Key)
            .Select(g => new RequestStatsPointResponse(
                g.Key,
                g.Where(x => x.StatusClass == "2xx").Sum(x => x.Count),
                g.Where(x => x.StatusClass == "4xx").Sum(x => x.Count),
                g.Where(x => x.StatusClass == "5xx").Sum(x => x.Count)))
            .ToList();

        return Results.Ok(new { items = byHour });
    }

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        ITenantAccessor tenants,
        IClock clock,
        [FromQuery] string? level,
        [FromQuery] string? statusGroup,
        [FromQuery] string? source,
        [FromQuery] string? q,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? cursor,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        pageSize = KeysetQuery.ClampPageSize(pageSize, defaultSize: 50, max: 200);

        var query = db.SystemLogs.AsNoTracking()
            .Where(l => l.TenantId == tenantId || l.TenantId == null);

        if (!string.IsNullOrWhiteSpace(level))
        {
            var lvl = level.Trim();
            query = query.Where(l => l.Level == lvl);
        }

        if (!string.IsNullOrWhiteSpace(statusGroup))
        {
            query = statusGroup.Trim().ToLowerInvariant() switch
            {
                "4xx" => query.Where(l => l.StatusCode >= 400 && l.StatusCode < 500),
                "5xx" => query.Where(l => l.StatusCode >= 500 && l.StatusCode < 600),
                _ => query,
            };
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var src = source.Trim();
            query = query.Where(l => l.Source == src);
        }

        if (from.HasValue)
            query = query.Where(l => l.OccurredAt >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.OccurredAt <= to.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            var pattern = $"%{EscapeLike(term)}%";
            query = query.Where(l =>
                (l.Message != null && EF.Functions.Like(l.Message, pattern, "\\")) ||
                (l.Path != null && EF.Functions.Like(l.Path, pattern, "\\")) ||
                (l.Category != null && EF.Functions.Like(l.Category, pattern, "\\")) ||
                (l.TraceId != null && EF.Functions.Like(l.TraceId, pattern, "\\")));
        }

        var key = KeysetQuery.DecodeLong(cursor);
        int? total = key is null ? await query.CountAsync(ct).ConfigureAwait(false) : null;
        if (key is not null)
        {
            var ts = key.Value.Ts;
            var id = key.Value.Id;
            query = query.Where(l => l.OccurredAt < ts || (l.OccurredAt == ts && l.Id < id));
        }

        var fetched = await query
            .OrderByDescending(l => l.OccurredAt)
            .ThenByDescending(l => l.Id)
            .Take(pageSize + 1)
            .Select(l => new SystemLogListItemResponse(
                l.Id,
                l.OccurredAt,
                l.Level,
                l.Source,
                l.Category,
                l.Message,
                l.StatusCode,
                l.Method,
                l.Path,
                l.ElapsedMs,
                l.TraceId,
                l.UserId))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var (items, nextCursor) = KeysetQuery.SliceWithLongCursor(
            fetched, pageSize, r => r.OccurredAt, r => r.Id);

        // Summary only on first page — avoid 2 COUNT queries on every infinite-scroll tick.
        SystemLogSummaryResponse summary;
        if (key is null)
        {
            var since = clock.UtcNow.AddHours(-24);
            var summaryQuery = db.SystemLogs.AsNoTracking()
                .Where(l => (l.TenantId == tenantId || l.TenantId == null) && l.OccurredAt >= since);
            var errors24h = await summaryQuery.CountAsync(l => l.Level == "Error" || l.Level == "Fatal", ct)
                .ConfigureAwait(false);
            var warnings24h = await summaryQuery.CountAsync(l => l.Level == "Warning", ct)
                .ConfigureAwait(false);
            summary = new SystemLogSummaryResponse(errors24h, warnings24h);
        }
        else
        {
            summary = new SystemLogSummaryResponse(0, 0);
        }

        return Results.Ok(new SystemLogCursorPage(
            items,
            nextCursor,
            total,
            summary));
    }

    private static async Task<IResult> GetAsync(
        long id,
        AppDbContext db,
        ITenantAccessor tenants,
        CancellationToken ct = default)
    {
        var tenantId = tenants.Require().TenantId;
        var row = await db.SystemLogs.AsNoTracking()
            .Where(l => l.Id == id && (l.TenantId == tenantId || l.TenantId == null))
            .Select(l => new SystemLogDetailResponse(
                l.Id,
                l.OccurredAt,
                l.Level,
                l.Source,
                l.Category,
                l.Message,
                l.Exception,
                l.StatusCode,
                l.Method,
                l.Path,
                l.ElapsedMs,
                l.TraceId,
                l.TenantId,
                l.UserId,
                l.Properties))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return row is null ? Results.NotFound(new { error = "system_log_not_found" }) : Results.Ok(row);
    }

    private static string EscapeLike(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal);
}
