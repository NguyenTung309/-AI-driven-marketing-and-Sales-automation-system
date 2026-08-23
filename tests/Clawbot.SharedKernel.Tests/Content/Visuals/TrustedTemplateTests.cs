using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class TrustedTemplateDefinitionTests
{
    private static readonly string ValidHash = new('a', 64);

    private static TrustedTemplateReference MakeIdentity(string id = "card", int version = 1) =>
        TrustedTemplateReference.Create(id, version, ValidHash);

    [Fact]
    public void Create_ValidInput_SetsAllFields()
    {
        var identity = MakeIdentity();
        var slot = TrustedVisualSlotDefinition.Create("headline", true, 3, 80);
        var theme = TrustedThemeTokenDefinition.Create("background", false, ["light", "dark"]);

        var def = TrustedTemplateDefinition.Create(
            identity,
            [ContentVisualPreset.Landscape],
            [slot],
            [theme]);

        def.Identity.Should().Be(identity);
        def.Presets.Should().HaveCount(1);
        def.Slots.Should().HaveCount(1);
        def.ThemeTokens.Should().HaveCount(1);
    }

    [Fact]
    public void Supports_MatchingPreset_ReturnsTrue()
    {
        var def = TrustedTemplateDefinition.Create(
            MakeIdentity(),
            [ContentVisualPreset.Landscape, ContentVisualPreset.Square],
            [TrustedVisualSlotDefinition.Create("h", true, 2, 50)],
            []);

        def.Supports(ContentVisualPreset.Landscape).Should().BeTrue();
        def.Supports(ContentVisualPreset.Square).Should().BeTrue();
    }

    [Fact]
    public void TryGetSlot_ExistingName_ReturnsTrue()
    {
        var def = TrustedTemplateDefinition.Create(
            MakeIdentity(),
            [ContentVisualPreset.Landscape],
            [TrustedVisualSlotDefinition.Create("title", true, 2, 50)],
            []);

        def.TryGetSlot("title", out var slot).Should().BeTrue();
        slot!.Name.Should().Be("title");
    }

    [Fact]
    public void TryGetSlot_MissingName_ReturnsFalse()
    {
        var def = TrustedTemplateDefinition.Create(
            MakeIdentity(),
            [ContentVisualPreset.Landscape],
            [TrustedVisualSlotDefinition.Create("title", true, 2, 50)],
            []);

        def.TryGetSlot("missing", out _).Should().BeFalse();
    }
}

public sealed class TrustedVisualSlotDefinitionTests
{
    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var def = TrustedVisualSlotDefinition.Create("headline", true, 4, 100);

        def.Name.Should().Be("headline");
        def.IsRequired.Should().BeTrue();
        def.MaxLines.Should().Be(4);
        def.MaxGraphemesPerLine.Should().Be(100);
    }

    [Fact]
    public void Create_ZeroMaxLines_Throws()
    {
        var act = () => TrustedVisualSlotDefinition.Create("h", true, 0, 50);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*slot_line_limit_invalid*");
    }

    [Fact]
    public void Create_ZeroMaxGraphemes_Throws()
    {
        var act = () => TrustedVisualSlotDefinition.Create("h", true, 2, 0);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*line_grapheme_limit_invalid*");
    }
}

public sealed class TrustedThemeTokenDefinitionTests
{
    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var def = TrustedThemeTokenDefinition.Create("background", true, ["light", "dark"]);

        def.Name.Should().Be("background");
        def.IsRequired.Should().BeTrue();
        def.AllowedTokens.Should().Equal("dark", "light"); // sorted
    }

    [Fact]
    public void Create_EmptyTokens_Throws()
    {
        var act = () => TrustedThemeTokenDefinition.Create("bg", false, []);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*theme_definition_tokens_required*");
    }

    [Fact]
    public void Create_DisallowedToken_Throws()
    {
        var act = () => TrustedThemeTokenDefinition.Create("bg", false, ["custom"]);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*theme_token_not_allowed*");
    }

    [Fact]
    public void Allows_MatchingToken_ReturnsTrue()
    {
        var def = TrustedThemeTokenDefinition.Create("bg", false, ["light", "dark"]);

        def.Allows("light").Should().BeTrue();
        def.Allows("brand").Should().BeFalse();
    }
}

public sealed class TrustedTemplateCatalogTests
{
    private static readonly string ValidHash = new('a', 64);

    [Fact]
    public void TryGetExact_MatchFound_ReturnsTrue()
    {
        var identity = TrustedTemplateReference.Create("card", 1, ValidHash);
        var def = TrustedTemplateDefinition.Create(
            identity,
            [ContentVisualPreset.Landscape],
            [TrustedVisualSlotDefinition.Create("h", true, 2, 50)],
            []);
        var catalog = new TrustedTemplateCatalog([def]);

        catalog.TryGetExact("card", 1, ValidHash, out var found).Should().BeTrue();
        found.Should().Be(def);
    }

    [Fact]
    public void TryGetExact_WrongHash_ReturnsFalse()
    {
        var identity = TrustedTemplateReference.Create("card", 1, ValidHash);
        var def = TrustedTemplateDefinition.Create(
            identity,
            [ContentVisualPreset.Landscape],
            [TrustedVisualSlotDefinition.Create("h", true, 2, 50)],
            []);
        var catalog = new TrustedTemplateCatalog([def]);

        catalog.TryGetExact("card", 1, new string('b', 64), out _).Should().BeFalse();
    }

    [Fact]
    public void Definitions_ReturnsSortedDefinitions()
    {
        var id1 = TrustedTemplateReference.Create("alpha", 1, ValidHash);
        var id2 = TrustedTemplateReference.Create("beta", 1, ValidHash);
        var d1 = TrustedTemplateDefinition.Create(id1, [ContentVisualPreset.Landscape],
            [TrustedVisualSlotDefinition.Create("h", true, 2, 50)], []);
        var d2 = TrustedTemplateDefinition.Create(id2, [ContentVisualPreset.Landscape],
            [TrustedVisualSlotDefinition.Create("h", true, 2, 50)], []);

        var catalog = new TrustedTemplateCatalog([d2, d1]);

        catalog.Definitions[0].Identity.TemplateId.Should().Be("alpha");
        catalog.Definitions[1].Identity.TemplateId.Should().Be("beta");
    }
}
