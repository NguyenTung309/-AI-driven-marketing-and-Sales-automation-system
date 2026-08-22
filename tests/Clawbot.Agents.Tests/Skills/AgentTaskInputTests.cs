using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.SaleAssist;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Skills;

// Parser input task orchestrator: guid/string/list/map/decimal/turns từ dictionary string.
public sealed class AgentTaskInputTests
{
    private static readonly Guid Sample = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Dictionary<string, string> Map(params (string Key, string Value)[] pairs)
        => pairs.ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void RequiredGuid_Valid_Parses()
    {
        AgentTaskInput.RequiredGuid(Map(("id", Sample.ToString())), "id").Should().Be(Sample);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")] // empty guid rejected
    public void RequiredGuid_InvalidOrEmpty_Throws(string value)
    {
        var act = () => AgentTaskInput.RequiredGuid(Map(("id", value)), "id");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequiredGuid_Missing_Throws()
    {
        var act = () => AgentTaskInput.RequiredGuid(Map(), "id");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OptionalGuid_MissingOrBlank_ReturnsNull()
    {
        AgentTaskInput.OptionalGuid(Map(), "id").Should().BeNull();
        AgentTaskInput.OptionalGuid(Map(("id", "  ")), "id").Should().BeNull();
    }

    [Fact]
    public void OptionalGuid_InvalidValue_Throws()
    {
        var act = () => AgentTaskInput.OptionalGuid(Map(("id", "bad")), "id");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RequiredString_TrimsValue()
    {
        AgentTaskInput.RequiredString(Map(("name", "  hello  ")), "name").Should().Be("hello");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RequiredString_BlankOrMissing_Throws(string value)
    {
        var act = () => AgentTaskInput.RequiredString(Map(("name", value)), "name");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OptionalString_TrimsOrNull()
    {
        AgentTaskInput.OptionalString(Map(("name", " x ")), "name").Should().Be("x");
        AgentTaskInput.OptionalString(Map(), "name").Should().BeNull();
    }

    [Fact]
    public void StringList_JsonArray_Parses()
    {
        var result = AgentTaskInput.StringList(Map(("tags", "[\"a\", \" b \", \"\"]")), "tags");

        result.Should().Equal("a", "b");
    }

    [Fact]
    public void StringList_CommaSeparated_Parses()
    {
        var result = AgentTaskInput.StringList(Map(("tags", "a, b ,c")), "tags");

        result.Should().Equal("a", "b", "c");
    }

    [Fact]
    public void StringList_MissingOrBlank_ReturnsEmpty()
    {
        AgentTaskInput.StringList(Map(), "tags").Should().BeEmpty();
    }

    [Fact]
    public void StringList_MalformedJson_Throws()
    {
        var act = () => AgentTaskInput.StringList(Map(("tags", "[unclosed")), "tags");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void StringMap_JsonObject_Parses()
    {
        var result = AgentTaskInput.StringMap(Map(("meta", "{\"k\":\"v\"}")), "meta");

        result.Should().ContainKey("k").WhoseValue.Should().Be("v");
    }

    [Fact]
    public void StringMap_MissingOrBlank_ReturnsEmpty()
    {
        AgentTaskInput.StringMap(Map(), "meta").Should().BeEmpty();
    }

    [Fact]
    public void StringMap_Malformed_Throws()
    {
        var act = () => AgentTaskInput.StringMap(Map(("meta", "{bad")), "meta");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OptionalDecimal_Parses()
    {
        AgentTaskInput.OptionalDecimal(Map(("price", "12.5")), "price").Should().Be(12.5m);
    }

    [Fact]
    public void OptionalDecimal_MissingOrBlank_ReturnsNull()
    {
        AgentTaskInput.OptionalDecimal(Map(), "price").Should().BeNull();
    }

    [Fact]
    public void OptionalDecimal_Invalid_Throws()
    {
        var act = () => AgentTaskInput.OptionalDecimal(Map(("price", "abc")), "price");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Turns_JsonArray_Parses()
    {
        var json = "[{\"direction\":\"in\",\"content\":\"hi\",\"sentAt\":\"2026-08-20T00:00:00+00:00\"}]";

        var result = AgentTaskInput.Turns(Map(("turns", json)), "turns");

        result.Should().HaveCount(1);
        result[0].Content.Should().Be("hi");
    }

    [Fact]
    public void Turns_MissingOrBlank_ReturnsEmpty()
    {
        AgentTaskInput.Turns(Map(), "turns").Should().BeEmpty();
    }

    [Fact]
    public void Turns_Malformed_Throws()
    {
        var act = () => AgentTaskInput.Turns(Map(("turns", "[bad")), "turns");

        act.Should().Throw<ArgumentException>();
    }
}
