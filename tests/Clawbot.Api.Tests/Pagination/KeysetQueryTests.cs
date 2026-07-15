using Clawbot.Api.Common.Pagination;
using FluentAssertions;

namespace Clawbot.Api.Tests.Pagination;

public sealed class KeysetQueryTests
{
    private sealed record Row(DateTimeOffset Ts, Guid Id, string Label);

    [Fact]
    public void SliceWithCursor_WhenNoOverflow_ReturnsNullCursor()
    {
        var rows = new[]
        {
            new Row(DateTimeOffset.UtcNow, Guid.NewGuid(), "a"),
            new Row(DateTimeOffset.UtcNow.AddMinutes(-1), Guid.NewGuid(), "b"),
        };

        var (page, next) = KeysetQuery.SliceWithCursor(rows, pageSize: 5, r => r.Ts, r => r.Id);

        page.Should().HaveCount(2);
        next.Should().BeNull();
    }

    [Fact]
    public void SliceWithCursor_WhenOverflow_EncodesCursorFromLastKeptRow()
    {
        var t0 = new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero);
        var id0 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var id1 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var id2 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var rows = new[]
        {
            new Row(t0, id0, "newest"),
            new Row(t0.AddMinutes(-1), id1, "mid"),
            new Row(t0.AddMinutes(-2), id2, "oldest"),
        };

        var (page, next) = KeysetQuery.SliceWithCursor(rows, pageSize: 2, r => r.Ts, r => r.Id);

        page.Should().HaveCount(2);
        page.Select(r => r.Label).Should().Equal("newest", "mid");
        next.Should().NotBeNullOrEmpty();
        var key = CursorCodec.TryDecode(next);
        key.Should().NotBeNull();
        key!.Value.Ts.Should().Be(t0.AddMinutes(-1));
        key.Value.Id.Should().Be(id1);
    }

    [Fact]
    public void KeysetWalk_TwoPages_NoDupNoSkip()
    {
        // Simulate DESC feed of 5 rows, pageSize=2.
        var baseTs = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var all = Enumerable.Range(0, 5)
            .Select(i => new Row(baseTs.AddMinutes(-i), Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), $"r{i}"))
            .ToList();

        // Page 1
        var fetched1 = TakeKeyset(all, cursor: null, pageSize: 2);
        var (page1, cursor1) = KeysetQuery.SliceWithCursor(fetched1, 2, r => r.Ts, r => r.Id);
        page1.Select(r => r.Label).Should().Equal("r0", "r1");
        cursor1.Should().NotBeNull();

        // Page 2
        var fetched2 = TakeKeyset(all, cursor1, pageSize: 2);
        var (page2, cursor2) = KeysetQuery.SliceWithCursor(fetched2, 2, r => r.Ts, r => r.Id);
        page2.Select(r => r.Label).Should().Equal("r2", "r3");
        cursor2.Should().NotBeNull();

        // Page 3 (last)
        var fetched3 = TakeKeyset(all, cursor2, pageSize: 2);
        var (page3, cursor3) = KeysetQuery.SliceWithCursor(fetched3, 2, r => r.Ts, r => r.Id);
        page3.Select(r => r.Label).Should().Equal("r4");
        cursor3.Should().BeNull();

        var walked = page1.Concat(page2).Concat(page3).Select(r => r.Id).ToList();
        walked.Should().OnlyHaveUniqueItems();
        walked.Should().BeEquivalentTo(all.Select(r => r.Id), o => o.WithStrictOrdering());
    }

    [Fact]
    public void KeysetWalk_InsertBetweenPages_NoDupNoSkipOfStableSnapshot()
    {
        // Initial 4 rows. After page1, insert a row NEWER than cursor boundary so it would only
        // appear on a full refresh of page1 (prepend), not as a duplicate on page2.
        var baseTs = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var all = Enumerable.Range(0, 4)
            .Select(i => new Row(baseTs.AddMinutes(-i), Guid.Parse($"10000000-0000-0000-0000-{i:D12}"), $"r{i}"))
            .ToList();

        var fetched1 = TakeKeyset(all, null, 2);
        var (page1, cursor1) = KeysetQuery.SliceWithCursor(fetched1, 2, r => r.Ts, r => r.Id);
        page1.Select(r => r.Label).Should().Equal("r0", "r1");

        // Insert brand-new head (newer than everything). Page2 uses cursor after r1 — must not include it.
        var inserted = new Row(baseTs.AddMinutes(5), Guid.Parse("20000000-0000-0000-0000-000000000099"), "NEW");
        all.Insert(0, inserted);

        var fetched2 = TakeKeyset(all, cursor1, 2);
        var (page2, _) = KeysetQuery.SliceWithCursor(fetched2, 2, r => r.Ts, r => r.Id);

        page2.Select(r => r.Label).Should().Equal("r2", "r3");
        page2.Should().NotContain(r => r.Label == "NEW");
        page1.Concat(page2).Select(r => r.Id).Should().OnlyHaveUniqueItems();
        // Inserted row is only visible after prepending page1 (realtime) or full refetch — not via nextCursor.
    }

    /// <summary>
    /// In-memory stand-in for: OrderByDescending(ts).ThenByDescending(id).Where keyset before.Take(n+1).
    /// </summary>
    private static List<Row> TakeKeyset(IReadOnlyList<Row> source, string? cursor, int pageSize)
    {
        IEnumerable<Row> q = source
            .OrderByDescending(r => r.Ts)
            .ThenByDescending(r => r.Id);

        var key = KeysetQuery.Decode(cursor);
        if (key is not null)
        {
            var ts = key.Value.Ts;
            var id = key.Value.Id;
            q = q.Where(r => r.Ts < ts || (r.Ts == ts && r.Id.CompareTo(id) < 0));
        }

        return q.Take(pageSize + 1).ToList();
    }
}
