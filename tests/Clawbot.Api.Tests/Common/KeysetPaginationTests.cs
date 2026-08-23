using Clawbot.Api.Common.Pagination;
using FluentAssertions;

namespace Clawbot.Api.Tests.Common;

public sealed class LongCursorCodecTests
{
    private static readonly DateTimeOffset Ts = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Encode_ThenTryDecode_RoundTrips()
    {
        var key = LongCursorCodec.TryDecode(LongCursorCodec.Encode(Ts, 4_294_967_296L));

        key.Should().NotBeNull();
        key!.Value.Ts.Should().Be(Ts);
        key.Value.Id.Should().Be(4_294_967_296L);
    }

    [Fact]
    public void Encode_ProducesUrlSafeString()
    {
        var cursor = LongCursorCodec.Encode(Ts, 12345);

        cursor.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryDecode_BlankCursor_ReturnsNull(string? cursor)
    {
        LongCursorCodec.TryDecode(cursor).Should().BeNull();
    }

    [Theory]
    [InlineData("!!!khong-base64!!!")]
    [InlineData("bm90LWpzb24")]
    public void TryDecode_CorruptCursor_ReturnsNull(string cursor)
    {
        LongCursorCodec.TryDecode(cursor).Should().BeNull();
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void TryDecode_NonPositiveId_ReturnsNull(long id)
    {
        LongCursorCodec.TryDecode(LongCursorCodec.Encode(Ts, id)).Should().BeNull();
    }

    [Fact]
    public void TryDecode_TrimsWhitespace()
    {
        var cursor = LongCursorCodec.Encode(Ts, 99);

        LongCursorCodec.TryDecode($"  {cursor} ")!.Value.Id.Should().Be(99);
    }

    [Fact]
    public void LongCursorKey_Equality_ComparesByValue()
    {
        new LongCursorKey(Ts, 5).Should().Be(new LongCursorKey(Ts, 5));
        new LongCursorKey(Ts, 5).Should().NotBe(new LongCursorKey(Ts, 6));
    }
}

public sealed class KeysetQueryTests
{
    private static readonly DateTimeOffset Ts = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    private sealed record Row(Guid Id, long LongId, DateTimeOffset Ts);

    private static List<Row> Rows(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new Row(Guid.NewGuid(), i + 1, Ts.AddMinutes(-i)))
            .ToList();

    [Theory]
    [InlineData(10, 10)]
    [InlineData(200, 200)]
    [InlineData(1, 1)]
    public void ClampPageSize_InRange_IsKept(int input, int expected)
    {
        KeysetQuery.ClampPageSize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(201)]
    public void ClampPageSize_OutOfRange_FallsBackToDefault(int input)
    {
        KeysetQuery.ClampPageSize(input).Should().Be(50);
    }

    [Fact]
    public void ClampPageSize_HonoursCustomDefaultAndMax()
    {
        KeysetQuery.ClampPageSize(999, defaultSize: 25, max: 100).Should().Be(25);
        KeysetQuery.ClampPageSize(80, defaultSize: 25, max: 100).Should().Be(80);
    }

    [Fact]
    public void Decode_DelegatesToCursorCodec()
    {
        var id = Guid.NewGuid();
        var cursor = CursorCodec.Encode(Ts, id);

        KeysetQuery.Decode(cursor)!.Value.Id.Should().Be(id);
        KeysetQuery.Decode(null).Should().BeNull();
    }

    [Fact]
    public void DecodeLong_DelegatesToLongCursorCodec()
    {
        var cursor = LongCursorCodec.Encode(Ts, 42);

        KeysetQuery.DecodeLong(cursor)!.Value.Id.Should().Be(42);
        KeysetQuery.DecodeLong("hong").Should().BeNull();
    }

    [Fact]
    public void SliceWithCursor_NoOverflow_ReturnsAllRowsAndNullCursor()
    {
        var rows = Rows(3);

        var (items, next) = KeysetQuery.SliceWithCursor(rows, 5, r => r.Ts, r => r.Id);

        items.Should().HaveCount(3);
        next.Should().BeNull();
    }

    [Fact]
    public void SliceWithCursor_ExactlyPageSize_ReturnsNullCursor()
    {
        var rows = Rows(5);

        var (items, next) = KeysetQuery.SliceWithCursor(rows, 5, r => r.Ts, r => r.Id);

        items.Should().HaveCount(5);
        next.Should().BeNull();
    }

    [Fact]
    public void SliceWithCursor_Overflow_TrimsAndEncodesLastRowOfPage()
    {
        var rows = Rows(6);

        var (items, next) = KeysetQuery.SliceWithCursor(rows, 5, r => r.Ts, r => r.Id);

        items.Should().HaveCount(5);
        next.Should().NotBeNull();
        // Cursor phải trỏ tới dòng CUỐI của trang, không phải dòng tràn.
        CursorCodec.TryDecode(next)!.Value.Id.Should().Be(rows[4].Id);
    }

    [Fact]
    public void SliceWithLongCursor_NoOverflow_ReturnsNullCursor()
    {
        var (items, next) = KeysetQuery.SliceWithLongCursor(Rows(2), 5, r => r.Ts, r => r.LongId);

        items.Should().HaveCount(2);
        next.Should().BeNull();
    }

    [Fact]
    public void SliceWithLongCursor_Overflow_EncodesLastRowOfPage()
    {
        var rows = Rows(6);

        var (items, next) = KeysetQuery.SliceWithLongCursor(rows, 5, r => r.Ts, r => r.LongId);

        items.Should().HaveCount(5);
        LongCursorCodec.TryDecode(next)!.Value.Id.Should().Be(rows[4].LongId);
    }

    [Fact]
    public void SliceWithCursor_EmptyInput_ReturnsEmptyPage()
    {
        var (items, next) = KeysetQuery.SliceWithCursor(new List<Row>(), 5, r => r.Ts, r => r.Id);

        items.Should().BeEmpty();
        next.Should().BeNull();
    }
}
