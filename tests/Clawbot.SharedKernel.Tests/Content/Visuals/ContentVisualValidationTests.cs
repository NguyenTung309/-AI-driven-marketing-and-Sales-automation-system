using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class ContentVisualValidationTests
{
    [Theory]
    [InlineData("valid-name")]
    [InlineData("a")]
    [InlineData("abc123")]
    [InlineData("with_underscore")]
    [InlineData("with.dot")]
    public void ValidateIdentifier_Valid_ReturnsValue(string value)
    {
        var result = ContentVisualValidation.ValidateIdentifier(value, "$");
        result.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("special!char")]
    [InlineData("-starts-with-dash")]
    public void ValidateIdentifier_Invalid_Throws(string? value)
    {
        var act = () => ContentVisualValidation.ValidateIdentifier(value, "$.test");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*identifier_invalid*");
    }

    [Fact]
    public void ValidateSha256_ValidHash_ReturnsValue()
    {
        var hash = new string('a', 64);
        ContentVisualValidation.ValidateSha256(hash, "$").Should().Be(hash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")] // uppercase
    public void ValidateSha256_Invalid_Throws(string? value)
    {
        var act = () => ContentVisualValidation.ValidateSha256(value, "$.hash");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*template_hash_invalid*");
    }

    [Fact]
    public void NormalizeLine_ValidText_ReturnsNormalized()
    {
        var result = ContentVisualValidation.NormalizeLine("Hello world", 120, "$");
        result.Should().Be("Hello world");
    }

    [Fact]
    public void NormalizeLine_BlankText_Throws()
    {
        var act = () => ContentVisualValidation.NormalizeLine("   ", 120, "$");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*text_required*");
    }

    [Fact]
    public void CountGraphemes_AsciiString_ReturnsLength()
    {
        ContentVisualValidation.CountGraphemes("hello").Should().Be(5);
    }

    [Fact]
    public void CopyBounded_UnderLimit_ReturnsArray()
    {
        var items = new[] { 1, 2, 3 };
        var result = ContentVisualValidation.CopyBounded(items, 5, "overflow", "$");

        result.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void CopyBounded_ExceedsLimit_Throws()
    {
        var items = new[] { 1, 2, 3 };
        var act = () => ContentVisualValidation.CopyBounded(items, 2, "overflow", "$");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*overflow*");
    }
}
