using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var config = AgentConfig.Create(TenantId, "chat-01", "Chat Agent", "chat", "gpt-4o", Now);

        config.TenantId.Should().Be(TenantId);
        config.Code.Should().Be("chat-01");
        config.DisplayName.Should().Be("Chat Agent");
        config.AgentType.Should().Be("chat");
        config.Model.Should().Be("gpt-4o");
        config.Status.Should().Be("stopped");
        config.SkillFilesJson.Should().Be("[]");
        config.KbModulesJson.Should().Be("[]");
        config.ConfigJson.Should().Be("{}");
        config.LlmConfigId.Should().BeNull();
        config.CreatedAt.Should().Be(Now);
        config.UpdatedAt.Should().Be(Now);
        config.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Start_SetsStatusRunning()
    {
        var config = AgentConfig.Create(TenantId, "c", "D", "chat", "m", Now);

        config.Start();

        config.Status.Should().Be("running");
    }

    [Fact]
    public void Stop_SetsStatusStopped()
    {
        var config = AgentConfig.Create(TenantId, "c", "D", "chat", "m", Now);
        config.Start();

        config.Stop();

        config.Status.Should().Be("stopped");
    }

    [Fact]
    public void MarkError_SetsStatusError()
    {
        var config = AgentConfig.Create(TenantId, "c", "D", "chat", "m", Now);

        config.MarkError();

        config.Status.Should().Be("error");
    }

    [Fact]
    public void UpdateSettings_ChangesAllMutableFields()
    {
        var config = AgentConfig.Create(TenantId, "c", "Old", "chat", "old-model", Now);
        var updatedAt = Now.AddMinutes(5);

        config.UpdateSettings("New Name", "claude-3", "[\"skill.json\"]", "[\"kb1\"]", "{\"temp\":0.7}", updatedAt);

        config.DisplayName.Should().Be("New Name");
        config.Model.Should().Be("claude-3");
        config.SkillFilesJson.Should().Be("[\"skill.json\"]");
        config.KbModulesJson.Should().Be("[\"kb1\"]");
        config.ConfigJson.Should().Be("{\"temp\":0.7}");
        config.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void BindLlmConfig_SetsIdAndTimestamp()
    {
        var config = AgentConfig.Create(TenantId, "c", "D", "chat", "m", Now);
        var llmConfigId = Guid.NewGuid();

        config.BindLlmConfig(llmConfigId, Now.AddMinutes(1));

        config.LlmConfigId.Should().Be(llmConfigId);
        config.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void BindLlmConfig_UnbindsWhenNull()
    {
        var config = AgentConfig.Create(TenantId, "c", "D", "chat", "m", Now);
        config.BindLlmConfig(Guid.NewGuid(), Now);

        config.BindLlmConfig(null, Now.AddMinutes(1));

        config.LlmConfigId.Should().BeNull();
    }
}
