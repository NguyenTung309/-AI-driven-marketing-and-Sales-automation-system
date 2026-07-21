using Clawbot.Agents.Core;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Orchestrator;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using NSubstitute;

namespace Clawbot.Agents.Tests.Orchestrator;

public sealed class PlatformAgentAdapterTests
{
    [Theory]
    [InlineData("tiktok")]
    [InlineData("youtube")]
    [InlineData("website")]
    [InlineData(" ")]
    public async Task ContentAgentAdapter_rejects_non_writable_platforms(string platform)
    {
        var adapter = new ContentAgentAdapter(BuildContentAgent());

        var result = await adapter.ExecuteAsync(CreateTask(platform), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().Be("content.platform_unsupported");
    }

    [Fact]
    public async Task ContentAgentAdapter_normalizes_writable_platform_before_generation()
    {
        var adapter = new ContentAgentAdapter(BuildContentAgent());

        var result = await adapter.ExecuteAsync(CreateTask(" Instagram "), CancellationToken.None);

        result.Success.Should().BeTrue(result.Error);
        result.Output.Should().Contain("\"platform\":\"instagram\"");
    }

    private static AgentTask CreateTask(string platform) =>
        new(
            "task-1",
            "content-agent",
            "Generate content",
            new Dictionary<string, string>
            {
                ["tenant_id"] = Guid.NewGuid().ToString(),
                ["platform"] = platform,
                ["brief"] = "Launch campaign",
            });

    private static ContentAgent BuildContentAgent()
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>()).Returns([]);
        var templates = Substitute.For<IPromptTemplateProvider>();
        templates.GetTemplate(Arg.Any<string>()).Returns("{{brief}}");
        var chat = Substitute.For<IClaudeChatClient>();
        chat.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("Generated draft", 10, 5, 0m, "test-model"));

        return new ContentAgent(rag, templates, chat, new LlmCallScope());
    }
}
