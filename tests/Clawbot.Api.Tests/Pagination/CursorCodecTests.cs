using Clawbot.Api.Common.Pagination;
using FluentAssertions;

namespace Clawbot.Api.Tests.Pagination;

public sealed class CursorCodecTests
{
    [Fact]
    public void Encode_Decode_Roundtrips()
    {
        var ts = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var cursor = CursorCodec.Encode(ts, id);
        cursor.Should().NotBeNullOrWhiteSpace();
        cursor.Should().NotContain("+").And.NotContain("/").And.NotContain("=");

        var key = CursorCodec.TryDecode(cursor);
        key.Should().NotBeNull();
        key!.Value.Ts.Should().Be(ts);
        key.Value.Id.Should().Be(id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!!")]
    [InlineData("eyJ0cyI6ImJhZCIsImlkIjoieCJ9")] // valid b64 but bad payload
    public void TryDecode_Invalid_ReturnsNull(string? cursor)
    {
        CursorCodec.TryDecode(cursor).Should().BeNull();
    }

    [Fact]
    public void TryDecode_TamperedPayload_ReturnsNull()
    {
        var good = CursorCodec.Encode(DateTimeOffset.UtcNow, Guid.NewGuid());
        var tampered = good[..^2] + "xx";
        CursorCodec.TryDecode(tampered).Should().BeNull();
    }
}
