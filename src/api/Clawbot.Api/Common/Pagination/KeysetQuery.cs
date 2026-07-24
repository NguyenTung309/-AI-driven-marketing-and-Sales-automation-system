namespace Clawbot.Api.Common.Pagination;

/// <summary>
/// Canonical keyset helpers used by list endpoints.
/// Pattern: Decode once → optional Count on first page → filter → Take(size+1) → SliceWithCursor.
/// </summary>
public static class KeysetQuery
{
    public static int ClampPageSize(int pageSize, int defaultSize = 50, int max = 200)
        => pageSize < 1 || pageSize > max ? defaultSize : pageSize;

    /// <summary>Decode once; invalid/missing → null (first page).</summary>
    public static CursorKey? Decode(string? cursor) => CursorCodec.TryDecode(cursor);

    public static (List<T> Items, string? NextCursor) SliceWithCursor<T>(
        IReadOnlyList<T> rowsIncludingOverflow,
        int pageSize,
        Func<T, DateTimeOffset> ts,
        Func<T, Guid> id)
    {
        if (rowsIncludingOverflow.Count > pageSize)
        {
            var last = rowsIncludingOverflow[pageSize - 1];
            var page = rowsIncludingOverflow.Take(pageSize).ToList();
            return (page, CursorCodec.Encode(ts(last), id(last)));
        }

        return (rowsIncludingOverflow as List<T> ?? rowsIncludingOverflow.ToList(), null);
    }

    /// <summary>Decode long-id cursor once; invalid/missing → null (first page).</summary>
    public static LongCursorKey? DecodeLong(string? cursor) => LongCursorCodec.TryDecode(cursor);

    public static (List<T> Items, string? NextCursor) SliceWithLongCursor<T>(
        IReadOnlyList<T> rowsIncludingOverflow,
        int pageSize,
        Func<T, DateTimeOffset> ts,
        Func<T, long> id)
    {
        if (rowsIncludingOverflow.Count > pageSize)
        {
            var last = rowsIncludingOverflow[pageSize - 1];
            var page = rowsIncludingOverflow.Take(pageSize).ToList();
            return (page, LongCursorCodec.Encode(ts(last), id(last)));
        }

        return (rowsIncludingOverflow as List<T> ?? rowsIncludingOverflow.ToList(), null);
    }
}
