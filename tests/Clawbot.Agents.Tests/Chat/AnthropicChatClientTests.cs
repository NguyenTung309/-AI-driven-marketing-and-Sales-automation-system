using System.Net;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Chat;

public sealed class AnthropicChatClientTests
{
    [Fact]
    public async Task CompleteAsync_sends_messages_request_and_maps_usage_cost()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "content": [
                    { "type": "text", "text": "Xin " },
                    { "type": "text", "text": "chao" }
                  ],
                  "usage": { "input_tokens": 100, "output_tokens": 40 }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        });
        var sut = CreateClient(handler);

        var result = await sut.CompleteAsync(
            "system prompt",
            new[] { new ChatTurn("user", "old question"), new ChatTurn("assistant", "old answer") },
            "new question");

        result.Should().Be(new ClaudeReply("Xin chao", 100, 40, 0.0009m, "claude-test"));
        handler.Method.Should().Be(HttpMethod.Post);
        handler.RequestUri.Should().Be("https://anthropic.test/v1/messages");
        handler.ApiKey.Should().Be("test-key");
        handler.AnthropicVersion.Should().Be("2023-06-01");
        handler.Accept.Should().Contain("application/json");

        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("model").GetString().Should().Be("claude-test");
        body.RootElement.GetProperty("max_tokens").GetInt32().Should().Be(1024);
        body.RootElement.TryGetProperty("temperature", out _).Should().BeFalse();
        body.RootElement.GetProperty("system").GetString().Should().Be("system prompt");

        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        messages.Should().HaveCount(3);
        messages[0].GetProperty("role").GetString().Should().Be("user");
        messages[0].GetProperty("content").GetString().Should().Be("old question");
        messages[1].GetProperty("role").GetString().Should().Be("assistant");
        messages[2].GetProperty("role").GetString().Should().Be("user");
        messages[2].GetProperty("content").GetString().Should().Be("new question");
    }

    [Fact]
    public async Task StreamAsync_sends_stream_request_and_yields_text_deltas_with_usage_cost()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                event: message_start
                data: {"type":"message_start","message":{"usage":{"input_tokens":12}}}

                event: content_block_delta
                data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"Xin "}}

                event: content_block_delta
                data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"chao"}}

                event: message_delta
                data: {"type":"message_delta","usage":{"output_tokens":5}}

                event: message_stop
                data: {"type":"message_stop"}

                """,
                Encoding.UTF8,
                "text/event-stream"),
        });
        var sut = CreateClient(handler);

        var chunks = new List<ClaudeStreamChunk>();
        await foreach (var chunk in sut.StreamAsync(
                           "system prompt",
                           Array.Empty<ChatTurn>(),
                           "new question"))
        {
            chunks.Add(chunk);
        }

        chunks.Where(c => !c.Final).Select(c => c.Text).Should().Equal("Xin ", "chao");
        var final = chunks.Should().ContainSingle(c => c.Final).Subject;
        final.Text.Should().BeEmpty();
        final.InputTokens.Should().Be(12);
        final.OutputTokens.Should().Be(5);
        final.UsdCost.Should().Be(0.000111m);
        final.Model.Should().Be("claude-test");

        handler.Accept.Should().Contain("text/event-stream");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
    }

    private static AnthropicChatClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new ResolvedLlmConfig(
            Provider: "anthropic",
            Model: "claude-test",
            ApiKey: "test-key",
            BaseUrl: "https://anthropic.test",
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m));

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? RequestUri { get; private set; }
        public string? ApiKey { get; private set; }
        public string? AnthropicVersion { get; private set; }
        public string? Accept { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri?.ToString();
            ApiKey = request.Headers.TryGetValues("x-api-key", out var apiKey) ? apiKey.Single() : null;
            AnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var version) ? version.Single() : null;
            Accept = string.Join(",", request.Headers.Accept.Select(v => v.MediaType));
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return respond(request);
        }
    }
}
