using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentDefinitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid LlmConfigId = Guid.NewGuid();

    private static AgentDefinition CreateDefault() => AgentDefinition.Create(
        TenantId, "sale-bot", "Sale Bot", "chat", "You are a sales assistant.", Now,
        allowedToolsJson: "[\"send_message\"]", inputSchemaJson: "{\"type\":\"object\"}",
        outputSchemaJson: "{\"type\":\"object\"}", memoryScope: "session",
        llmConfigId: LlmConfigId, isOrchestratable: true, kbModuleCode: "SALE_KB");

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var agent = CreateDefault();

        agent.TenantId.Should().Be(TenantId);
        agent.Code.Should().Be("sale-bot");
        agent.DisplayName.Should().Be("Sale Bot");
        agent.AgentType.Should().Be("chat");
        agent.PersonaPrompt.Should().Be("You are a sales assistant.");
        agent.AllowedToolsJson.Should().Be("[\"send_message\"]");
        agent.InputSchemaJson.Should().Be("{\"type\":\"object\"}");
        agent.OutputSchemaJson.Should().Be("{\"type\":\"object\"}");
        agent.MemoryScope.Should().Be("session");
        agent.LlmConfigId.Should().Be(LlmConfigId);
        agent.IsOrchestratable.Should().BeTrue();
        agent.KbModuleCode.Should().Be("SALE_KB");
        agent.Version.Should().Be(1);
        agent.SystemPrompt.Should().BeNull();
        agent.SystemPromptVersion.Should().BeNull();
        agent.DeletedAt.Should().BeNull();
        agent.CreatedAt.Should().Be(Now);
        agent.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_NormalizesAgentTypeToLower()
    {
        var agent = AgentDefinition.Create(TenantId, "bot", "Bot", "CHAT", "P", Now);

        agent.AgentType.Should().Be("chat");
    }

    [Fact]
    public void Create_DefaultsJsonFieldsWhenBlank()
    {
        var agent = AgentDefinition.Create(TenantId, "bot", "Bot", "chat", "P", Now,
            allowedToolsJson: "", inputSchemaJson: "  ", outputSchemaJson: "  ", memoryScope: "  ");

        agent.AllowedToolsJson.Should().Be("[]");
        agent.InputSchemaJson.Should().Be("{}");
        agent.OutputSchemaJson.Should().Be("{}");
        agent.MemoryScope.Should().Be("none");
    }

    [Fact]
    public void Create_NormalizesKbModuleCode()
    {
        var agent = AgentDefinition.Create(TenantId, "bot", "Bot", "chat", "P", Now,
            kbModuleCode: "  MY_KB  ");

        agent.KbModuleCode.Should().Be("MY_KB");
    }

    [Fact]
    public void Create_NullKbModuleCodeStaysNull()
    {
        var agent = AgentDefinition.Create(TenantId, "bot", "Bot", "chat", "P", Now);

        agent.KbModuleCode.Should().BeNull();
    }

    // ── UpdateDefinition ──────────────────────────────────────────────

    [Fact]
    public void UpdateDefinition_UpdatesAllFieldsAndBumpsVersion()
    {
        var agent = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        agent.UpdateDefinition("New Name", "WORKER", "New persona",
            "[\"tool_a\"]", "{\"in\":true}", "{\"out\":true}", "tenant",
            null, false, updatedAt, kbModuleCode: "NEW_KB");

        agent.DisplayName.Should().Be("New Name");
        agent.AgentType.Should().Be("worker");
        agent.PersonaPrompt.Should().Be("New persona");
        agent.AllowedToolsJson.Should().Be("[\"tool_a\"]");
        agent.InputSchemaJson.Should().Be("{\"in\":true}");
        agent.OutputSchemaJson.Should().Be("{\"out\":true}");
        agent.MemoryScope.Should().Be("tenant");
        agent.LlmConfigId.Should().BeNull();
        agent.IsOrchestratable.Should().BeFalse();
        agent.KbModuleCode.Should().Be("NEW_KB");
        agent.Version.Should().Be(2);
        agent.UpdatedAt.Should().Be(updatedAt);
    }

    // ── SetSystemPrompt ───────────────────────────────────────────────

    [Fact]
    public void SetSystemPrompt_SetsPromptAndClearsVersion()
    {
        var agent = CreateDefault();
        agent.SetSeededSystemPrompt("Seed v1", 1, Now);
        var updatedAt = Now.AddMinutes(5);

        agent.SetSystemPrompt("Custom prompt", updatedAt);

        agent.SystemPrompt.Should().Be("Custom prompt");
        agent.SystemPromptVersion.Should().BeNull();
        agent.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void SetSystemPrompt_NullClearsPrompt()
    {
        var agent = CreateDefault();
        agent.SetSeededSystemPrompt("Seed", 1, Now);

        agent.SetSystemPrompt(null, Now.AddMinutes(5));

        agent.SystemPrompt.Should().BeNull();
        agent.SystemPromptVersion.Should().BeNull();
    }

    // ── SetSeededSystemPrompt ─────────────────────────────────────────

    [Fact]
    public void SetSeededSystemPrompt_SetsPromptAndVersion()
    {
        var agent = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        agent.SetSeededSystemPrompt("Seed prompt v2", 2, updatedAt);

        agent.SystemPrompt.Should().Be("Seed prompt v2");
        agent.SystemPromptVersion.Should().Be(2);
        agent.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void SetSeededSystemPrompt_ThrowsOnZeroVersion()
    {
        var agent = CreateDefault();

        var act = () => agent.SetSeededSystemPrompt("Prompt", 0, Now);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SetSeededSystemPrompt_ThrowsOnEmptyPrompt()
    {
        var agent = CreateDefault();

        var act = () => agent.SetSeededSystemPrompt("  ", 1, Now);

        act.Should().Throw<ArgumentException>();
    }

    // ── CanRefreshSeededSystemPrompt ──────────────────────────────────

    [Fact]
    public void CanRefreshSeededSystemPrompt_TrueWhenNoPromptSet()
    {
        var agent = CreateDefault();

        agent.CanRefreshSeededSystemPrompt(1).Should().BeTrue();
    }

    [Fact]
    public void CanRefreshSeededSystemPrompt_TrueWhenVersionIsOlder()
    {
        var agent = CreateDefault();
        agent.SetSeededSystemPrompt("v1", 1, Now);

        agent.CanRefreshSeededSystemPrompt(2).Should().BeTrue();
    }

    [Fact]
    public void CanRefreshSeededSystemPrompt_FalseWhenVersionIsCurrent()
    {
        var agent = CreateDefault();
        agent.SetSeededSystemPrompt("v2", 2, Now);

        agent.CanRefreshSeededSystemPrompt(2).Should().BeFalse();
    }

    [Fact]
    public void CanRefreshSeededSystemPrompt_FalseWhenCustomized()
    {
        var agent = CreateDefault();
        agent.SetSystemPrompt("Custom", Now);

        agent.CanRefreshSeededSystemPrompt(99).Should().BeFalse();
    }

    [Fact]
    public void CanRefreshSeededSystemPrompt_FalseWhenVersionIsZero()
    {
        var agent = CreateDefault();

        agent.CanRefreshSeededSystemPrompt(0).Should().BeFalse();
    }

    // ── SetAllowedTools ───────────────────────────────────────────────

    [Fact]
    public void SetAllowedTools_UpdatesToolsJson()
    {
        var agent = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        agent.SetAllowedTools("[\"new_tool\"]", updatedAt);

        agent.AllowedToolsJson.Should().Be("[\"new_tool\"]");
        agent.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void SetAllowedTools_DefaultsToEmptyArrayWhenBlank()
    {
        var agent = CreateDefault();

        agent.SetAllowedTools("", Now);

        agent.AllowedToolsJson.Should().Be("[]");
    }

    // ── Archive ───────────────────────────────────────────────────────

    [Fact]
    public void Archive_SetsDeletedAtAndDisablesOrchestration()
    {
        var agent = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        agent.Archive(updatedAt);

        agent.DeletedAt.Should().Be(updatedAt);
        agent.IsOrchestratable.Should().BeFalse();
        agent.UpdatedAt.Should().Be(updatedAt);
    }
}
