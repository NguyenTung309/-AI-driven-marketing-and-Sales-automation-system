using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class ContentRenderSpecCanonicalizerTests
{
    private static readonly string ValidHash = new('a', 64);

    private static ContentRenderSpec BuildMinimalSpec()
    {
        var identity = TrustedTemplateReference.Create("card", 1, ValidHash);
        var slotDef = TrustedVisualSlotDefinition.Create("headline", true, 3, 80);
        var themeDef = TrustedThemeTokenDefinition.Create("background", false, ["light", "dark"]);
        var template = TrustedTemplateDefinition.Create(
            identity, [ContentVisualPreset.Landscape], [slotDef], [themeDef]);
        var catalog = new TrustedTemplateCatalog([template]);

        var slot = ContentVisualSlot.Create("headline", ["Hello world"]);
        var theme = ContentThemeTokenBinding.Create("background", "dark");

        return ContentRenderSpec.Create(catalog, identity, ContentVisualPreset.Landscape, [slot], [theme]);
    }

    [Fact]
    public void ToCanonicalJson_ProducesDeterministicOutput()
    {
        var spec = BuildMinimalSpec();

        var json1 = ContentRenderSpecCanonicalizer.ToCanonicalJson(spec);
        var json2 = ContentRenderSpecCanonicalizer.ToCanonicalJson(spec);

        json1.Should().Be(json2);
    }

    [Fact]
    public void ComputeSha256_ReturnsLowercaseHex()
    {
        var spec = BuildMinimalSpec();

        var hash = ContentRenderSpecCanonicalizer.ComputeSha256(spec);

        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void GetCanonicalUtf8_NullSpec_Throws()
    {
        var act = () => ContentRenderSpecCanonicalizer.GetCanonicalUtf8(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToCanonicalSlotsJson_SortsByName()
    {
        var slotB = ContentVisualSlot.Create("beta", ["line"]);
        var slotA = ContentVisualSlot.Create("alpha", ["line"]);

        var json = ContentRenderSpecCanonicalizer.ToCanonicalSlotsJson([slotB, slotA]);

        var alphaIndex = json.IndexOf("alpha", StringComparison.Ordinal);
        var betaIndex = json.IndexOf("beta", StringComparison.Ordinal);
        alphaIndex.Should().BeLessThan(betaIndex);
    }

    [Fact]
    public void ComputeSlotsSha256_ReturnsConsistentHash()
    {
        var slots = new[] { ContentVisualSlot.Create("h", ["text"]) };

        var h1 = ContentRenderSpecCanonicalizer.ComputeSlotsSha256(slots);
        var h2 = ContentRenderSpecCanonicalizer.ComputeSlotsSha256(slots);

        h1.Should().Be(h2);
    }
}
