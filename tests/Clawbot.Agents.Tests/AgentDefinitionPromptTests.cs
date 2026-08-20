using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Agents.Tests;

public sealed class AgentDefinitionPromptTests
{
    [Fact]
    public void SetSeededSystemPrompt_RefreshesOnlyASeededOlderVersion()
    {
        // Arrange
        var definition = AgentDefinition.Create(
            Guid.NewGuid(),
            "content-agent",
            "Content Agent",
            "content",
            "Compact planner description.",
            DateTimeOffset.UnixEpoch);
        definition.SetSeededSystemPrompt("seed-v1", version: 1, DateTimeOffset.UnixEpoch);

        // Act
        var canRefresh = definition.CanRefreshSeededSystemPrompt(currentVersion: 2);
        if (canRefresh)
            definition.SetSeededSystemPrompt("seed-v2", version: 2, DateTimeOffset.UnixEpoch.AddMinutes(1));

        // Assert
        canRefresh.Should().BeTrue();
        definition.SystemPrompt.Should().Be("seed-v2");
        definition.SystemPromptVersion.Should().Be(2);
    }

    [Fact]
    public void SetSystemPrompt_PreservesTenantCustomizationDuringLaterSeed()
    {
        // Arrange
        var definition = AgentDefinition.Create(
            Guid.NewGuid(),
            "content-agent",
            "Content Agent",
            "content",
            "Compact planner description.",
            DateTimeOffset.UnixEpoch);
        definition.SetSystemPrompt("tenant-customized-prompt", DateTimeOffset.UnixEpoch);

        // Act
        var canRefresh = definition.CanRefreshSeededSystemPrompt(currentVersion: 2);

        // Assert
        canRefresh.Should().BeFalse();
        definition.SystemPrompt.Should().Be("tenant-customized-prompt");
        definition.SystemPromptVersion.Should().BeNull();
    }
}
