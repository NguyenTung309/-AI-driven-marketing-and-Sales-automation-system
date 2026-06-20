using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Rag;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Content;

public sealed class ContentAgentTests
{
    [Fact]
    public async Task GenerateAsync_renders_configured_template_with_brief_and_rag_context()
    {
        var tenantId = Guid.NewGuid();
        var briefId = Guid.NewGuid();
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns([
                new RagChunk("kbv-1", "HSK", "HSK3 classes open in June", 0.91f),
            ]);

        var templates = Substitute.For<IPromptTemplateProvider>();
        templates.GetTemplate("tiktok").Returns("Brief={{brief}}\nKnowledge={{knowledge}}");

        var scope = new LlmCallScope();
        string? capturedPrompt = null;
        LlmCallContext? capturedCtx = null;
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Do<string>(p => { capturedPrompt = p; capturedCtx = scope.Current; }),
                Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(" Draft body ", 17, 9, 0m, "content-model"));

        var agent = new ContentAgent(rag, templates, claude, scope);

        var result = await agent.GenerateAsync(
            new ContentGenerateRequest(tenantId, briefId, "tiktok", "Chinese study trend", KbModuleCode: null),
            CancellationToken.None);

        result.BriefId.Should().Be(briefId);
        result.Platform.Should().Be("tiktok");
        result.Body.Should().Be("Draft body");
        result.Citations.Should().ContainSingle();
        result.InputTokens.Should().Be(17);
        result.OutputTokens.Should().Be(9);
        result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);

        await rag.Received(1).RetrieveAsync(
            Arg.Is<RagRequest>(r => r.TenantId == tenantId && r.Query == "Chinese study trend" && r.TopK == 4),
            Arg.Any<CancellationToken>());
        // Provider resolved via the ambient (tenant, content-agent) scope, and the rendered template
        // is sent verbatim as the user message.
        capturedCtx.Should().Be(new LlmCallContext(tenantId, "content-agent"));
        capturedPrompt.Should().Contain("Brief=Chinese study trend");
        capturedPrompt.Should().Contain("[1] (module=HSK, score=0.91) HSK3 classes open in June");
    }

    [Fact]
    public async Task GenerateAsync_rejects_blank_brief()
    {
        var agent = new ContentAgent(
            Substitute.For<IRagRetriever>(),
            Substitute.For<IPromptTemplateProvider>(),
            Substitute.For<IClaudeChatClient>(),
            new LlmCallScope());

        var act = async () => await agent.GenerateAsync(
            new ContentGenerateRequest(Guid.NewGuid(), BriefId: null, "facebook", " ", KbModuleCode: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
