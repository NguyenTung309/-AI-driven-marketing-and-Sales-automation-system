using System.Net;
using System.Text;
using System.Text.Json;
using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

// Chuẩn OpenAI v2 (Responses API): request mapping messages->input/instructions và parse output_text.
public sealed class OpenAiResponsesChatClientTests
{
    private static ResolvedLlmConfig Config(string? baseUrl = "https://gateway.example/v1") =>
        new("openai-responses", "gpt-5", "sk-test", baseUrl, InputUsdPer1M: 1m, OutputUsdPer1M: 2m, MaxOutputTokens: 512);

    [Fact]
    public async Task CompleteAsync_posts_to_responses_endpoint_with_migrated_body()
    {
        var handler = new CapturingHandler("""
        {
          "id": "resp_1",
          "status": "completed",
          "output": [
            {"type":"reasoning","content":[]},
            {"type":"message","role":"assistant","content":[{"type":"output_text","text":"Xin chào"},{"type":"output_text","text":"!"}]}
          ],
          "usage": {"input_tokens": 10, "output_tokens": 5}
        }
        """);
        using var http = new HttpClient(handler);
        var client = new OpenAiResponsesChatClient(http, Config());

        var reply = await client.CompleteAsync(
            "system prompt",
            [new ChatTurn("user", "hi"), new ChatTurn("assistant", "hello")],
            "câu hỏi",
            CancellationToken.None);

        handler.LastUrl.Should().Be("https://gateway.example/v1/responses");
        handler.LastAuthorization.Should().Be("Bearer sk-test");
        using var doc = JsonDocument.Parse(handler.LastBody!);
        var root = doc.RootElement;
        root.GetProperty("model").GetString().Should().Be("gpt-5");
        root.GetProperty("instructions").GetString().Should().Be("system prompt");
        root.GetProperty("max_output_tokens").GetInt32().Should().Be(512);
        var input = root.GetProperty("input");
        input.GetArrayLength().Should().Be(3);
        input[0].GetProperty("role").GetString().Should().Be("user");
        input[0].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("input_text");
        input[1].GetProperty("role").GetString().Should().Be("assistant");
        input[1].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("output_text");
        input[2].GetProperty("content")[0].GetProperty("text").GetString().Should().Be("câu hỏi");

        reply.Text.Should().Be("Xin chào!");
        reply.InputTokens.Should().Be(10);
        reply.OutputTokens.Should().Be(5);
        reply.UsdCost.Should().Be((10m * 1m + 5m * 2m) / 1_000_000m);
    }

    [Fact]
    public async Task CompleteAsync_throws_http_exception_with_status_on_error()
    {
        var handler = new CapturingHandler("""{"error":{"message":"bad key"}}""", HttpStatusCode.Unauthorized);
        using var http = new HttpClient(handler);
        var client = new OpenAiResponsesChatClient(http, Config());

        var act = () => client.CompleteAsync("s", [], "hi", CancellationToken.None);

        (await act.Should().ThrowAsync<HttpRequestException>())
            .Which.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StreamAsync_emits_content_then_final_usage_chunk()
    {
        var handler = new CapturingHandler("""
        {"output":[{"type":"message","content":[{"type":"output_text","text":"ok"}]}],"usage":{"input_tokens":3,"output_tokens":1}}
        """);
        using var http = new HttpClient(handler);
        var client = new OpenAiResponsesChatClient(http, Config());

        var chunks = new List<ClaudeStreamChunk>();
        await foreach (var chunk in client.StreamAsync("s", [], "hi", CancellationToken.None))
            chunks.Add(chunk);

        chunks.Should().HaveCount(2);
        chunks[0].Text.Should().Be("ok");
        chunks[1].Final.Should().BeTrue();
        chunks[1].InputTokens.Should().Be(3);
        chunks[1].OutputTokens.Should().Be(1);
    }

    [Fact]
    public async Task CompleteAsync_reads_sse_stream_collecting_output_text_and_usage()
    {
        // Gateway streaming-only (aigatewayport): nội dung chỉ về qua SSE; reasoning deltas phải bị bỏ.
        var sse = string.Join("\n",
            """event: response.created""",
            """data: {"response":{"id":"resp_1","status":"in_progress"},"type":"response.created"}""",
            "",
            """event: response.reasoning_summary_text.delta""",
            """data: {"delta":"Thinking...","type":"response.reasoning_summary_text.delta"}""",
            "",
            """event: response.output_text.delta""",
            """data: {"delta":"Xin ","type":"response.output_text.delta"}""",
            "",
            """event: response.output_text.delta""",
            """data: {"delta":"chào","type":"response.output_text.delta"}""",
            "",
            """event: response.completed""",
            """data: {"response":{"id":"resp_1","status":"completed","usage":{"input_tokens":7,"output_tokens":2}},"type":"response.completed"}""",
            "");
        var handler = new CapturingHandler(sse, contentType: "text/event-stream");
        using var http = new HttpClient(handler);
        var client = new OpenAiResponsesChatClient(http, Config());

        var reply = await client.CompleteAsync("s", [], "hi", CancellationToken.None);

        using var doc = JsonDocument.Parse(handler.LastBody!);
        doc.RootElement.GetProperty("stream").GetBoolean().Should().BeTrue();
        reply.Text.Should().Be("Xin chào");
        reply.InputTokens.Should().Be(7);
        reply.OutputTokens.Should().Be(2);
    }

    private sealed class CapturingHandler(string responseBody, HttpStatusCode status = HttpStatusCode.OK, string contentType = "application/json") : HttpMessageHandler
    {
        public string? LastUrl { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUrl = request.RequestUri!.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, contentType),
            };
        }
    }
}
