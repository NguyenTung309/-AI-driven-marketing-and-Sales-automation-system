using Clawbot.Api.Contracts.Common;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Common.Pagination;

public static class PaginationExtensions
{
    /// <summary>
    /// Offset page: CountAsync + Skip/Take, then map rows to DTOs.
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TEntity, TDto>(
        this IQueryable<TEntity> query,
        int page,
        int pageSize,
        Func<TEntity, TDto> selector,
        int defaultPageSize = PageRequest.DefaultPageSize,
        int maxPageSize = PageRequest.DefaultMaxPageSize,
        CancellationToken ct = default)
    {
        var req = PageRequest.Create(page, pageSize, defaultPageSize, maxPageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var rows = await query
            .Skip(req.Skip)
            .Take(req.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        var items = rows.Select(selector).ToList();
        return new PagedResult<TDto>(items, total, req.Page, req.PageSize);
    }

    /// <summary>
    /// Offset page when projection is already applied via Select (IQueryable of DTO).
    /// </summary>
    public static async Task<PagedResult<TDto>> ToPagedResultAsync<TDto>(
        this IQueryable<TDto> query,
        int page,
        int pageSize,
        int defaultPageSize = PageRequest.DefaultPageSize,
        int maxPageSize = PageRequest.DefaultMaxPageSize,
        CancellationToken ct = default)
    {
        var req = PageRequest.Create(page, pageSize, defaultPageSize, maxPageSize);
        var total = await query.CountAsync(ct).ConfigureAwait(false);
        var items = await query
            .Skip(req.Skip)
            .Take(req.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new PagedResult<TDto>(items, total, req.Page, req.PageSize);
    }
}
