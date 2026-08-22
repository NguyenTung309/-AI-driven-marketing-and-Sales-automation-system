using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

// Hệ hợp đồng render nội dung: preset, template tin cậy, slot/theme, canonical JSON + hash, parser JSON đóng.
public sealed class ContentRenderSpecTests
{
    private const string TemplateId = "promo-card";
    private const int TemplateVersion = 1;
    // sha256 hợp lệ: 64 ký tự hex thường.
    private const string TemplateSha = "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    private static ContentVisualPreset Landscape => ContentVisualPreset.Landscape;

    private static TrustedTemplateCatalog BuildCatalog()
    {
        var identity = TrustedTemplateReference.Create(TemplateId, TemplateVersion, TemplateSha);
        var definition = TrustedTemplateDefinition.Create(
            identity,
            [ContentVisualPreset.Landscape, ContentVisualPreset.Square],
            [
                TrustedVisualSlotDefinition.Create("title", isRequired: true, maxLines: 2, maxGraphemesPerLine: 60),
                TrustedVisualSlotDefinition.Create("subtitle", isRequired: false, maxLines: 3, maxGraphemesPerLine: 80),
            ],
            [
                TrustedThemeTokenDefinition.Create("background", isRequired: false, ["light", "dark"]),
            ]);
        return new TrustedTemplateCatalog([definition]);
    }

    // ---------- ContentVisualPreset ----------

    [Theory]
    [InlineData("1200x630", 1200, 630)]
    [InlineData("1080x1080", 1080, 1080)]
    public void Preset_Parse_Known_ReturnsDimensions(string token, int w, int h)
    {
        var preset = ContentVisualPreset.Parse(token);
        preset.Width.Should().Be(w);
        preset.Height.Should().Be(h);
        preset.ToString().Should().Be(token);
    }

    [Fact]
    public void Preset_Parse_Unknown_Throws()
    {
        var act = () => ContentVisualPreset.Parse("999x999");
        act.Should().Throw<ContentVisualContractException>();
    }

    [Fact]
    public void Preset_TryParse_Null_ReturnsFalse()
    {
        ContentVisualPreset.TryParse(null, out var preset).Should().BeFalse();
        preset.Should().BeNull();
    }

    // ---------- TrustedTemplateReference ----------

    [Fact]
    public void TemplateReference_Create_Valid()
    {
        var reference = TrustedTemplateReference.Create(TemplateId, 2, TemplateSha);
        reference.TemplateId.Should().Be(TemplateId);
        reference.Version.Should().Be(2);
        reference.Sha256.Should().Be(TemplateSha);
    }

    [Theory]
    [InlineData("", 1, TemplateSha)]                  // id rỗng
    [InlineData("bad id!", 1, TemplateSha)]           // id ký tự lạ
    [InlineData(TemplateId, 0, TemplateSha)]          // version <= 0
    [InlineData(TemplateId, 1, "short")]              // hash sai độ dài
    [InlineData(TemplateId, 1, "ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ab")] // hash hoa (không hợp lệ)
    public void TemplateReference_Create_Invalid_Throws(string id, int version, string sha)
    {
        var act = () => TrustedTemplateReference.Create(id, version, sha);
        act.Should().Throw<ContentVisualContractException>();
    }

    // ---------- ContentVisualSlot ----------

    [Fact]
    public void Slot_Create_NormalizesLines()
    {
        var slot = ContentVisualSlot.Create("title", ["Hello", "World"]);
        slot.Name.Should().Be("title");
        slot.Lines.Should().Equal("Hello", "World");
    }

    [Fact]
    public void Slot_Create_NoLines_Throws()
    {
        var act = () => ContentVisualSlot.Create("title", []);
        act.Should().Throw<ContentVisualContractException>();
    }

    [Fact]
    public void Slot_Create_BlankLine_Throws()
    {
        var act = () => ContentVisualSlot.Create("title", ["  "]);
        act.Should().Throw<ContentVisualContractException>();
    }

    // ---------- ContentThemeTokenBinding ----------

    [Fact]
    public void ThemeBinding_Create_Valid()
    {
        var binding = ContentThemeTokenBinding.Create("background", "dark");
        binding.Name.Should().Be("background");
        binding.Token.Should().Be("dark");
    }

    [Fact]
    public void ThemeBinding_Create_TokenNotAllowed_Throws()
    {
        var act = () => ContentThemeTokenBinding.Create("background", "neon");
        act.Should().Throw<ContentVisualContractException>();
    }

    // ---------- Catalog ----------

    [Fact]
    public void Catalog_TryGetExact_MatchAndMismatch()
    {
        var catalog = BuildCatalog();

        catalog.TryGetExact(TemplateId, TemplateVersion, TemplateSha, out var definition).Should().BeTrue();
        definition!.Identity.TemplateId.Should().Be(TemplateId);

        // Sai hash → không khớp.
        catalog.TryGetExact(TemplateId, TemplateVersion, new string('0', 64), out _).Should().BeFalse();
        // Sai version → không khớp.
        catalog.TryGetExact(TemplateId, 99, TemplateSha, out _).Should().BeFalse();
    }

    [Fact]
    public void Catalog_DuplicateVersion_Throws()
    {
        var identity = TrustedTemplateReference.Create(TemplateId, 1, TemplateSha);
        var def1 = TrustedTemplateDefinition.Create(identity, [Landscape],
            [TrustedVisualSlotDefinition.Create("title", true, 2, 60)], []);
        var def2 = TrustedTemplateDefinition.Create(identity, [Landscape],
            [TrustedVisualSlotDefinition.Create("title", true, 2, 60)], []);

        var act = () => new TrustedTemplateCatalog([def1, def2]);
        act.Should().Throw<ContentVisualContractException>();
    }

