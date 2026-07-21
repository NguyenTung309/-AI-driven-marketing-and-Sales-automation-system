using System.Net;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Content;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content;

// Phase 2.7 RED: provider review adapters must preserve trusted/untrusted roles, terminal finish
// metadata, usage/cost, and exact requested/sent part ID sets. Implementation lands in 2.8.
public sealed class ContentReviewCompletionAdapterTests
{
    private const string ClosedApproveJson = """{"verdict":"approve","reason":"ok"}""";

    [Theory]
    [InlineData("openai")]
    [InlineData("openai-compatible")]
    public async Task OpenAiChat_text_keeps_system_role_separate_from_untrusted_user(string provider)
    {
        var handler = new CapturingHandler(OpenAiChatCompletionJson(
            content: ClosedApproveJson,
            finishReason: "stop",
            promptTokens: 12,
            completionTokens: 8));
        var sut = new OpenAiChatContentReviewClient(new HttpClient(handler), Config(provider, "gpt-4o-mini"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("TRUSTED_SYSTEM"),
            [ReviewPromptPart.UntrustedText("UNTRUSTED_BODY")],
            CancellationToken.None);

        handler.RequestPath.Should().Be("/v1/chat/completions");
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var messages = doc.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        messages[0].GetProperty("role").GetString().Should().Be("system");
        messages[0].GetProperty("content").GetString().Should().Contain("TRUSTED_SYSTEM");
        messages[0].GetProperty("content").GetString().Should().NotContain("UNTRUSTED_BODY");
        var lastUser = messages[messages.GetArrayLength() - 1];
        lastUser.GetProperty("role").GetString().Should().Be("user");
        var userContent = lastUser.GetProperty("content").ToString();
        userContent.Should().Contain("UNTRUSTED_BODY");
        userContent.Should().NotContain("TRUSTED_SYSTEM");
        handler.RequestBody.Should().NotContain("TRUSTED_SYSTEM\\n\\nUNTRUSTED_BODY");

        envelope.ObservedTerminalSuccess.Should().BeTrue();
        envelope.FinishReason.Should().Be(ReviewCompletionFinishReasons.Stop);
        envelope.IsRefused.Should().BeFalse();
        envelope.IsContentFiltered.Should().BeFalse();
        envelope.IsTruncated.Should().BeFalse();
        envelope.RawText.Should().Be(ClosedApproveJson);
        envelope.InputTokens.Should().Be(12);
        envelope.OutputTokens.Should().Be(8);
        envelope.UsdCost.Should().Be(0.000156m); // 12*3 + 8*15 / 1e6
        envelope.Model.Should().Be("gpt-4o-mini");
        envelope.RequestedPartIds.Should().BeEmpty();
        envelope.SentPartIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("content_filter", true, false, false)]
    [InlineData("refusal", false, true, false)]
    [InlineData("length", false, false, true)]
    [InlineData("max_tokens", false, false, true)]
    public async Task OpenAiChat_text_maps_non_stop_finish_to_fail_closed_flags(
        string finishReason,
        bool contentFiltered,
        bool refused,
        bool truncated)
    {
        var handler = new CapturingHandler(OpenAiChatCompletionJson(
            content: ClosedApproveJson,
            finishReason: finishReason,
            promptTokens: 1,
            completionTokens: 1));
        var sut = new OpenAiChatContentReviewClient(new HttpClient(handler), Config("openai", "gpt-4o"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("sys"),
            [ReviewPromptPart.UntrustedText("body")],
            CancellationToken.None);

        envelope.ObservedTerminalSuccess.Should().BeFalse();
        envelope.IsContentFiltered.Should().Be(contentFiltered);
        envelope.IsRefused.Should().Be(refused);
        envelope.IsTruncated.Should().Be(truncated);
        envelope.FinishReason.Should().Be(finishReason);
    }

    [Fact]
    public async Task OpenAiChat_text_rejects_multiple_choices()
    {
        var json =
            """
            {
              "id": "chatcmpl-1",
              "object": "chat.completion",
              "model": "gpt-4o",
              "choices": [
                { "index": 0, "message": { "role": "assistant", "content": "{\"verdict\":\"approve\",\"reason\":\"a\"}" }, "finish_reason": "stop" },
                { "index": 1, "message": { "role": "assistant", "content": "{\"verdict\":\"approve\",\"reason\":\"b\"}" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 1, "completion_tokens": 1, "total_tokens": 2 }
            }
            """;
        var handler = new CapturingHandler(json);
        var sut = new OpenAiChatContentReviewClient(new HttpClient(handler), Config("openai", "gpt-4o"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("sys"),
            [ReviewPromptPart.UntrustedText("body")],
            CancellationToken.None);

        envelope.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiChat_vision_records_canonical_requested_and_sent_part_ids()
    {
        var handler = new CapturingHandler(OpenAiChatCompletionJson(
            content: """{"verdict":"approve","reason":"img ok","reviewedPartIds":["asset-a","frame-1"]}""",
            finishReason: "stop",
            promptTokens: 20,
            completionTokens: 10));
        var sut = new OpenAiChatContentReviewClient(new HttpClient(handler), Config("openai", "gpt-4o"));
        var bytesA = Encoding.UTF8.GetBytes("png-a");
        var bytesB = Encoding.UTF8.GetBytes("png-b");

        var envelope = await sut.CompleteVisionAsync(
            ReviewPromptPart.TrustedSystem("TRUSTED_VISION"),
            [
                ReviewPromptPart.UntrustedText("caption"),
                ReviewPromptPart.UntrustedImageBytes("asset-a", "image/png", bytesA),
                ReviewPromptPart.UntrustedImageBytes("frame-1", "image/jpeg", bytesB),
                // Duplicate id must collapse to a single canonical requested/sent entry.
                ReviewPromptPart.UntrustedImageBytes("asset-a", "image/png", bytesA),
            ],
            CancellationToken.None);

        envelope.RequestedPartIds.Should().Equal("asset-a", "frame-1");
        envelope.SentPartIds.Should().Equal("asset-a", "frame-1");
        envelope.ObservedTerminalSuccess.Should().BeTrue();
        envelope.FinishReason.Should().Be(ReviewCompletionFinishReasons.Stop);

        using var doc = JsonDocument.Parse(handler.RequestBody!);
        var messages = doc.RootElement.GetProperty("messages");
        messages[0].GetProperty("role").GetString().Should().Be("system");
        var userContent = messages[messages.GetArrayLength() - 1].GetProperty("content");
        userContent.ValueKind.Should().Be(JsonValueKind.Array);
        var imageCount = 0;
        foreach (var part in userContent.EnumerateArray())
        {
            if (part.TryGetProperty("type", out var type) && type.GetString() is "image_url" or "input_image")
                imageCount++;
        }

        imageCount.Should().Be(2);
    }

    [Fact]
    public async Task Anthropic_text_uses_separate_system_field_and_end_turn_only()
    {
        var handler = new CapturingHandler(AnthropicMessagesJson(
            text: ClosedApproveJson,
            stopReason: "end_turn",
            inputTokens: 11,
            outputTokens: 7));
        var sut = new AnthropicContentReviewClient(new HttpClient(handler), Config("anthropic", "claude-sonnet-4-5"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("TRUSTED_SYSTEM"),
            [ReviewPromptPart.UntrustedText("UNTRUSTED_BODY")],
            CancellationToken.None);

        handler.RequestPath.Should().EndWith("/v1/messages");
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        doc.RootElement.GetProperty("system").GetString().Should().Contain("TRUSTED_SYSTEM");
        doc.RootElement.GetProperty("system").GetString().Should().NotContain("UNTRUSTED_BODY");
        var messages = doc.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(1);
        messages[0].GetProperty("role").GetString().Should().Be("user");
        messages[0].GetProperty("content").ToString().Should().Contain("UNTRUSTED_BODY");
        messages[0].GetProperty("content").ToString().Should().NotContain("TRUSTED_SYSTEM");

        envelope.ObservedTerminalSuccess.Should().BeTrue();
        envelope.FinishReason.Should().Be(ReviewCompletionFinishReasons.EndTurn);
        envelope.InputTokens.Should().Be(11);
        envelope.OutputTokens.Should().Be(7);
        envelope.UsdCost.Should().Be(0.000138m); // 11*3 + 7*15 / 1e6
        envelope.Model.Should().Be("claude-sonnet-4-5");
    }

    [Theory]
    [InlineData("max_tokens")]
    [InlineData("refusal")]
    [InlineData("stop_sequence")]
    public async Task Anthropic_text_non_end_turn_is_not_terminal_success(string stopReason)
    {
        var handler = new CapturingHandler(AnthropicMessagesJson(
            text: ClosedApproveJson,
            stopReason: stopReason,
            inputTokens: 1,
            outputTokens: 1));
        var sut = new AnthropicContentReviewClient(new HttpClient(handler), Config("anthropic", "claude-3-5-sonnet-latest"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("sys"),
            [ReviewPromptPart.UntrustedText("body")],
            CancellationToken.None);

        envelope.ObservedTerminalSuccess.Should().BeFalse();
        envelope.FinishReason.Should().Be(stopReason);
        if (stopReason == "max_tokens")
            envelope.IsTruncated.Should().BeTrue();
        if (stopReason == "refusal")
            envelope.IsRefused.Should().BeTrue();
    }

    [Fact]
    public async Task OpenAiResponses_requires_explicit_completed_terminal_event()
    {
        var sse =
            """
            event: response.output_text.delta
            data: {"type":"response.output_text.delta","delta":"{\"verdict\":\"approve\",\"reason\":\"ok\"}"}

            event: response.completed
            data: {"type":"response.completed","response":{"status":"completed","output_text":"{\"verdict\":\"approve\",\"reason\":\"ok\"}","usage":{"input_tokens":9,"output_tokens":4}}}

            """;
        var handler = new CapturingHandler(sse, mediaType: "text/event-stream");
        var sut = new OpenAiResponsesContentReviewClient(
            new HttpClient(handler),
            Config("openai-responses", "gpt-4o"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("TRUSTED_SYSTEM"),
            [ReviewPromptPart.UntrustedText("UNTRUSTED_BODY")],
            CancellationToken.None);

        handler.RequestPath.Should().EndWith("/responses");
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        doc.RootElement.GetProperty("instructions").GetString().Should().Contain("TRUSTED_SYSTEM");
        doc.RootElement.GetProperty("instructions").GetString().Should().NotContain("UNTRUSTED_BODY");
        doc.RootElement.GetProperty("input").ToString().Should().Contain("UNTRUSTED_BODY");

        envelope.ObservedTerminalSuccess.Should().BeTrue();
        envelope.FinishReason.Should().Be(ReviewCompletionFinishReasons.Stop);
        envelope.RawText.Should().Be(ClosedApproveJson);
        envelope.InputTokens.Should().Be(9);
        envelope.OutputTokens.Should().Be(4);
        envelope.UsdCost.Should().Be(0.000087m); // 9*3 + 4*15 / 1e6
    }

    [Fact]
    public async Task OpenAiResponses_eof_without_completed_event_fails_closed()
    {
        var sse =
            """
            event: response.output_text.delta
            data: {"type":"response.output_text.delta","delta":"{\"verdict\":\"approve\",\"reason\":\"ok\"}"}

            """;
        var handler = new CapturingHandler(sse, mediaType: "text/event-stream");
        var sut = new OpenAiResponsesContentReviewClient(
            new HttpClient(handler),
            Config("openai-responses", "gpt-4o"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("sys"),
            [ReviewPromptPart.UntrustedText("body")],
            CancellationToken.None);

        envelope.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task OpenAiResponses_malformed_sse_fails_closed()
    {
        var sse =
            """
            event: response.completed
            data: {not-json

            """;
        var handler = new CapturingHandler(sse, mediaType: "text/event-stream");
        var sut = new OpenAiResponsesContentReviewClient(
            new HttpClient(handler),
            Config("openai-responses", "gpt-4o"));

        var envelope = await sut.CompleteTextAsync(
            ReviewPromptPart.TrustedSystem("sys"),
            [ReviewPromptPart.UntrustedText("body")],
            CancellationToken.None);

        envelope.ObservedTerminalSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("anthropic", typeof(AnthropicContentReviewClient))]
    [InlineData("openai", typeof(OpenAiChatContentReviewClient))]
    [InlineData("openai-compatible", typeof(OpenAiChatContentReviewClient))]
    [InlineData("openai-responses", typeof(OpenAiResponsesContentReviewClient))]
    public void Factory_routes_provider_to_review_adapter(string provider, Type expectedType)
    {
        var factory = new ContentReviewCompletionClientFactory();
        var client = factory.Create(Config(provider, "model-x"));
        client.Should().BeOfType(expectedType);
    }

    [Fact]
    public void Factory_rejects_unknown_provider()
    {
        var factory = new ContentReviewCompletionClientFactory();
        var act = () => factory.Create(Config("unknown-provider", "model-x"));
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*content_review_provider_unsupported*");
    }

    private static ResolvedLlmConfig Config(string provider, string model) =>
        new(
            Provider: provider,
            Model: model,
            ApiKey: "test-key",
            BaseUrl: provider switch
            {
                "anthropic" => "https://api.anthropic.com",
                "openai-responses" => "https://api.openai.com/v1",
                _ => "https://api.openai.com/v1",
            },
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);

    private static string OpenAiChatCompletionJson(
        string content,
        string finishReason,
        int promptTokens,
        int completionTokens) =>
        $$"""
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "created": 1700000000,
          "model": "gpt-4o-mini",
          "choices": [
            {
              "index": 0,
              "message": { "role": "assistant", "content": {{JsonSerializer.Serialize(content)}} },
              "finish_reason": "{{finishReason}}"
            }
          ],
          "usage": {
            "prompt_tokens": {{promptTokens}},
            "completion_tokens": {{completionTokens}},
            "total_tokens": {{promptTokens + completionTokens}}
          }
        }
        """;

    private static string AnthropicMessagesJson(
        string text,
        string stopReason,
        int inputTokens,
        int outputTokens) =>
        $$"""
        {
          "id": "msg_1",
          "type": "message",
          "role": "assistant",
          "model": "claude-sonnet-4-5",
          "content": [ { "type": "text", "text": {{JsonSerializer.Serialize(text)}} } ],
          "stop_reason": "{{stopReason}}",
          "usage": { "input_tokens": {{inputTokens}}, "output_tokens": {{outputTokens}} }
        }
        """;

    private sealed class CapturingHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string mediaType = "application/json") : HttpMessageHandler
    {
        public string? RequestPath { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, mediaType),
            };
        }
    }
}
