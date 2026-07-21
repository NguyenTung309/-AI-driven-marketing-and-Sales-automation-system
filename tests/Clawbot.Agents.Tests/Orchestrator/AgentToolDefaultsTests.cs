using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentToolDefaultsTests
{
    [Theory]
    [InlineData("lead-agent", "lead-agent")]
    [InlineData("lead", "lead-agent")]
    [InlineData("sale-assist-agent", "sale-assist")]
    [InlineData("sale_assist", "sale-assist")]
    [InlineData("report-agent", "report-agent")]
    [InlineData("research", "research-agent")]
    [InlineData("reviewer", "content.review")]
    [InlineData("reviewer-agent", "content.review")]
    public void ResolveDefaultToolNames_maps_identity_to_tools(string key, string expectedTool)
    {
        var tools = AgentToolDefaults.ResolveDefaultToolNames(key);
        tools.Should().Contain(expectedTool);
    }

    [Fact]
    public void ResolveDefaultToolNames_publisher_has_no_autonomous_publish_tools()
    {
        AgentToolDefaults.ResolveDefaultToolNames("publisher-agent").Should().BeEmpty();
        AgentToolDefaults.ResolveDefaultToolNames("publisher").Should().BeEmpty();
    }

    [Fact]
    public void ResolveToolNames_explicit_grants_win_over_defaults()
    {
        var entry = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "lead-agent", "lead", "Lead", "lead",
            "desc", "{}", true, null, AllowedToolsJson: """["web.search"]""");

        AgentToolDefaults.ResolveToolNames(entry).Should().Equal("web.search");
    }

    [Fact]
    public void ResolveToolNames_empty_grants_use_defaults_for_lead()
    {
        var entry = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "lead-agent", "lead", "Lead", "lead",
            "desc", "{}", true, null, AllowedToolsJson: "[]");

        AgentToolDefaults.ResolveToolNames(entry).Should().Equal("lead-agent");
    }

    [Fact]
    public void ResolveToolNames_reporter_stays_text_only()
    {
        var entry = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(), "reporter-agent", "reporter", "Reporter", "reporter",
            "desc", "{}", true, null, AllowedToolsJson: "[]");

        AgentToolDefaults.ResolveToolNames(entry).Should().BeEmpty();
    }

    [Fact]
    public void ResolveDefaultToolNames_research_includes_web_search()
    {
        AgentToolDefaults.ResolveDefaultToolNames("research-agent")
            .Should().BeEquivalentTo("research-agent", "web.search");
    }

    [Fact]
    public void ResolveDefaultToolNames_unknown_agent_returns_empty()
    {
        AgentToolDefaults.ResolveDefaultToolNames("mystery-agent").Should().BeEmpty();
    }
}
