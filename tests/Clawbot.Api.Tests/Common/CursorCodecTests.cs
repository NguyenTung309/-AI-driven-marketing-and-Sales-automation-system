using Clawbot.Api.Common.Pagination;
using FluentAssertions;

namespace Clawbot.Api.Tests.Common;

public sealed class CursorCodecTests
{
    private static readonly DateTimeOffset Ts = new(2026, 8, 17, 10, 30, 15, TimeSpan.Zero);

    [Fact]
    public void Encode_ThenTryDecode_RoundTrips()
    {
        var id = Guid.NewGuid();

        var key = CursorCodec.TryDecode(CursorCodec.Encode(Ts, id));

        key.Should().NotBeNull();
        key!.Value.Ts.Should().Be(Ts);
        key.Value.Id.Should().Be(id);
    }

    [Fact]
    public void Encode_ProducesBase64UrlSafeString()
    {
        var cursor = CursorCodec.Encode(Ts, Guid.NewGuid());

        // Base64Url: không có '+', '/' hay padding '=' để cursor đi thẳng vào query string.
        cursor.Should().NotContain("+").And.NotContain("/").And.NotContain("=");
    }

    [Fact]
    public void TryDecode_TrimsSurroundingWhitespace()
    {
        var id = Guid.NewGuid();
        var cursor = CursorCodec.Encode(Ts, id);

        CursorCodec.TryDecode($"  {cursor}  ")!.Value.Id.Should().Be(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryDecode_BlankCursor_ReturnsNull(string? cursor)
    {
        CursorCodec.TryDecode(cursor).Should().BeNull();
    }

    [Theory]
    [InlineData("!!!not-base64!!!")]
    [InlineData("bm90LWpzb24")]
    public void TryDecode_CorruptCursor_ReturnsNullInsteadOfThrowing(string cursor)
    {
        // Cursor hỏng/bị sửa tay phải rơi về trang đầu, tuyệt đối không ném lỗi ra client.
        CursorCodec.TryDecode(cursor).Should().BeNull();
    }

    [Fact]
    public void TryDecode_EmptyGuidPayload_ReturnsNull()
    {
        var cursor = CursorCodec.Encode(Ts, Guid.Empty);

        CursorCodec.TryDecode(cursor).Should().BeNull();
    }

    [Fact]
    public void Encode_DifferentInputs_ProduceDifferentCursors()
    {
        var a = CursorCodec.Encode(Ts, Guid.NewGuid());
        var b = CursorCodec.Encode(Ts, Guid.NewGuid());

        a.Should().NotBe(b);
    }

    [Fact]
    public void Encode_SameInput_IsDeterministic()
    {
        var id = Guid.NewGuid();

        CursorCodec.Encode(Ts, id).Should().Be(CursorCodec.Encode(Ts, id));
    }

    [Fact]
    public void CursorKey_Equality_ComparesByValue()
    {
        var id = Guid.NewGuid();

        new CursorKey(Ts, id).Should().Be(new CursorKey(Ts, id));
        new CursorKey(Ts, id).Should().NotBe(new CursorKey(Ts.AddSeconds(1), id));
    }
}
