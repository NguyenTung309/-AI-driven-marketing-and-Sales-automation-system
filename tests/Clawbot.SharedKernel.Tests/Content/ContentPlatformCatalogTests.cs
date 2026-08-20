using Clawbot.SharedKernel.Content;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content;

public sealed class ContentPlatformCatalogTests
{
    [Theory]
    [InlineData("facebook", true, "facebook")]
    [InlineData("  Zalo  ", true, "zalo")]
    [InlineData("INSTAGRAM", true, "instagram")]
    [InlineData("tiktok", false, null)]
    [InlineData("", false, null)]
    [InlineData(null, false, null)]
    public void TryNormalizeWritable_ReturnsExpected(string? input, bool expected, string? expectedNormalized)
    {
        var result = ContentPlatformCatalog.TryNormalizeWritable(input, out var normalized);

        result.Should().Be(expected);
        if (expected)
            normalized.Should().Be(expectedNormalized);
        else
            normalized.Should().BeNull();
    }

    [Fact]
    public void NormalizeWritable_ValidPlatforms_ReturnsDistinct()
    {
        var result = ContentPlatformCatalog.NormalizeWritable(["facebook", "ZALO", "facebook"]);

        result.Should().Equal("facebook", "zalo");
    }

    [Fact]
    public void NormalizeWritable_EmptyList_Throws()
    {
        var act = () => ContentPlatformCatalog.NormalizeWritable([]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void NormalizeWritable_UnsupportedPlatform_Throws()
    {
        var act = () => ContentPlatformCatalog.NormalizeWritable(["tiktok"]);

        act.Should().Throw<ArgumentException>().WithMessage("*unsupported*");
    }

    [Fact]
    public void Writable_ContainsThreePlatforms()
    {
        ContentPlatformCatalog.Writable.Should().HaveCount(3);
        ContentPlatformCatalog.Writable.Should().Contain("facebook");
    }

    [Fact]
    public void ContentPlatform_DelegatesToCatalog()
    {
        ContentPlatform.Writable.Should().Equal(ContentPlatformCatalog.Writable);
        ContentPlatform.TryNormalizeWritable("facebook", out var n).Should().BeTrue();
        n.Should().Be("facebook");
    }
}
