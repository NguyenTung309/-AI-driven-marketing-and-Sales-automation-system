using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using OpenAI;
using OpenAI.Chat;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

// Exercises the OpenAI adapter's request shaping + usage/cost mapping through a stubbed transport.
// StreamAsync resolves the full completion (SDK 2.11.0 exposes no public streaming-usage option),
// so the final chunk MUST still carry real token usage — guarding the cost-cap regression.
public sealed class OpenAiChatClientTests
{
    private const string CompletionJson =
        """
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "created": 1700000000,
          "model": "gpt-test",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": "Xin chao" }, "finish_reason": "stop" }
          ],
          "usage": { "prompt_tokens": 100, "completion_tokens": 40, "total_tokens": 140 }
        }
        """;

    [Fact]
    public async Task CompleteAsync_maps_usage_and_cost_from_response()
    {
        var sut = CreateClient(out _);

        var reply = await sut.CompleteAsync(
            "system prompt",
            new[] { new ChatTurn("user", "old"), new ChatTurn("assistant", "ans") },
            "new question");

        reply.Should().Be(new ClaudeReply("Xin chao", 100, 40, 0.0009m, "gpt-test"));
    }

    [Fact]
    public async Task StreamAsync_yields_content_then_final_chunk_with_usage_cost()
    {
        var sut = CreateClient(out _);

        var chunks = new List<ClaudeStreamChunk>();
        await foreach (var chunk in sut.StreamAsync("system prompt", Array.Empty<ChatTurn>(), "new question"))
            chunks.Add(chunk);

        chunks.Where(c => !c.Final).Select(c => c.Text).Should().Equal("Xin chao");
        var final = chunks.Should().ContainSingle(c => c.Final).Subject;
        final.InputTokens.Should().Be(100);
        final.OutputTokens.Should().Be(40);
        final.UsdCost.Should().Be(0.0009m);
        final.Model.Should().Be("gpt-test");
    }

    private static OpenAiChatClient CreateClient(out StubHandler handler)
    {
        handler = new StubHandler(CompletionJson);
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
        };
        var chatClient = new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options);
        var config = new ResolvedLlmConfig(
            Provider: "openai",
            Model: "gpt-test",
            ApiKey: "test-key",
            BaseUrl: null,
            MaxTokens: 256,
            Temperature: 0.5m,
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);
        return new OpenAiChatClient(chatClient, config);
    }

    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
    }
}
