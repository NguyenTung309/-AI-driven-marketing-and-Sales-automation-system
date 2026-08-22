using System.Text.Json;
using Clawbot.Api.Contracts.Content;
using Clawbot.Api.Endpoints;
using Clawbot.SharedKernel.Content;
using FluentAssertions;

namespace Clawbot.Api.Tests;

public sealed class ContentAssetsJsonTests
{
    private static string AssetJson(
        string url = "https://cdn.example.test/a.png",
        string type = "image",
        string? fileName = "a.png",
        string? contentType = "image/png") =>
        JsonSerializer.Serialize(new
        {
            type,
            url,
            fileName,
            contentType,
        });

    [Fact]
    public void TryNormalizeAssetsJson_BlankInput_ReturnsEmptyArray()
    {
        ContentEndpoints.TryNormalizeAssetsJson("", out var normalized).Should().BeTrue();
        normalized.Should().Be("[]");

        ContentEndpoints.TryNormalizeAssetsJson("   ", out var blank).Should().BeTrue();
        blank.Should().Be("[]");
    }

    [Fact]
    public void TryNormalizeAssetsJson_ValidAsset_KeepsFields()
    {
        var ok = ContentEndpoints.TryNormalizeAssetsJson($"[{AssetJson()}]", out var normalized);

        ok.Should().BeTrue();
        normalized.Should().Contain("\"type\":\"image\"");
        normalized.Should().Contain("https://cdn.example.test/a.png");
        normalized.Should().Contain("\"contentType\":\"image/png\"");
    }

    [Fact]
    public void TryNormalizeAssetsJson_StripsDirectoryFromFileName()
    {
        // Chống path traversal: chỉ giữ tên file, bỏ mọi thành phần thư mục.
        var json = $"[{AssetJson(fileName: "../../etc/passwd.png")}]";

        ContentEndpoints.TryNormalizeAssetsJson(json, out var normalized).Should().BeTrue();

        normalized.Should().Contain("passwd.png");
        normalized.Should().NotContain("..");
    }

