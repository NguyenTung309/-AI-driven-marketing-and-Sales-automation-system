using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Skills;

// M11 P1 — ChatAgent wiring: language detection, toxicity blocking, spam flagging.
public sealed class ChatAgentWiringTests
{
    private static ChatAgent CreateAgent(
        IPromptInjectionDefender? injection = null,
        IPiiRedactor? pii = null,
        IIntentClassifier? intent = null,
        ILanguageDetector? language = null,
        IToxicityFilter? toxicity = null,
        ISpamDetector? spam = null,
        IClaudeChatClient? claude = null,
        IRagRetriever? rag = null,
        IClaudeCostTracker? cost = null)
    {
        injection ??= SafeInjection();
        pii ??= SafePii();
        intent ??= SafeIntent();
        language ??= SafeLanguage();
        toxicity ??= SafeToxicity();
        spam ??= SafeSpam();
        claude ??= SafeClaude();
        rag ??= SafeRag();
        cost ??= SafeCost();
        var toxicityOpts = Options.Create(new ToxicityOptions());

        return new ChatAgent(rag, claude, intent, pii, injection, cost, language, toxicity, spam, toxicityOpts, new AlwaysEnabledAgentToggleGate(), new LlmCallScope());
    }

    [Fact]
    public async Task Clean_input_passes_through_with_language()
    {
        var lang = Substitute.For<ILanguageDetector>();
        lang.DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LanguageDetection("vi", 0.8f));

