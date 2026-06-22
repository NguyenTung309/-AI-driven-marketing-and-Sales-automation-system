using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.SaleAssist;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentTaskInputTests
{
    [Fact]
    public void RequiredGuid_returns_guid_when_present()
    {
        var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var input = new Dictionary<string, string> { ["tenant_id"] = id.ToString("D") };

        AgentTaskInput.RequiredGuid(input, "tenant_id").Should().Be(id);
    }

    [Fact]
    public void RequiredGuid_rejects_missing_or_invalid_value()
    {
        var input = new Dictionary<string, string> { ["tenant_id"] = "bad" };

        var act = () => AgentTaskInput.RequiredGuid(input, "tenant_id");

        act.Should().Throw<ArgumentException>().WithMessage("tenant_id must be a valid GUID.");
    }

    [Fact]
    public void OptionalGuid_returns_null_for_missing_value()
    {
        AgentTaskInput.OptionalGuid(new Dictionary<string, string>(), "conversation_id").Should().BeNull();
    }

    [Fact]
    public void StringList_parses_json_array_or_csv()
    {
        AgentTaskInput.StringList(new Dictionary<string, string> { ["keywords"] = "[\"hsk\",\"ielts\"]" }, "keywords")
            .Should().Equal("hsk", "ielts");
        AgentTaskInput.StringList(new Dictionary<string, string> { ["keywords"] = "hsk, ielts" }, "keywords")
            .Should().Equal("hsk", "ielts");
    }

    [Fact]
    public void StringMap_parses_json_object()
    {
        var map = AgentTaskInput.StringMap(
            new Dictionary<string, string> { ["vars_json"] = "{\"name\":\"An\",\"course\":\"HSK\"}" },
            "vars_json");

        map.Should().HaveCount(2);
        map["name"].Should().Be("An");
        map["course"].Should().Be("HSK");
    }

    [Fact]
    public void StringMap_returns_empty_for_missing_value()
    {
        AgentTaskInput.StringMap(new Dictionary<string, string>(), "vars_json").Should().BeEmpty();
    }

    [Fact]
    public void StringMap_rejects_invalid_json()
    {
        var act = () => AgentTaskInput.StringMap(
            new Dictionary<string, string> { ["vars_json"] = "not-json" }, "vars_json");

        act.Should().Throw<ArgumentException>().WithMessage("vars_json must be a JSON object.");
    }

    [Fact]
    public void OptionalDecimal_returns_value_when_present()
    {
        AgentTaskInput.OptionalDecimal(new Dictionary<string, string> { ["new_budget"] = "12.5" }, "new_budget")
            .Should().Be(12.5m);
    }

    [Fact]
    public void OptionalDecimal_returns_null_for_missing_value()
    {
        AgentTaskInput.OptionalDecimal(new Dictionary<string, string>(), "new_budget").Should().BeNull();
    }

    [Fact]
    public void OptionalDecimal_rejects_invalid_value()
    {
        var act = () => AgentTaskInput.OptionalDecimal(
            new Dictionary<string, string> { ["new_budget"] = "lots" }, "new_budget");

        act.Should().Throw<ArgumentException>().WithMessage("new_budget must be a decimal number.");
    }

    [Fact]
    public void Turns_parses_json_array_of_turn_snapshots()
    {
        var json = "[{\"direction\":\"inbound\",\"content\":\"hi\",\"sentAt\":\"2026-06-21T00:00:00+00:00\"}]";

        var turns = AgentTaskInput.Turns(new Dictionary<string, string> { ["turns_json"] = json }, "turns_json");

        turns.Should().HaveCount(1);
        turns[0].Direction.Should().Be("inbound");
        turns[0].Content.Should().Be("hi");
    }

    [Fact]
    public void Turns_returns_empty_for_missing_value()
    {
        AgentTaskInput.Turns(new Dictionary<string, string>(), "turns_json").Should().BeEmpty();
    }
}
