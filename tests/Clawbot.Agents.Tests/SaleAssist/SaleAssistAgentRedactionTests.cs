using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.SaleAssist;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.SaleAssist;

public sealed class SaleAssistAgentRedactionTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task DraftAsync_redacts_history_and_prompt_before_claude_call()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("reply", 1, 1, 0.001m));
        var sut = CreateAgent(claude: claude);

        await sut.DraftAsync(Context(), CancellationToken.None);

        await claude.Received(1).CompleteAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ChatTurn>>(turns => turns.Any(t => t.Content.Contains("[PHONE]")) && turns.All(t => !t.Content.Contains("0912345678"))),
            Arg.Is<string>(prompt => prompt.Contains("[PHONE]") && !prompt.Contains("0912345678")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SummarizeAsync_redacts_transcript_before_claude_call()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("summary", 1, 1, 0.001m));
        var sut = CreateAgent(claude: claude);

        await sut.SummarizeAsync(Context(), CancellationToken.None);

        await claude.Received(1).CompleteAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            Arg.Is<string>(prompt => prompt.Contains("[PHONE]") && !prompt.Contains("0912345678")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuggestUpsellAsync_redacts_transcript_before_claude_call()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("NONE", 1, 1, 0.001m));
        var sut = CreateAgent(claude: claude);

        await sut.SuggestUpsellAsync(Context(), CancellationToken.None);

        await claude.Received(1).CompleteAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            Arg.Is<string>(prompt => prompt.Contains("[PHONE]") && !prompt.Contains("0912345678")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoSummaryAsync_redacts_turns_before_summarizer_call()
    {
        var summarizer = Substitute.For<IConversationSummarizer>();
        summarizer.SummarizeAsync(Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Clawbot.Agents.Core.Skills.Nlp.SummaryResult("summary", Array.Empty<string>()));
        var sut = CreateAgent(summarizer: summarizer);

        await sut.AutoSummaryAsync(Context(), CancellationToken.None);

        await summarizer.Received(1).SummarizeAsync(
            Arg.Is<IReadOnlyList<ConversationTurn>>(turns => turns.Any(t => t.Content.Contains("[PHONE]")) && turns.All(t => !t.Content.Contains("0912345678"))),
            100,
            Arg.Any<CancellationToken>());
    }

    private static SaleAssistAgent CreateAgent(
        IClaudeChatClient? claude = null,
        IConversationSummarizer? summarizer = null)
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RagChunk>());
        claude ??= Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("reply", 1, 1, 0.001m));
        summarizer ??= Substitute.For<IConversationSummarizer>();
        summarizer.SummarizeAsync(Arg.Any<IReadOnlyList<ConversationTurn>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new Clawbot.Agents.Core.Skills.Nlp.SummaryResult("summary", Array.Empty<string>()));
        var toxicity = Substitute.For<IToxicityFilter>();
        toxicity.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>()).Returns(false);

        return new SaleAssistAgent(
            rag,
            claude,
            summarizer,
            Redactor(),
            toxicity,
            Options.Create(new ToxicityOptions()),
            new LlmCallScope());
    }

    private static IPiiRedactor Redactor()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(
                ci.ArgAt<string>(0).Replace("0912345678", "[PHONE]", StringComparison.Ordinal),
                Array.Empty<PiiSpan>()));
        return pii;
    }

    private static ConversationContext Context() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        ContactName: null,
        Platform: "pancake",
        RecentTurns:
        [
            new TurnSnapshot("in", "Call me 0912345678", Now),
            new TurnSnapshot("out", "Will call 0912345678", Now.AddMinutes(1)),
        ]);
}