        var sut = CreateAgent(language: lang);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(Guid.NewGuid(), null, null, "Xin chào", Array.Empty<ChatTurn>()));

        reply.Blocked.Should().BeFalse();
        reply.Language.Should().Be("vi");
        reply.ToxicityBlocked.Should().BeFalse();
        reply.SpamFlagged.Should().BeFalse();
    }

    [Fact]
    public async Task Toxic_inbound_blocks_with_reason()
    {
        var tox = Substitute.For<IToxicityFilter>();
        tox.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = CreateAgent(toxicity: tox);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(Guid.NewGuid(), null, null, "toxic message", Array.Empty<ChatTurn>()));

        reply.Blocked.Should().BeTrue();
        reply.ToxicityBlocked.Should().BeTrue();
        reply.BlockReason.Should().Be("toxicity");
        reply.Text.Should().Contain("không phù hợp");
    }

    [Fact]
    public async Task Spam_inbound_flags_but_still_replies()
    {
        var spamDetector = Substitute.For<ISpamDetector>();
        spamDetector.EvaluateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpamSignal(true, 0.7f, "url_flood"));

        var sut = CreateAgent(spam: spamDetector);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(Guid.NewGuid(), null, null, "Check https://a.com https://b.com", Array.Empty<ChatTurn>()));

        reply.Blocked.Should().BeFalse();
        reply.SpamFlagged.Should().BeTrue();
        reply.Text.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Injection_still_blocks_first()
    {
        var inj = Substitute.For<IPromptInjectionDefender>();
        inj.InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InjectionVerdict(true, 0.9f, new[] { "ignore previous instructions" }));

        var sut = CreateAgent(injection: inj);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(Guid.NewGuid(), null, null, "hack", Array.Empty<ChatTurn>()));

        reply.Blocked.Should().BeTrue();
        reply.Intent.Should().Be("blocked");
    }

    [Fact]
    public async Task Language_injected_into_system_prompt()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var sys = ci.ArgAt<string>(0);
                sys.Should().Contain("Reply in Vietnamese");
                return new ClaudeReply("Xin chào!", 100, 50, 0.001m);
            });

        var lang = Substitute.For<ILanguageDetector>();
        lang.DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LanguageDetection("vi", 0.8f));

        var sut = CreateAgent(language: lang, claude: claude);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(Guid.NewGuid(), null, null, "Xin chào", Array.Empty<ChatTurn>()));

        reply.Language.Should().Be("vi");
    }

    [Fact]
    public async Task Matched_scenario_template_is_injected_into_system_prompt()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var sys = ci.ArgAt<string>(0);
                sys.Should().Contain("## Matched chat scenario template");
                sys.Should().Contain("Tra loi hoc phi HSK4 trong 2 cau.");
                return new ClaudeReply("Da gui hoc phi.", 100, 50, 0.001m);
            });

        var sut = CreateAgent(claude: claude);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(
            Guid.NewGuid(),
            null,
            null,
            "hoc phi HSK4 bao nhieu?",
            Array.Empty<ChatTurn>(),
            MatchedScenarioTemplate: "Tra loi hoc phi HSK4 trong 2 cau."));

        reply.Blocked.Should().BeFalse();
        await claude.Received(1).CompleteAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            "hoc phi HSK4 bao nhieu?",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReplyAsync_redacts_history_before_claude_call()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(
                ci.ArgAt<string>(0).Replace("0912345678", "[PHONE]", StringComparison.Ordinal),
                Array.Empty<PiiSpan>()));
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("ok", 1, 1, 0.001m));
        var sut = CreateAgent(pii: pii, claude: claude);

        await sut.ReplyAsync(new ChatAgentRequest(
            Guid.NewGuid(),
            null,
            null,
            "new 0912345678",
            [new ChatTurn("user", "old 0912345678")]));

        await claude.Received(1).CompleteAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ChatTurn>>(turns =>
                turns.Count == 1 && turns[0].Content == "old [PHONE]"),
            "new [PHONE]",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StreamReplyAsync_redacts_history_before_claude_call()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(
                ci.ArgAt<string>(0).Replace("0912345678", "[PHONE]", StringComparison.Ordinal),
                Array.Empty<PiiSpan>()));
        var claude = Substitute.For<IClaudeChatClient>();
        claude.StreamAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Stream(new ClaudeStreamChunk(string.Empty, Final: true, 1, 1, 0.001m, "m")));
        var sut = CreateAgent(pii: pii, claude: claude);

        var chunks = new List<ChatAgentStreamChunk>();
        await foreach (var chunk in sut.StreamReplyAsync(new ChatAgentRequest(
                           Guid.NewGuid(),
                           null,
                           null,
                           "new 0912345678",
                           [new ChatTurn("user", "old 0912345678")])))
        {
            chunks.Add(chunk);
        }

        _ = claude.Received(1).StreamAsync(
            Arg.Any<string>(),
            Arg.Is<IReadOnlyList<ChatTurn>>(turns =>
                turns.Count == 1 && turns[0].Content == "old [PHONE]"),
            "new [PHONE]",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cost_cap_blocks_claude_call_before_generation()
    {
        var tenant = Guid.NewGuid();
        var claude = Substitute.For<IClaudeChatClient>();
        var cost = Substitute.For<IClaudeCostTracker>();
        cost.SummaryAsync(tenant, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new CostSummary(tenant, 200m, 200m, 1f));
        var sut = CreateAgent(claude: claude, cost: cost);

        var reply = await sut.ReplyAsync(new ChatAgentRequest(
            tenant,
            Guid.NewGuid(),
            null,
            "Can you answer pricing?",
            Array.Empty<ChatTurn>()));

        reply.Blocked.Should().BeTrue();
        reply.BlockReason.Should().Be("cost_cap_exceeded");
        await claude.DidNotReceive().CompleteAsync(
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<ChatTurn>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Outbound_toxicity_blocks_reply()
    {
        var tox = Substitute.For<IToxicityFilter>();
        // Inbound clean, outbound toxic
        tox.IsBlockedAsync(Arg.Any<string>(), Arg.Is<float>(t => t == 0.7f), Arg.Any<CancellationToken>())
            .Returns(false);
        tox.IsBlockedAsync(Arg.Any<string>(), Arg.Is<float>(t => t == 0.8f), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = CreateAgent(toxicity: tox);
        var reply = await sut.ReplyAsync(new ChatAgentRequest(Guid.NewGuid(), null, null, "Hello", Array.Empty<ChatTurn>()));

        reply.Blocked.Should().BeTrue();
        reply.ToxicityBlocked.Should().BeTrue();
        reply.BlockReason.Should().Be("outbound_toxicity");
    }

    private static IPromptInjectionDefender SafeInjection()
    {
        var inj = Substitute.For<IPromptInjectionDefender>();
        inj.InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InjectionVerdict(false, 0.1f, Array.Empty<string>()));
        return inj;
    }

    private static IPiiRedactor SafePii()
    {
        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        return pii;
    }

    private static IIntentClassifier SafeIntent()
    {
        var intent = Substitute.For<IIntentClassifier>();
        intent.ClassifyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IntentResult("other", 0.3f));
        return intent;
    }

    private static ILanguageDetector SafeLanguage()
    {
        var lang = Substitute.For<ILanguageDetector>();
        lang.DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LanguageDetection("en", 0.5f));
        return lang;
    }

    private static IToxicityFilter SafeToxicity()
    {
        var tox = Substitute.For<IToxicityFilter>();
        tox.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(false);
        return tox;
    }

    private static ISpamDetector SafeSpam()
    {
        var spam = Substitute.For<ISpamDetector>();
        spam.EvaluateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpamSignal(false, 0f, null));
        return spam;
    }

    private static async IAsyncEnumerable<ClaudeStreamChunk> Stream(params ClaudeStreamChunk[] chunks)
    {
        foreach (var chunk in chunks)
        {
            await Task.Yield();
            yield return chunk;
        }
    }

    private static IClaudeChatClient SafeClaude()
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("Test reply", 100, 50, 0.001m));
        return claude;
    }

    private static IRagRetriever SafeRag()
    {
        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RagChunk>());
        return rag;
    }

    private static IClaudeCostTracker SafeCost()
    {
        var cost = Substitute.For<IClaudeCostTracker>();
        cost.SummaryAsync(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var tenantId = ci.ArgAt<Guid>(0);
                return new CostSummary(tenantId, 0m, 200m, 0f);
            });
        return cost;
    }
}
