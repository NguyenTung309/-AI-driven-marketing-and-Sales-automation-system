using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class ContentVisualPresetTests
{
    [Fact]
    public void Landscape_HasExpectedDimensions()
    {
        var preset = ContentVisualPreset.Landscape;

        preset.Token.Should().Be("1200x630");
        preset.Width.Should().Be(1200);
        preset.Height.Should().Be(630);
    }

    [Fact]
    public void Square_HasExpectedDimensions()
    {
        var preset = ContentVisualPreset.Square;

        preset.Token.Should().Be("1080x1080");
        preset.Width.Should().Be(1080);
        preset.Height.Should().Be(1080);
    }

    [Fact]
    public void Parse_ValidToken_ReturnsPreset()
    {
        var preset = ContentVisualPreset.Parse("1200x630");

        preset.Should().Be(ContentVisualPreset.Landscape);
    }

    [Fact]
    public void Parse_InvalidToken_Throws()
    {
        var act = () => ContentVisualPreset.Parse("999x999");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*preset_not_allowed*");
    }

    [Fact]
    public void TryParse_NullToken_ReturnsFalse()
    {
        ContentVisualPreset.TryParse(null, out var preset).Should().BeFalse();
        preset.Should().BeNull();
    }

    [Fact]
    public void Supported_ContainsBothPresets()
    {
        ContentVisualPreset.Supported.Should().HaveCount(2);
    }

    [Fact]
    public void ToString_ReturnsToken()
    {
        ContentVisualPreset.Landscape.ToString().Should().Be("1200x630");
    }
}

public sealed class TrustedThemeTokenCatalogTests
{
    [Theory]
    [InlineData("light", true)]
    [InlineData("dark", true)]
    [InlineData("brand", true)]
    [InlineData("custom", false)]
    [InlineData(null, false)]
    public void IsAllowed_ReturnsExpected(string? token, bool expected)
    {
        TrustedThemeTokenCatalog.IsAllowed(token).Should().Be(expected);
    }

    [Fact]
    public void Allowed_ContainsThreeTokens()
    {
        TrustedThemeTokenCatalog.Allowed.Should().HaveCount(3);
    }
}
