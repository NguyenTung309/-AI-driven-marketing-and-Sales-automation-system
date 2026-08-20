using System.Net;
using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

// Adapter review theo từng provider: dựng envelope từ phản hồi HTTP.
// Dùng HttpClient giả (StubHandler) để chạy full parse mà không gọi mạng thật.
public sealed class ContentReviewCompletionAdaptersTests
{
    private static ResolvedLlmConfig Config(string provider, string model = "gpt-5", string? baseUrl = "https://example.test/v1") =>
        new(provider, model, "sk-test-key", baseUrl, InputUsdPer1M: 3m, OutputUsdPer1M: 15m);

    private static ReviewPromptPart System() => ReviewPromptPart.TrustedSystem("You are a strict reviewer.");

    private static IReadOnlyList<ReviewPromptPart> UserText(string text) =>
        [ReviewPromptPart.UntrustedText(text)];

    private static IReadOnlyList<ReviewPromptPart> Image(string id = "img-1") =>
        [ReviewPromptPart.UntrustedImageBytes(id, "image/png", [1, 2, 3, 4])];

    // ---------- Factory ----------

    [Theory]
    [InlineData("anthropic", typeof(AnthropicContentReviewClient))]
    [InlineData("openai-responses", typeof(OpenAiResponsesContentReviewClient))]
    [InlineData("openai", typeof(OpenAiChatContentReviewClient))]
    [InlineData("openai-compatible", typeof(OpenAiChatContentReviewClient))]
    [InlineData("OpenAI", typeof(OpenAiChatContentReviewClient))]  // provider được lower-case
    public void Factory_MapsProviderToClient(string provider, Type expected)
    {
        var factory = new ContentReviewCompletionClientFactory(allowPrivateBaseUrls: true);

        var client = factory.Create(Config(provider, baseUrl: "https://public.example.com/v1"));

        client.Should().BeOfType(expected);
    }

    [Fact]
    public void Factory_UnknownProvider_Throws()
    {
        var factory = new ContentReviewCompletionClientFactory();

        var act = () => factory.Create(Config("cohere", baseUrl: "https://public.example.com/v1"));

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*content_review_provider_unsupported:cohere*");
    }