    // ---------- ContentRenderSpec.Create ----------

    [Fact]
    public void Spec_Create_Valid_ProducesCanonicalJsonAndHash()
    {
        var catalog = BuildCatalog();
        var template = TrustedTemplateReference.Create(TemplateId, TemplateVersion, TemplateSha);

        var spec = ContentRenderSpec.Create(
            catalog, template, Landscape,
            [ContentVisualSlot.Create("title", ["Khai giảng"])],
            [ContentThemeTokenBinding.Create("background", "dark")]);

        spec.SchemaVersion.Should().Be(ContentRenderSpec.CurrentSchemaVersion);
        spec.CanonicalJson.Should().Contain("\"preset\":\"1200x630\"");
        spec.CanonicalSha256.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void Spec_Create_UntrustedTemplate_Throws()
    {
        var catalog = BuildCatalog();
        // Template không có trong catalog (id khác).
        var unknown = TrustedTemplateReference.Create("unknown-tpl", 1, TemplateSha);

        var act = () => ContentRenderSpec.Create(catalog, unknown, Landscape,
            [ContentVisualSlot.Create("title", ["x"])], []);
        act.Should().Throw<ContentVisualContractException>();
    }

    [Fact]
    public void Spec_Create_MissingRequiredSlot_Throws()
    {
        var catalog = BuildCatalog();
        var template = TrustedTemplateReference.Create(TemplateId, TemplateVersion, TemplateSha);

        // Thiếu slot "title" (required) — chỉ cấp subtitle.
        var act = () => ContentRenderSpec.Create(catalog, template, Landscape,
            [ContentVisualSlot.Create("subtitle", ["x"])], []);
        act.Should().Throw<ContentVisualContractException>();
    }

    [Fact]
    public void Spec_Create_UntrustedSlot_Throws()
    {
        var catalog = BuildCatalog();
        var template = TrustedTemplateReference.Create(TemplateId, TemplateVersion, TemplateSha);

        var act = () => ContentRenderSpec.Create(catalog, template, Landscape,
            [
                ContentVisualSlot.Create("title", ["x"]),
                ContentVisualSlot.Create("ghost", ["y"]),
            ], []);
        act.Should().Throw<ContentVisualContractException>();
    }

    // ---------- Canonicalizer ----------

    [Fact]
    public void Canonicalizer_SortsSlotsAndThemesDeterministically()
    {
        var catalog = BuildCatalog();
        var template = TrustedTemplateReference.Create(TemplateId, TemplateVersion, TemplateSha);

        var spec1 = ContentRenderSpec.Create(catalog, template, Landscape,
            [ContentVisualSlot.Create("subtitle", ["b"]), ContentVisualSlot.Create("title", ["a"])], []);
        var spec2 = ContentRenderSpec.Create(catalog, template, Landscape,
            [ContentVisualSlot.Create("title", ["a"]), ContentVisualSlot.Create("subtitle", ["b"])], []);

        // Thứ tự nhập khác nhau -> canonical JSON và hash giống nhau.
        ContentRenderSpecCanonicalizer.ComputeSha256(spec1)
            .Should().Be(ContentRenderSpecCanonicalizer.ComputeSha256(spec2));
    }

    [Fact]
    public void Canonicalizer_ComputeSlotsSha256_StableForSameSlots()
    {
        var slots = new[] { ContentVisualSlot.Create("title", ["a", "b"]) };

        ContentRenderSpecCanonicalizer.ComputeSlotsSha256(slots)
            .Should().Be(ContentRenderSpecCanonicalizer.ComputeSlotsSha256(slots));
    }

    // ---------- ContentRenderSpecJson ----------

    [Fact]
    public void Json_Parse_ValidSpec_RoundTripsToCanonical()
    {
        var catalog = BuildCatalog();
        var json = $$"""
        {
          "schemaVersion": 1,
          "preset": "1200x630",
          "template": { "id": "{{TemplateId}}", "version": 1, "sha256": "{{TemplateSha}}" },
          "slots": [ { "name": "title", "lines": ["Khai giảng"] } ],
          "themeTokens": [ { "name": "background", "token": "dark" } ]
        }
        """;

        var spec = ContentRenderSpecJson.Parse(json, catalog);

        spec.Template.TemplateId.Should().Be(TemplateId);
        spec.Slots.Should().ContainSingle(s => s.Name == "title");
        spec.ThemeTokens.Should().ContainSingle(t => t.Token == "dark");
    }

    [Theory]
    [InlineData("")]                                       // rỗng
    [InlineData("{ not json")]                               // JSON hỏng
    [InlineData("""{"schemaVersion":2,"preset":"1200x630","template":{},"slots":[],"themeTokens":[]}""")] // schema sai
    [InlineData("""{"schemaVersion":1,"preset":"1200x630","unknown":1}""")] // member lạ
    public void Json_Parse_Invalid_Throws(string json)
    {
        var act = () => ContentRenderSpecJson.Parse(json, BuildCatalog());
        act.Should().Throw<ContentVisualContractException>();
    }

    [Fact]
    public void Json_Parse_NullCatalog_Throws()
    {
        var act = () => ContentRenderSpecJson.Parse("{}", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Json_ParseSlots_Valid_ReturnsSlots()
    {
        var slots = ContentRenderSpecJson.ParseSlots("""[ { "name": "title", "lines": ["Hello"] } ]""");

        slots.Should().ContainSingle();
        slots[0].Name.Should().Be("title");
    }

    [Fact]
    public void Json_ParseSlots_NotArray_Throws()
    {
        var act = () => ContentRenderSpecJson.ParseSlots("""{ "name": "title" }""");
        act.Should().Throw<ContentVisualContractException>();
    }
}
