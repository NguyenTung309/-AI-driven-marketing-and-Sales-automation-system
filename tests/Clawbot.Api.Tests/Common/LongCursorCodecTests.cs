using Clawbot.Api.Common.Pagination;
using FluentAssertions;
using Xunit;

namespace Clawbot.Api.Tests.Common;

public sealed class LongCursorCodecTests
{
    [Fact]
    public void Encode_decode_roundtrips()
    {
        var ts = new DateTimeOffset(2026, 7, 17, 12, 30, 0, TimeSpan.Zero);
        var cursor = LongCursorCodec.Encode(ts, 42);
        var key = LongCursorCodec.TryDecode(cursor);
        key.Should().NotBeNull();
        key!.Value.Id.Should().Be(42);
        key.Value.Ts.Should().Be(ts);
    }

    [Fact]
    public void TryDecode_returns_null_for_garbage()
    {
        LongCursorCodec.TryDecode("%%%").Should().BeNull();
        LongCursorCodec.TryDecode(null).Should().BeNull();
        LongCursorCodec.TryDecode("").Should().BeNull();
    }

    [Fact]
    public void SliceWithLongCursor_returns_next_cursor_when_overflow()
    {
        var rows = Enumerable.Range(1, 3)
            .Select(i => new Sample(i, new DateTimeOffset(2026, 7, 17, 0, 0, i, TimeSpan.Zero)))
            .ToList();

        var (page, next) = KeysetQuery.SliceWithLongCursor(rows, 2, r => r.Ts, r => r.Id);
        page.Should().HaveCount(2);
        next.Should().NotBeNullOrEmpty();

        var key = KeysetQuery.DecodeLong(next);
        key.Should().NotBeNull();
        key!.Value.Id.Should().Be(2);
    }

    private sealed record Sample(long Id, DateTimeOffset Ts);
}
