using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class ContentVisualSlotTests
{
    [Fact]
    public void Create_ValidInput_SetsNameAndLines()
    {
        var slot = ContentVisualSlot.Create("headline", ["Line one", "Line two"]);

        slot.Name.Should().Be("headline");
        slot.Lines.Should().Equal("Line one", "Line two");
    }

    [Fact]
    public void Create_EmptyLines_Throws()
    {
        var act = () => ContentVisualSlot.Create("headline", []);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*slot_line_limit_exceeded*");
    }

    [Fact]
    public void Create_BlankLine_Throws()
    {
        var act = () => ContentVisualSlot.Create("headline", ["  "]);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*text_required*");
    }

    [Fact]
    public void Create_InvalidName_Throws()
    {
        var act = () => ContentVisualSlot.Create("invalid name!", ["ok"]);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*identifier_invalid*");
    }
}

public sealed class ContentThemeTokenBindingTests
{
    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var binding = ContentThemeTokenBinding.Create("background", "dark");

        binding.Name.Should().Be("background");
        binding.Token.Should().Be("dark");
    }

    [Fact]
    public void Create_DisallowedToken_Throws()
    {
        var act = () => ContentThemeTokenBinding.Create("bg", "custom");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*theme_token_not_allowed*");
    }

    [Fact]
    public void Create_InvalidName_Throws()
    {
        var act = () => ContentThemeTokenBinding.Create("", "light");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*identifier_invalid*");
    }
}

public sealed class TrustedTemplateReferenceTests
{
    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var reference = TrustedTemplateReference.Create("card-v1", 1, ValidHash);

        reference.TemplateId.Should().Be("card-v1");
        reference.Version.Should().Be(1);
        reference.Sha256.Should().Be(ValidHash);
    }

    [Fact]
    public void Create_ZeroVersion_Throws()
    {
        var act = () => TrustedTemplateReference.Create("t", 0, ValidHash);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*template_version_invalid*");
    }

    [Fact]
    public void Create_InvalidHash_Throws()
    {
        var act = () => TrustedTemplateReference.Create("t", 1, "short");

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*template_hash_invalid*");
    }

    [Fact]
    public void Create_UppercaseHash_Throws()
    {
        var upperHash = new string('A', 64);
        var act = () => TrustedTemplateReference.Create("t", 1, upperHash);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*template_hash_invalid*");
    }
}