    [Fact]
    public void TryNormalizeAssetsJson_NotAnArray_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson("{\"type\":\"image\"}", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_MalformedJson_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson("[{not json", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_NonObjectElement_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson("[\"just-a-string\"]", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_NonImageType_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson($"[{AssetJson(type: "video")}]", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_MissingType_DefaultsToImage()
    {
        var json = """[{"url":"https://cdn.example.test/a.png"}]""";

        ContentEndpoints.TryNormalizeAssetsJson(json, out var normalized).Should().BeTrue();
        normalized.Should().Contain("\"type\":\"image\"");
    }

    [Fact]
    public void TryNormalizeAssetsJson_MissingUrl_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson("""[{"type":"image"}]""", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_NonStringUrl_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson("""[{"type":"image","url":123}]""", out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("ftp://cdn.example.test/a.png")]
    [InlineData("file:///c:/windows/system32/a.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/relative/path.png")]
    [InlineData("")]
    public void TryNormalizeAssetsJson_DisallowedUrlScheme_ReturnsFalse(string url)
    {
        ContentEndpoints.TryNormalizeAssetsJson($"[{AssetJson(url: url)}]", out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("https://cdn.example.test/a.png")]
    [InlineData("http://cdn.example.test/a.png")]
    public void TryNormalizeAssetsJson_HttpAndHttpsAreAllowed(string url)
    {
        ContentEndpoints.TryNormalizeAssetsJson($"[{AssetJson(url: url)}]", out _)
            .Should().BeTrue();
    }

    [Fact]
    public void TryNormalizeAssetsJson_UrlOverLengthCap_ReturnsFalse()
    {
        var longUrl = "https://cdn.example.test/" + new string('a', 2048);

        ContentEndpoints.TryNormalizeAssetsJson($"[{AssetJson(url: longUrl)}]", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_DisallowedContentType_ReturnsFalse()
    {
        ContentEndpoints.TryNormalizeAssetsJson(
            $"[{AssetJson(contentType: "application/pdf")}]", out _).Should().BeFalse();
    }

    [Fact]
    public void TryNormalizeAssetsJson_NullContentType_IsAccepted()
    {
        ContentEndpoints.TryNormalizeAssetsJson(
            $"[{AssetJson(contentType: null)}]", out _).Should().BeTrue();
    }

    [Fact]
    public void TryNormalizeAssetsJson_AtCapLimit_IsAccepted()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat(AssetJson(), 10)) + "]";

        ContentEndpoints.TryNormalizeAssetsJson(json, out _).Should().BeTrue();
    }

    [Fact]
    public void TryNormalizeAssetsJson_OverCapLimit_ReturnsFalse()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat(AssetJson(), 11)) + "]";

        ContentEndpoints.TryNormalizeAssetsJson(json, out _).Should().BeFalse();
    }

    [Fact]
    public void AddImageAsset_AppendsToExistingArray()
    {
        var existing = $"[{AssetJson(url: "https://cdn.example.test/first.png")}]";

        var result = ContentEndpoints.AddImageAsset(
            existing, "https://cdn.example.test/second.png", "second.png", "image/png");

        var array = JsonSerializer.Deserialize<JsonElement>(result);
        array.GetArrayLength().Should().Be(2);
        result.Should().Contain("first.png").And.Contain("second.png");
    }

    [Fact]
    public void AddImageAsset_EmptyExisting_CreatesSingleEntry()
    {
        var result = ContentEndpoints.AddImageAsset(
            "", "https://cdn.example.test/a.png", "a.png", "image/png");

        JsonSerializer.Deserialize<JsonElement>(result).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AddImageAsset_CorruptExisting_DiscardsAndStartsFresh()
    {
        // Assets cũ hỏng thì bỏ hẳn chứ không kéo dữ liệu rác sang bản mới.
        var result = ContentEndpoints.AddImageAsset(
            "[{broken", "https://cdn.example.test/a.png", "a.png", "image/png");

        JsonSerializer.Deserialize<JsonElement>(result).GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void AddImageAsset_BlankFileNameAndContentType_WriteNull()
    {
        var result = ContentEndpoints.AddImageAsset(
            "", "https://cdn.example.test/a.png", "  ", "  ");

        result.Should().Contain("\"fileName\":null");
        result.Should().Contain("\"contentType\":null");
    }
}

public sealed class ContentAssetContentTypeTests
{
    private static readonly byte[] PngMagic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF, 0xE0, 0x00];
    private static readonly byte[] Gif87Magic = [0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0x00];
    private static readonly byte[] Gif89Magic = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x00];
    private static readonly byte[] WebpMagic =
        [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

    [Fact]
    public void LooksLikeAllowedImage_MatchingMagicBytes_ReturnsTrue()
    {
        ContentEndpoints.LooksLikeAllowedImage(PngMagic, "image/png").Should().BeTrue();
        ContentEndpoints.LooksLikeAllowedImage(JpegMagic, "image/jpeg").Should().BeTrue();
        ContentEndpoints.LooksLikeAllowedImage(Gif87Magic, "image/gif").Should().BeTrue();
        ContentEndpoints.LooksLikeAllowedImage(Gif89Magic, "image/gif").Should().BeTrue();
        ContentEndpoints.LooksLikeAllowedImage(WebpMagic, "image/webp").Should().BeTrue();
    }

    [Fact]
    public void LooksLikeAllowedImage_IsCaseInsensitiveOnContentType()
    {
        ContentEndpoints.LooksLikeAllowedImage(PngMagic, "IMAGE/PNG").Should().BeTrue();
    }

    [Fact]
    public void LooksLikeAllowedImage_MismatchedMagicBytes_ReturnsFalse()
    {
        // File tự nhận là PNG nhưng bytes là JPEG — phải từ chối.
        ContentEndpoints.LooksLikeAllowedImage(JpegMagic, "image/png").Should().BeFalse();
    }

    [Fact]
    public void LooksLikeAllowedImage_TruncatedBytes_ReturnsFalse()
    {
        ContentEndpoints.LooksLikeAllowedImage([0x89, 0x50], "image/png").Should().BeFalse();
        ContentEndpoints.LooksLikeAllowedImage([0xFF], "image/jpeg").Should().BeFalse();
        ContentEndpoints.LooksLikeAllowedImage([0x47, 0x49], "image/gif").Should().BeFalse();
        ContentEndpoints.LooksLikeAllowedImage([0x52, 0x49], "image/webp").Should().BeFalse();
    }

    [Fact]
    public void LooksLikeAllowedImage_UnknownOrNullContentType_ReturnsFalse()
    {
        ContentEndpoints.LooksLikeAllowedImage(PngMagic, "image/bmp").Should().BeFalse();
        ContentEndpoints.LooksLikeAllowedImage(PngMagic, null).Should().BeFalse();
    }

    [Fact]
    public void LooksLikeAllowedImage_GifWithWrongVersionByte_ReturnsFalse()
    {
        byte[] gifBad = [0x47, 0x49, 0x46, 0x38, 0x31, 0x61, 0x00];

        ContentEndpoints.LooksLikeAllowedImage(gifBad, "image/gif").Should().BeFalse();
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("IMAGE/PNG")]
    [InlineData("image/png; charset=binary")]
    public void ResolveAssetContentType_DeclaredTypeMatchingBytes_ReturnsNormalizedType(string supplied)
    {
        ContentEndpoints.ResolveAssetContentType(PngMagic, supplied).Should().Be("image/png");
    }

    [Fact]
    public void ResolveAssetContentType_ImageJpgAliasNormalizesToJpeg()
    {
        ContentEndpoints.ResolveAssetContentType(JpegMagic, "image/jpg").Should().Be("image/jpeg");
    }

    [Fact]
    public void ResolveAssetContentType_DeclaredTypeContradictsBytes_ReturnsNull()
    {
        ContentEndpoints.ResolveAssetContentType(JpegMagic, "image/png").Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("application/octet-stream")]
    public void ResolveAssetContentType_UnknownDeclaredType_SniffsFromBytes(string? supplied)
    {
        ContentEndpoints.ResolveAssetContentType(PngMagic, supplied).Should().Be("image/png");
        ContentEndpoints.ResolveAssetContentType(JpegMagic, supplied).Should().Be("image/jpeg");
        ContentEndpoints.ResolveAssetContentType(WebpMagic, supplied).Should().Be("image/webp");
    }

    [Fact]
    public void ResolveAssetContentType_UnsupportedDeclaredType_ReturnsNull()
    {
        // application/pdf không nằm trong allowlist -> từ chối luôn, không sniff.
        ContentEndpoints.ResolveAssetContentType(PngMagic, "application/pdf").Should().BeNull();
    }

    [Fact]
    public void ResolveAssetContentType_UnrecognizedBytes_ReturnsNull()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];

        ContentEndpoints.ResolveAssetContentType(garbage, null).Should().BeNull();
    }
}

public sealed class ContentScheduleResolutionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 3, 0, 0, TimeSpan.Zero);

    private static Clawbot.Domain.Content.ContentItem Item() =>
        Clawbot.Domain.Content.ContentItem.Create(
            Guid.NewGuid(), "facebook", "nội dung", Guid.NewGuid(), Now.AddHours(-1));

    [Fact]
    public void ResolveScheduledAt_FutureExplicitTime_IsAccepted()
    {
        var at = Now.AddHours(3);

        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(at), Item(), Now, new DefaultGoldenHourResolver());

        result.ScheduledAt.Should().Be(at);
        result.ErrorCode.Should().BeNull();
        result.Message.Should().BeNull();
    }

