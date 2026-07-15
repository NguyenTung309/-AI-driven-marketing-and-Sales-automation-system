namespace Clawbot.Api.Common.Pagination;

/// <summary>Normalized offset page parameters with clamp defaults.</summary>
public readonly record struct PageRequest(int Page, int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int DefaultMaxPageSize = 200;

    public int Skip => (Page - 1) * PageSize;

    public static PageRequest Create(int page, int pageSize, int defaultPageSize = DefaultPageSize, int maxPageSize = DefaultMaxPageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > maxPageSize) pageSize = defaultPageSize;
        return new PageRequest(page, pageSize);
    }

    public static PageRequest CreateClamped(int page, int pageSize, int defaultPageSize = DefaultPageSize, int maxPageSize = DefaultMaxPageSize)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, maxPageSize);
        if (pageSize < 1) pageSize = defaultPageSize;
        return new PageRequest(page, pageSize);
    }
}
