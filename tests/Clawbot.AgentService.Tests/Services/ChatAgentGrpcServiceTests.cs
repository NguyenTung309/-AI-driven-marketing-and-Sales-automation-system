using System.Runtime.CompilerServices;
using Clawbot.Agents.Contracts.Chat;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Rag;
using Clawbot.Agents.Core.Skills.Lead;
using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Agents.Core.Skills.Ops;
using Clawbot.AgentService.Services;
using Clawbot.Domain.Agents;
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
        var clock = new FixedClock(Now);
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
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
        var clock = new FixedClock(Now);
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "toi can tu van",
        }, stream, TestServerCallContext.Create());

        claude.StreamCalls.Should().Be(1);
        claude.CompleteCalls.Should().Be(0);
        stream.Messages.Select(m => (m.Text, m.Final)).Should().Equal(
            ("Xin ", false),
            ("chao", false),
            (string.Empty, true));

        var savedMessage = fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out");
        savedMessage.Content.Should().Be("Xin chao");
        savedMessage.Status.Should().Be("sent");

        var trace = fx.Db.AgentTraces.Single();
        trace.Message.Should().Contain("tokens=10/5");
        trace.Message.Should().Contain("usd=0.0001");
    }

    [Fact]
    public async Task Reply_sends_to_channel_when_conversation_has_external_thread()
    {
        // SPEC-16 P2-10: an unblocked reply to a conversation with an external thread is physically sent via the channel adapter.
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "pancake", "page_1:thread-ext", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        channel.Name.Returns("pancake");
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, stream, TestServerCallContext.Create());

        await channel.Received(1).SendAsync(TenantId, "page_1:thread-ext", "Scenario reply", Arg.Any<CancellationToken>());
        fx.Db.AgentTraces.Should().Contain(t => t.Phase == "sent" && (t.Message ?? string.Empty).Contains("page_1:thread-ext"));
    }

    [Fact]
    public async Task Reply_marks_message_pending_before_channel_delivery_then_sent_after_confirmation()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "pzl_page_1:pzl_conv_1", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        string? statusDuringSend = null;
        channel.SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                statusDuringSend = fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out").Status;
                return Task.FromResult<string?>("zalo-msg-42");
            });
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, new CapturingChatStream(), TestServerCallContext.Create());

        statusDuringSend.Should().Be("pending_send");
        var saved = fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out");
        saved.Status.Should().Be("sent");
        saved.ExternalMessageId.Should().Be("zalo-msg-42");
    }

    [Fact]
    public async Task Reply_marks_message_send_failed_when_channel_delivery_fails()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "pzl_page_1:pzl_conv_1", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        channel.SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new HttpRequestException("zalo unavailable"));
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, new CapturingChatStream(), TestServerCallContext.Create());

        var saved = fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out");
        saved.Status.Should().Be("send_failed");
        saved.ExternalMessageId.Should().BeNull();
        fx.Db.AgentTraces.Should().Contain(t => t.Phase == "send_failed");
        fx.Db.AgentTraces.Should().NotContain(t => t.Phase == "sent");
    }

    [Fact]
    public async Task Reply_marks_message_send_failed_when_channel_delivery_is_cancelled()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "pzl_page_1:pzl_conv_1", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        channel.SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new OperationCanceledException("shutdown"));
        var service = new ChatAgentGrpcService(
            BuildAgent(claude), BuildReviewer(), fx.Db, clock, BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance, channel);

        var act = () => service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, new CapturingChatStream(), TestServerCallContext.Create());

        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out")
            .Status.Should().Be("send_failed");
        fx.Db.AgentSessions.Single().Status.Should().Be(AgentSessionStatuses.Failed);
    }

    [Fact]
    public async Task Reply_does_not_send_when_blocked()
    {
        // EARS[WHEN the reply is blocked (safety/toxicity) THE SYSTEM SHALL not send it to the channel]
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "pancake", "thread-block", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        var service = new ChatAgentGrpcService(
            BuildAgent(claude, toxicityBlocked: true),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "spam spam",
        }, stream, TestServerCallContext.Create());

        await channel.DidNotReceive().SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reply_holds_pending_approval_when_reviewer_needs_human()
    {
        // Review-gate P2: KB rỗng → Escalate → LLM critic; verdict needs_human → hold, KHÔNG gửi kênh.
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "pancake", "page_1:thread-hold", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer("""{"verdict":"needs_human","reason":"thieu du lieu doi chieu"}"""),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "hoc phi bao nhieu",
        }, stream, TestServerCallContext.Create());

        await channel.DidNotReceive().SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        var saved = fx.Db.Messages.Single(m => m.ConversationId == conversation.Id && m.Direction == "out");
        saved.Status.Should().Be("pending_approval");
        fx.Db.AgentTraces.Should().Contain(t => t.Phase == "held_for_review");
    }

    [Fact]
    public async Task Reply_blocks_and_does_not_send_when_reviewer_rejects()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "pancake", "page_1:thread-rej", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer("""{"verdict":"reject","reason":"bia cam ket"}"""),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "cam ket dau ra?",
        }, stream, TestServerCallContext.Create());

        await channel.DidNotReceive().SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.Db.Messages.Single(m => m.Direction == "out").Status.Should().Be("blocked");
        fx.Db.AgentTraces.Should().Contain(t => t.Phase == "review_rejected");
    }

    [Fact]
    public async Task Reply_holds_when_reviewer_llm_unavailable_fail_closed()
    {
        // QĐ3 fail-closed: reviewer chết → hold chờ người, tuyệt đối không gửi.
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "pancake", "page_1:thread-down", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        var reviewerClaude = Substitute.For<IClaudeChatClient>();
        reviewerClaude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClaudeReply>>(_ => throw new HttpRequestException("provider down"));
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            new Clawbot.Agents.Core.Content.ContentReviewer(reviewerClaude, new CoreChat.LlmCallScope()),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, stream, TestServerCallContext.Create());

        await channel.DidNotReceive().SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.Db.Messages.Single(m => m.Direction == "out").Status.Should().Be("pending_approval");
    }

    [Fact]
    public async Task Reply_holds_when_reviewer_is_cancelled_fail_closed()
    {
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "zalo", "page_1:thread-review-timeout", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        var reviewerClaude = Substitute.For<IClaudeChatClient>();
        reviewerClaude.CompleteAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ChatTurn>>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns<Task<ClaudeReply>>(_ => throw new OperationCanceledException("review provider timeout"));
        var service = new ChatAgentGrpcService(
            BuildAgent(new CapturingClaude()),
            new Clawbot.Agents.Core.Content.ContentReviewer(reviewerClaude, new CoreChat.LlmCallScope()),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel);

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, new CapturingChatStream(), TestServerCallContext.Create());

        await channel.DidNotReceive().SendAsync(
            Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.Db.Messages.Single(m => m.Direction == "out").Status.Should().Be("pending_approval");
        fx.Db.AgentSessions.Single().Status.Should().Be(AgentSessionStatuses.Completed);
        fx.Db.AgentTraces.Should().ContainSingle(t =>
            t.Phase == "held_for_review" && (t.Message ?? string.Empty).Contains("review_timeout"));
    }

    [Fact]
    public async Task Reply_holds_all_when_tenant_requires_manual_approval()
    {
        // Review-gate P3: RequireChatReplyApproval on → hold MỌI reply (skip luôn LLM critic), không gửi.
        using var fx = new AgentServiceTestAppDb(TenantId);
        var conversation = Conversation.Open(TenantId, "pancake", "page_1:thread-manual", Now);
        fx.Db.Add(conversation);
        await fx.Db.SaveChangesAsync();

        var claude = new CapturingClaude();
        var clock = new FixedClock(Now);
        var channel = Substitute.For<Clawbot.SharedKernel.Channels.IChannelAdapter>();
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(), // critic sẽ approve nếu được gọi — phase held_for_approval chứng minh nó bị skip
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance,
            channel,
            new FakeChatApprovalPolicy(true));
        var stream = new CapturingChatStream();

        await service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            ConversationId = conversation.Id.ToString("D"),
            UserText = "xin chao",
        }, stream, TestServerCallContext.Create());

        await channel.DidNotReceive().SendAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        fx.Db.Messages.Single(m => m.Direction == "out").Status.Should().Be("pending_approval");
        fx.Db.AgentTraces.Should().Contain(t => t.Phase == "held_for_approval");
    }

    [Fact]
    public async Task Reply_marks_session_failed_when_request_is_cancelled_during_llm_stream()
    {
        // Regression: live auto-reply requests used to leave agent_sessions permanently "running"
        // when the provider/RAG path hung and the caller cancelled the gRPC request.
        using var fx = new AgentServiceTestAppDb(TenantId);
        var clock = new FixedClock(Now);
        var claude = new HangingClaude();
        var service = new ChatAgentGrpcService(
            BuildAgent(claude),
            BuildReviewer(),
            fx.Db,
            clock,
            BuildLeadScorer(fx.Db, clock),
            NullLogger<ChatAgentGrpcService>.Instance);
        using var cts = new CancellationTokenSource();

        var replyTask = service.Reply(new ChatRequest
        {
            TenantId = TenantId.ToString("D"),
            UserText = "xin chao",
        }, new CapturingChatStream(), TestServerCallContext.Create(cts.Token));
        await claude.StreamStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cts.Cancel();

        var act = async () => await replyTask;
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded);
        fx.Db.AgentSessions.Should().ContainSingle();
        fx.Db.AgentSessions.Single().Status.Should().Be(AgentSessionStatuses.Failed);
        fx.Db.AgentSessions.Single().FinishedAt.Should().NotBeNull();
        fx.Db.AgentTraces.Should().ContainSingle(t => t.Phase == "timeout");
    }

    private sealed class FakeChatApprovalPolicy(bool required, bool bypassReview = false) : Clawbot.SharedKernel.Inbox.IChatApprovalPolicyResolver
    {
        public Task<bool> IsRequiredAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(required);

        public Task<bool> IsReviewGateBypassedAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(bypassReview);
    }

    private static Clawbot.Agents.Core.Content.ContentReviewer BuildReviewer(string? verdictJson = null)
    {
        var claude = Substitute.For<IClaudeChatClient>();
        claude.CompleteAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<ChatTurn>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply(verdictJson ?? """{"verdict":"approve","reason":"ok"}""", 1, 1, 0m));
        return new Clawbot.Agents.Core.Content.ContentReviewer(claude, new CoreChat.LlmCallScope());
    }

    private static CoreChat.ChatAgent BuildAgent(IClaudeChatClient claude) => BuildAgent(claude, toxicityBlocked: false);

    private static CoreChat.ChatAgent BuildAgent(IClaudeChatClient claude, bool toxicityBlocked)
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
            .Returns(toxicityBlocked);

        var spam = Substitute.For<ISpamDetector>();
        spam.EvaluateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new SpamSignal(false, 0f, null));

        var rag = Substitute.For<IRagRetriever>();
        rag.RetrieveAsync(Arg.Any<RagRequest>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RagChunk>());

        var cost = Substitute.For<ILlmCostTracker>();

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
            new AlwaysEnabledAgentToggleGate(),
            new CoreChat.LlmCallScope());
    }

    private static LeadAutoScorer BuildLeadScorer(Clawbot.Infrastructure.Persistence.AppDbContext db, IClock clock) =>
        new(db, new KeywordLeadSignalClassifier(), new CoreChat.LlmCallScope(), clock, NullLogger<LeadAutoScorer>.Instance);

    private sealed class HangingClaude : IClaudeChatClient
    {
        public TaskCompletionSource<bool> StreamStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ClaudeReply> CompleteAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            CancellationToken ct) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
            string systemPrompt,
            IReadOnlyList<ChatTurn> history,
            string userMessage,
            [EnumeratorCancellation] CancellationToken ct)
        {
            StreamStarted.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            yield break;
        }
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
