namespace Clawbot.Api.Contracts.Common;

/// <summary>Offset pagination envelope for stable tables (leads, users, briefs, kb…).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

/// <summary>
/// Keyset/cursor envelope for time-ordered feeds (conversations, notifications, jobs, logs…).
/// <see cref="Total"/> is populated on the first page only (cursor null); later pages may omit it.
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, int? Total);