    [Fact]
    public void ResolveScheduledAt_PastTime_IsRejected()
    {
        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(Now.AddHours(-1)),
            Item(),
            Now,
            new DefaultGoldenHourResolver());

        result.ErrorCode.Should().Be("content.schedule_in_past");
        result.Message.Should().Contain("future");
    }

    [Fact]
    public void ResolveScheduledAt_ExactlyNow_IsRejected()
    {
        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(Now), Item(), Now, new DefaultGoldenHourResolver());

        result.ErrorCode.Should().Be("content.schedule_in_past");
    }

    [Fact]
    public void ResolveScheduledAt_NoExplicitTime_FallsBackToGoldenHour()
    {
        var result = ContentEndpoints.ResolveScheduledAt(
            new ScheduleContentItemRequest(null), Item(), Now, new DefaultGoldenHourResolver());

        result.ErrorCode.Should().BeNull();
        result.ScheduledAt.Should().BeAfter(Now);
        // Golden hour của facebook là 20:30 giờ VN.
        result.ScheduledAt.ToOffset(TimeSpan.FromHours(7)).TimeOfDay
            .Should().Be(new TimeSpan(20, 30, 0));
    }
}

public sealed class ContentPostPerformanceWindowTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 7)]
    [InlineData(90, 90)]
    public void NormalizePostPerformanceWindowDays_InRange_IsKept(int input, int expected)
    {
        ContentEndpoints.NormalizePostPerformanceWindowDays(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(91)]
    [InlineData(int.MaxValue)]
    public void NormalizePostPerformanceWindowDays_OutOfRange_FallsBackTo30(int? input)
    {
        ContentEndpoints.NormalizePostPerformanceWindowDays(input).Should().Be(30);
    }
}
