using Clawbot.AgentService.Services;
using Clawbot.Agents.Contracts.Chat;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.Domain.ChatScenarios;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using CoreChat = Clawbot.Agents.Core.Chat;

namespace Clawbot.AgentService.Tests.Services;

public sealed class ChatAgentGrpcServiceTests
{
    private static readonly Guid TenantId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset Now = new(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Reply_matches_chat_scenario_template_by_conversation_platform()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "thread-1", Now);
        fx.Db.AddRange(
            conversation,
            ChatScenario.Create(TenantId, "KB-001", "pricing", "hoc phi", "Facebook pricing template", "facebook", Now),
            ChatScenario.Create(TenantId, "KB-002", "pricing", "hoc phi", "Zalo pricing template", "zalo", Now));
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            fx.Db,
            new FixedClock(Now),
            NullLogger<ChatAgentGrpcService>.Instance);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "hoc phi HSK4 bao nhieu?",
        }, stream, TestServerCallContext.Create());

        claude.SystemPrompt.Should().Contain("## Matched chat scenario template");
        claude.SystemPrompt.Should().Contain("Zalo pricing template");
        claude.SystemPrompt.Should().NotContain("Facebook pricing template");
        stream.Messages.Where(m => !m.Final).Select(m => m.Text).Should().Equal("Scenario reply");
        stream.Messages.Should().ContainSingle(m => m.Final && m.Text.Length == 0);
    }

    [Fact]
    public async Task Reply_streams_claude_chunks_before_final_marker_and_persists_joined_text()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "web", "thread-stream", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude(
            new[]
            {
                new ClaudeStreamChunk("Xin ", Final: false, 0, 0, 0m),
                new ClaudeStreamChunk("chao", Final: false, 0, 0, 0m),
                new ClaudeStreamChunk(string.Empty, Final: true, 10, 5, 0.000105m),
            });
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            fx.Db,
            new FixedClock(Now),
            NullLogger<ChatAgentGrpcService>.Instance);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, stream, TestServerCallContext.Create());

        claude.StreamCalls.Should().Be(1);
        claude.CompleteCalls.Should().Be(0);
        stream.Messages.Select(m => (m.Text, m.Final)).Should().Equal(
            ("Xin ", false),
            ("chao", false),
            (string.Empty, true));

        var savedMessage = fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out");
        savedMessage.Content.Should().Be("Xin chao");

        var trace = fx.Db.AgentTraces.Single();
        trace.Message.Should().Contain("tokens=10/5");
        trace.Message.Should().Contain("usd=0.0001");
    }

    private static CoreChat.ChatAgent BuildAgent(IClaudeChatClient claude)
    {
        var injection = Substitute.For<IPromptInjectionDefender>();
        injection.InspectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InjectionVerdict(false, 0.1f, Array.Empty<string>()));

        var pii = Substitute.For<IPiiRedactor>();
        pii.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));

        var intent = Substitute.For<IIntentClassifier>();
        intent.ClassifyAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new IntentResult("pricing", 0.9f));

        var language = Substitute.For<ILanguageDetector>();
        language.DetectAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LanguageDetection("vi", 0.8f));

        var toxicity = Substitute.For<IToxicityFilter>();
        toxicity.IsBlockedAsync(Arg.Any<string>(), Arg.Any<float>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var spam = Substitute.For<ISpamDetector>();
        spam.EvaluateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpamSignal(false, 0f, null));

        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RagChunk>());

        var cost = Substitute.For<IClaudeCostTracker>();

        return new CoreChat.ChatAgent(
            rag,
            claude,
            intent,
            pii,
            injection,
            cost,
            language,
            toxicity,
            spam,
            Options.Create(new ToxicityOptions()),
            new AlwaysEnabledAgentToggleGate());
    }

    private sealed class CapturingClaude : IClaudeChatClient
    {
        private readonly IReadOnlyList<ClaudeStreamChunk> _streamChunks;

        public CapturingClaude(IReadOnlyList<ClaudeStreamChunk>? streamChunks = null)
        {
            _streamChunks = streamChunks ?? new[]
            {
                new ClaudeStreamChunk("Scenario reply", Final: false, 0, 0, 0m),
                new ClaudeStreamChunk(string.Empty, Final: true, 10, 5, 0.0001m),
            };
        }

        public string SystemPrompt { get; private set; } = string.Empty;
        public int CompleteCalls { get; private set; }
        public int StreamCalls { get; private set; }

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct)
        {
            CompleteCalls++;
            SystemPrompt = systemPrompt;
            return Task.FromResult(new ClaudeReply("Scenario reply", 10, 5, 0.0001m));
        }

        public IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct)
        {
            StreamCalls++;
            SystemPrompt = systemPrompt;
            _ = history;
            _ = userMessage;
            _ = ct;
            return YieldStreamChunks(_streamChunks);
        }

        private static async IAsyncEnumerable<ClaudeStreamChunk> YieldStreamChunks(IReadOnlyList<ClaudeStreamChunk> chunks)
        {
            foreach (var chunk in chunks)
            {
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class CapturingChatStream : IServerStreamWriter<ChatToken>
    {
        private readonly List<ChatToken> _messages = new();

        public IReadOnlyList<ChatToken> Messages => _messages;

        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(ChatToken message)
        {
            _messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