    [Fact]
    public void Factory_NullConfig_Throws()
    {
        var factory = new ContentReviewCompletionClientFactory();

        var act = () => factory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ---------- OpenAI chat completions ----------

    [Fact]
    public async Task OpenAiChat_TerminalSuccess_ParsesTextAndUsage()
    {
        var body = """
            {"model":"gpt-5","choices":[
              {"finish_reason":"stop","message":{"content":"verdict"}}
            ],"usage":{"prompt_tokens":100,"completion_tokens":20}}
            """;
        var client = new OpenAiChatContentReviewClient(StubClient(body), Config("openai"));

        var env = await client.CompleteTextAsync(System(), UserText("review this"), default);

        env.ObservedTerminalSuccess.Should().BeTrue();
        env.RawText.Should().Be("verdict");
        env.FinishReason.Should().Be("stop");
        env.InputTokens.Should().Be(100);
        env.OutputTokens.Should().Be(20);
        env.IsEstimated.Should().BeFalse();
        // cost = (100*3 + 20*15)/1e6
        env.UsdCost.Should().BeApproximately((100 * 3m + 20 * 15m) / 1_000_000m, 1e-9m);
    }

    [Fact]
    public async Task OpenAiChat_NoUsage_EstimatesTokensAndFlagsEstimated()
    {
        var body = """
            {"choices":[{"finish_reason":"stop","message":{"content":"ok verdict text"}}]}
            """;
        var client = new OpenAiChatContentReviewClient(StubClient(body), Config("openai"));

        var env = await client.CompleteTextAsync(System(), UserText("review this"), default);

        env.IsEstimated.Should().BeTrue();
        env.InputTokens.Should().BeGreaterThan(0);
        env.OutputTokens.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OpenAiChat_RefusalFinishReason_NotTerminal()
    {
        var body = """
            {"choices":[{"finish_reason":"refusal","message":{"content":"no"}}]}
            """;
        var client = new OpenAiChatContentReviewClient(StubClient(body), Config("openai"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.IsRefused.Should().BeTrue();
        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiChat_LengthFinishReason_MarksTruncated()
    {
        var body = """
            {"choices":[{"finish_reason":"length","message":{"content":"partial"}}]}
            """;
        var client = new OpenAiChatContentReviewClient(StubClient(body), Config("openai"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.IsTruncated.Should().BeTrue();
        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiChat_ErrorStatus_ReturnsIncomplete()
    {
        var client = new OpenAiChatContentReviewClient(
            StubClient("server error", HttpStatusCode.InternalServerError), Config("openai"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeFalse();
        env.RawText.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAiChat_VisionUnsupportedHttp_ThrowsVisionUnsupported()
    {
        var client = new OpenAiChatContentReviewClient(
            StubClient("{\"error\":\"unsupported image modality\"}", HttpStatusCode.BadRequest),
            Config("openai"));

        var act = async () => await client.CompleteVisionAsync(System(), Image(), default);

        await act.Should().ThrowAsync<VisionUnsupportedException>();
    }

    [Fact]
    public async Task OpenAiChat_MultipleChoices_ReturnsIncomplete()
    {
        var body = """
            {"choices":[
              {"finish_reason":"stop","message":{"content":"a"}},
              {"finish_reason":"stop","message":{"content":"b"}}
            ]}
            """;
        var client = new OpenAiChatContentReviewClient(StubClient(body), Config("openai"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiChat_EmptyUntrustedText_Throws()
    {
        var client = new OpenAiChatContentReviewClient(StubClient("{}"), Config("openai"));

        var act = async () => await client.CompleteTextAsync(System(), [], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task OpenAiChat_Vision_SendsCanonicalPartIds()
    {
        var body = """
            {"choices":[{"finish_reason":"stop","message":{"content":"seen"}}]}
            """;
        var client = new OpenAiChatContentReviewClient(StubClient(body), Config("openai"));

        var env = await client.CompleteVisionAsync(System(), Image("img-42"), default);

        env.RequestedPartIds.Should().BeEquivalentTo("img-42");
        env.SentPartIds.Should().BeEquivalentTo("img-42");
    }

    [Fact]
    public async Task OpenAiChat_VisionWithoutImages_Throws()
    {
        var client = new OpenAiChatContentReviewClient(StubClient("{}"), Config("openai"));

        var act = async () => await client.CompleteVisionAsync(System(), UserText("no images"), default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------- Anthropic ----------

    [Fact]
    public async Task Anthropic_EndTurn_TerminalSuccess()
    {
        var body = """
            {"model":"claude-opus-5","stop_reason":"end_turn",
             "content":[{"type":"text","text":"verdict"}],
             "usage":{"input_tokens":50,"output_tokens":10}}
            """;
        var client = new AnthropicContentReviewClient(StubClient(body), Config("anthropic"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeTrue();
        env.RawText.Should().Be("verdict");
        env.Model.Should().Be("claude-opus-5");
        env.InputTokens.Should().Be(50);
        env.OutputTokens.Should().Be(10);
    }

    [Fact]
    public async Task Anthropic_MaxTokensStopReason_MarksTruncated()
    {
        var body = """
            {"stop_reason":"max_tokens","content":[{"type":"text","text":"partial"}]}
            """;
        var client = new AnthropicContentReviewClient(StubClient(body), Config("anthropic"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.IsTruncated.Should().BeTrue();
        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Anthropic_MalformedJson_ReturnsIncomplete()
    {
        var client = new AnthropicContentReviewClient(StubClient("not json"), Config("anthropic"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    // ---------- OpenAI Responses (SSE) ----------

    [Fact]
    public async Task OpenAiResponses_SseCompleted_TerminalSuccess()
    {
        var sse = "event: response.output_text.delta\n"
            + "data: {\"delta\":\"ver\"}\n\n"
            + "event: response.output_text.delta\n"
            + "data: {\"delta\":\"dict\"}\n\n"
            + "event: response.completed\n"
            + "data: {\"response\":{\"status\":\"completed\",\"model\":\"gpt-5\",\"usage\":{\"input_tokens\":7,\"output_tokens\":3}}}\n\n";
        var client = new OpenAiResponsesContentReviewClient(
            StubClient(sse, contentType: "text/event-stream"), Config("openai-responses"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeTrue();
        env.RawText.Should().Be("verdict");
        env.InputTokens.Should().Be(7);
        env.OutputTokens.Should().Be(3);
    }

    [Fact]
    public async Task OpenAiResponses_SseIncomplete_MarksTruncated()
    {
        var sse = "event: response.output_text.delta\n"
            + "data: {\"delta\":\"partial\"}\n\n"
            + "event: response.incomplete\n"
            + "data: {\"response\":{\"status\":\"incomplete\"}}\n\n";
        var client = new OpenAiResponsesContentReviewClient(
            StubClient(sse, contentType: "text/event-stream"), Config("openai-responses"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.IsTruncated.Should().BeTrue();
        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiResponses_NonStreamCompletedJson_ParsesText()
    {
        var body = """
            {"status":"completed","output_text":"verdict","model":"gpt-5",
             "usage":{"input_tokens":4,"output_tokens":2}}
            """;
        var client = new OpenAiResponsesContentReviewClient(
            StubClient(body, contentType: "application/json"), Config("openai-responses"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeTrue();
        env.RawText.Should().Be("verdict");
    }

    [Fact]
    public async Task OpenAiResponses_NonStreamNotCompleted_ReturnsIncomplete()
    {
        var body = """{"status":"in_progress","output_text":"x"}""";
        var client = new OpenAiResponsesContentReviewClient(
            StubClient(body, contentType: "application/json"), Config("openai-responses"));

        var env = await client.CompleteTextAsync(System(), UserText("x"), default);

        env.ObservedTerminalSuccess.Should().BeFalse();
    }

    // ---------- Fake HTTP plumbing ----------

    private static HttpClient StubClient(
        string body,
        HttpStatusCode status = HttpStatusCode.OK,
        string contentType = "application/json") =>
        new(new StubHandler(body, status, contentType));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        private readonly HttpStatusCode _status;
        private readonly string _contentType;

        public StubHandler(string body, HttpStatusCode status, string contentType)
        {
            _body = body;
            _status = status;
            _contentType = contentType;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, _contentType),
            });
    }
}
