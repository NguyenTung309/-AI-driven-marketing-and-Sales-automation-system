using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class AgentDefinitionCatalogEntryTests
{
    [Fact]
    public void ToPlannerEntry_UsesCompactPersonaInsteadOfLongRuntimePrompt()
    {
        // Arrange
        const string persona = "Create channel-ready Học Bá campaign content from a brief.";
        var definition = new AgentDefinitionCatalogEntry(
            Guid.NewGuid(),
            "content-agent",
            "content",
            "Content Agent",
            "content",
            persona,
            "{}",
            true,
            null,
            "[]",
            AgentPromptPacks.For("content-agent"));

        // Act
        var plannerEntry = definition.ToPlannerEntry();

        // Assert
        plannerEntry.Description.Should().Be(persona);
        plannerEntry.Description.Length.Should().BeLessThan(400);
        plannerEntry.Description.Should().NotContain("BỐI CẢNH THƯƠNG HIỆU");
    }
}
