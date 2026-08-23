using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class ContentRenderSpecJsonTests
{
    private const string TemplateId = "card";
    private static readonly string TemplateHash = new('a', 64);

    private static readonly ITrustedTemplateCatalog Catalog = new TrustedTemplateCatalog(
    [
        TrustedTemplateDefinition.Create(
            TrustedTemplateReference.Create(TemplateId, 1, TemplateHash),
            [ContentVisualPreset.Landscape, ContentVisualPreset.Square],
            [
                TrustedVisualSlotDefinition.Create("headline", true, 3, 80),
                TrustedVisualSlotDefinition.Create("subhead", false, 2, 60),
            ],
            [TrustedThemeTokenDefinition.Create("background", false, ["light", "dark"])]),
    ]);

    private static string ValidJson(
        string? preset = null,
        string? slots = null,
        string? themeTokens = null,
        int schemaVersion = 1) =>
        $$"""
        {
          "schemaVersion": {{schemaVersion}},
          "preset": "{{preset ?? ContentVisualPreset.Landscape.Token}}",
          "template": { "id": "{{TemplateId}}", "version": 1, "sha256": "{{TemplateHash}}" },
          "slots": {{slots ?? """[{ "name": "headline", "lines": ["Khai giảng lớp 12"] }]"""}},
          "themeTokens": {{themeTokens ?? "[]"}}
        }
        """;

    [Fact]
    public void Parse_ValidJson_ReturnsSpec()
    {
        var spec = ContentRenderSpecJson.Parse(ValidJson(), Catalog);

        spec.SchemaVersion.Should().Be(ContentRenderSpec.CurrentSchemaVersion);
        spec.Preset.Should().Be(ContentVisualPreset.Landscape);
        spec.Template.TemplateId.Should().Be(TemplateId);
        spec.Slots.Should().ContainSingle(slot => slot.Name == "headline");
    }

    [Fact]
    public void Parse_WithThemeTokens_BindsTokens()
    {
        var spec = ContentRenderSpecJson.Parse(
            ValidJson(themeTokens: """[{ "name": "background", "token": "dark" }]"""),
            Catalog);

        spec.ThemeTokens.Should().ContainSingle();
        spec.ThemeTokens[0].Token.Should().Be("dark");
    }

    [Fact]
    public void Parse_SortsSlotsByName()
    {
        var json = ValidJson(slots: """
            [
              { "name": "subhead", "lines": ["Phụ đề"] },
              { "name": "headline", "lines": ["Tiêu đề"] }
            ]
            """);

        var spec = ContentRenderSpecJson.Parse(json, Catalog);

        spec.Slots.Select(slot => slot.Name).Should().Equal("headline", "subhead");
    }

    [Fact]
    public void Parse_NullCatalog_Throws()
    {
        var act = () => ContentRenderSpecJson.Parse(ValidJson(), null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_BlankJson_ThrowsInvalidJson(string? json)
    {
        var act = () => ContentRenderSpecJson.Parse(json!, Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*invalid_json*");
    }

    [Theory]
    [InlineData("{ not json }")]
    [InlineData("{ \"schemaVersion\": 1, }")]
    [InlineData("{ /* comment */ }")]
    public void Parse_MalformedJson_ThrowsInvalidJson(string json)
    {
        var act = () => ContentRenderSpecJson.Parse(json, Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*invalid_json*");
    }

    [Fact]
    public void Parse_OversizedJson_ThrowsSizeExceeded()
    {
        var padding = new string('x', ContentVisualLimits.MaximumJsonUtf8Bytes + 16);
        var act = () => ContentRenderSpecJson.Parse($"{{\"pad\":\"{padding}\"}}", Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*json_size_exceeded*");
    }

    [Fact]
    public void Parse_UnsupportedSchemaVersion_Throws()
    {
        var act = () => ContentRenderSpecJson.Parse(ValidJson(schemaVersion: 99), Catalog);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*schema_version_unsupported*");
    }

    [Fact]
    public void Parse_UnknownRootMember_Throws()
    {
        var json = ValidJson().TrimEnd().TrimEnd('}') + ", \"extra\": 1 }";

        var act = () => ContentRenderSpecJson.Parse(json, Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*unknown_member*");
    }

    [Fact]
    public void Parse_DuplicateMember_Throws()
    {
        var json = ValidJson().TrimEnd().TrimEnd('}') + ", \"preset\": \"square\" }";

        var act = () => ContentRenderSpecJson.Parse(json, Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*duplicate_member*");
    }

    [Fact]
    public void Parse_MissingRequiredMember_Throws()
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "template": { "id": "{{TemplateId}}", "version": 1, "sha256": "{{TemplateHash}}" },
              "slots": [],
              "themeTokens": []
            }
            """;

        var act = () => ContentRenderSpecJson.Parse(json, Catalog);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*required_member_missing*");
    }

    [Fact]
    public void Parse_WrongMemberType_Throws()
    {
        var act = () => ContentRenderSpecJson.Parse(ValidJson(slots: "\"not-an-array\""), Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*member_type_invalid*");
    }

    [Fact]
    public void Parse_NonIntegerTemplateVersion_Throws()
    {
        var json = $$"""
            {
              "schemaVersion": 1,
              "preset": "{{ContentVisualPreset.Landscape.Token}}",
              "template": { "id": "{{TemplateId}}", "version": "one", "sha256": "{{TemplateHash}}" },
              "slots": [{ "name": "headline", "lines": ["A"] }],
              "themeTokens": []
            }
            """;

        var act = () => ContentRenderSpecJson.Parse(json, Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*member_type_invalid*");
    }

    [Fact]
    public void Parse_EmptySlotLines_Throws()
    {
        var act = () => ContentRenderSpecJson.Parse(
            ValidJson(slots: """[{ "name": "headline", "lines": [] }]"""),
            Catalog);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*slot_line_limit_exceeded*");
    }

    [Fact]
    public void Parse_TooManySlotLines_Throws()
    {
        var lines = string.Join(",", Enumerable.Repeat("\"dòng\"", ContentVisualLimits.MaximumLinesPerSlot + 1));

        var act = () => ContentRenderSpecJson.Parse(
            ValidJson(slots: $$"""[{ "name": "headline", "lines": [{{lines}}] }]"""),
            Catalog);

        act.Should().Throw<ContentVisualContractException>()
            .WithMessage("*slot_line_limit_exceeded*");
    }

    [Fact]
    public void Parse_DuplicateSlotName_Throws()
    {
        var json = ValidJson(slots: """
            [
              { "name": "headline", "lines": ["A"] },
              { "name": "headline", "lines": ["B"] }
            ]
            """);

        var act = () => ContentRenderSpecJson.Parse(json, Catalog);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*slot_duplicate*");
    }

    [Fact]
    public void Parse_UnknownPreset_Throws()
    {
        var act = () => ContentRenderSpecJson.Parse(ValidJson(preset: "panorama"), Catalog);

        act.Should().Throw<ContentVisualContractException>();
    }

    [Fact]
    public void ParseSlots_ValidArray_ReturnsSortedSlots()
    {
        var slots = ContentRenderSpecJson.ParseSlots("""
            [
              { "name": "subhead", "lines": ["Phụ"] },
              { "name": "headline", "lines": ["Chính"] }
            ]
            """);

        slots.Select(slot => slot.Name).Should().Equal("headline", "subhead");
    }

    [Fact]
    public void ParseSlots_NotAnArray_Throws()
    {
        var act = () => ContentRenderSpecJson.ParseSlots("""{ "name": "headline" }""");

        act.Should().Throw<ContentVisualContractException>().WithMessage("*member_type_invalid*");
    }

    [Fact]
    public void ParseSlots_UnknownMember_Throws()
    {
        var act = () => ContentRenderSpecJson.ParseSlots(
            """[{ "name": "headline", "lines": ["A"], "color": "red" }]""");

        act.Should().Throw<ContentVisualContractException>().WithMessage("*unknown_member*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseSlots_BlankJson_Throws(string? json)
    {
        var act = () => ContentRenderSpecJson.ParseSlots(json!);

        act.Should().Throw<ContentVisualContractException>().WithMessage("*invalid_json*");
    }
}
