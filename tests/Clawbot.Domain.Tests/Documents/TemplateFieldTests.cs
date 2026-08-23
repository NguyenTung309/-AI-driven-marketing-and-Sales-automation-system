using System.Text.Json;
using Clawbot.Domain.Documents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Documents;

public sealed class TemplateFieldTests
{
    // ── NormalizeType ─────────────────────────────────────────────────

    [Theory]
    [InlineData("text", "text")]
    [InlineData("multiline", "multiline")]
    [InlineData("number", "number")]
    [InlineData("currency", "currency")]
    [InlineData("date", "date")]
    [InlineData("TEXT", "text")]
    [InlineData("Date", "date")]
    public void NormalizeType_ReturnsKnownTypesLowercase(string input, string expected)
    {
        TemplateField.NormalizeType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("select")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void NormalizeType_FallsBackToTextForUnknown(string? input)
    {
        TemplateField.NormalizeType(input).Should().Be("text");
    }

    // ── Parse (array schema) ──────────────────────────────────────────

    [Fact]
    public void Parse_ArraySchema_ReturnsFields()
    {
        var json = """[{"key":"name","label":"Tên","type":"text","required":true,"placeholder":"Nhập tên","sample":"Nguyễn Văn A"}]""";

        var fields = TemplateFieldSchema.Parse(json);

        fields.Should().ContainSingle();
        var f = fields[0];
        f.Key.Should().Be("name");
        f.Label.Should().Be("Tên");
        f.Type.Should().Be("text");
        f.Required.Should().BeTrue();
        f.Placeholder.Should().Be("Nhập tên");
        f.Sample.Should().Be("Nguyễn Văn A");
    }

    [Fact]
    public void Parse_ArraySchema_LabelDefaultsToKey()
    {
        var json = """[{"key":"amount","type":"number","required":false}]""";

        var fields = TemplateFieldSchema.Parse(json);

        fields[0].Label.Should().Be("amount");
    }

    [Fact]
    public void Parse_ArraySchema_SkipsItemsWithoutKey()
    {
        var json = """[{"label":"No key"},{"key":"valid","type":"text"}]""";

        var fields = TemplateFieldSchema.Parse(json);

        fields.Should().ContainSingle();
        fields[0].Key.Should().Be("valid");
    }

    [Fact]
    public void Parse_ArraySchema_SkipsNonObjectItems()
    {
        var json = """["string_item",{"key":"valid"}]""";

        var fields = TemplateFieldSchema.Parse(json);

        fields.Should().ContainSingle();
    }

    [Fact]
    public void Parse_ArraySchema_CaseInsensitivePropertyLookup()
    {
        var json = """[{"Key":"name","LABEL":"Tên","TYPE":"text","Required":true}]""";

        var fields = TemplateFieldSchema.Parse(json);

        fields.Should().ContainSingle();
        fields[0].Key.Should().Be("name");
        fields[0].Label.Should().Be("Tên");
        fields[0].Type.Should().Be("text");
        fields[0].Required.Should().BeTrue();
    }

    // ── Parse (legacy object schema) ──────────────────────────────────

    [Fact]
    public void Parse_LegacyObjectSchema_ReturnsTextFields()
    {
        var json = """{"customer_name":"Tên khách hàng","order_date":"dd/MM/yyyy"}""";

        var fields = TemplateFieldSchema.Parse(json);

        fields.Should().HaveCount(2);
        fields.Should().AllSatisfy(f =>
        {
            f.Type.Should().Be("text");
            f.Required.Should().BeFalse();
            f.Sample.Should().BeNull();
        });
    }

    [Fact]
    public void Parse_LegacyObjectSchema_UsesHintAsLabelAndPlaceholder()
    {
        var json = """{"amount":"Số tiền"}""";

        var fields = TemplateFieldSchema.Parse(json);

        fields[0].Label.Should().Be("Số tiền");
        fields[0].Placeholder.Should().Be("Số tiền");
    }

    [Fact]
    public void Parse_LegacyObjectSchema_LabelDefaultsToKeyWhenNoHint()
    {
        var json = """{"bare_key":null}""";

        var fields = TemplateFieldSchema.Parse(json);

        fields[0].Label.Should().Be("bare_key");
        fields[0].Placeholder.Should().BeNull();
    }

    // ── Parse edge cases ──────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_ReturnsEmptyForNullOrWhitespace(string? input)
    {
        TemplateFieldSchema.Parse(input).Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReturnsEmptyForInvalidJson()
    {
        TemplateFieldSchema.Parse("{not valid json").Should().BeEmpty();
    }

    [Fact]
    public void Parse_ReturnsEmptyForPrimitiveRoot()
    {
        TemplateFieldSchema.Parse("\"just a string\"").Should().BeEmpty();
    }

    // ── Serialize ─────────────────────────────────────────────────────

    [Fact]
    public void Serialize_RoundTripsThroughParse()
    {
        var original = new List<TemplateField>
        {
            new("name", "Tên", "text", true, "Nhập tên", "Nguyễn Văn A"),
            new("amount", "Số tiền", "currency", false, null, null),
        };

        var json = TemplateFieldSchema.Serialize(original);
        var parsed = TemplateFieldSchema.Parse(json);

        parsed.Should().HaveCount(2);
        parsed[0].Key.Should().Be("name");
        parsed[0].Required.Should().BeTrue();
        parsed[1].Type.Should().Be("currency");
    }

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var fields = new List<TemplateField> { new("K", "L", "text", true, null, null) };

        var json = TemplateFieldSchema.Serialize(fields);

        json.Should().Contain("\"key\"");
        json.Should().Contain("\"label\"");
        json.Should().Contain("\"required\"");
        json.Should().NotContain("\"Key\"");
    }

    // ── MissingRequired ───────────────────────────────────────────────

    [Fact]
    public void MissingRequired_IdentifiesMissingFields()
    {
        var fields = new List<TemplateField>
        {
            new("name", "Name", "text", true, null, null),
            new("email", "Email", "text", true, null, null),
            new("notes", "Notes", "text", false, null, null),
        };
        var vars = new Dictionary<string, string> { ["name"] = "Alice" };

        var missing = TemplateFieldSchema.MissingRequired(fields, vars);

        missing.Should().ContainSingle();
        missing[0].Key.Should().Be("email");
    }

    [Fact]
    public void MissingRequired_TreatsWhitespaceAsMissing()
    {
        var fields = new List<TemplateField>
        {
            new("name", "Name", "text", true, null, null),
        };
        var vars = new Dictionary<string, string> { ["name"] = "   " };

        var missing = TemplateFieldSchema.MissingRequired(fields, vars);

        missing.Should().ContainSingle();
    }

    [Fact]
    public void MissingRequired_ReturnsEmptyWhenAllPresent()
    {
        var fields = new List<TemplateField>
        {
            new("name", "Name", "text", true, null, null),
        };
        var vars = new Dictionary<string, string> { ["name"] = "Alice" };

        TemplateFieldSchema.MissingRequired(fields, vars).Should().BeEmpty();
    }

    [Fact]
    public void MissingRequired_IgnoresOptionalFields()
    {
        var fields = new List<TemplateField>
        {
            new("notes", "Notes", "text", false, null, null),
        };
        var vars = new Dictionary<string, string>();

        TemplateFieldSchema.MissingRequired(fields, vars).Should().BeEmpty();
    }

    [Fact]
    public void MissingRequired_ThrowsOnNullFields()
    {
        var act = () => TemplateFieldSchema.MissingRequired(null!, new Dictionary<string, string>());

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MissingRequired_ThrowsOnNullVars()
    {
        var act = () => TemplateFieldSchema.MissingRequired([], null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
