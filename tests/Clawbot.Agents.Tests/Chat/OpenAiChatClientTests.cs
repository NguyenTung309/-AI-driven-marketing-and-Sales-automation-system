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

    private const string ContentArrayCompletionJson =
        """
        {
          "id": "chatcmpl-1",
          "object": "chat.completion",
          "created": 1700000000,
          "model": "gpt-test",
          "choices": [
            { "index": 0, "message": { "role": "assistant", "content": [{ "type": "text", "text": "Xin" }, { "type": "text", "text": " chao" }] }, "finish_reason": "stop" }
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

    [Theory]
    [InlineData("test-key", "test-key")]
    [InlineData(" Bearer test-key ", "test-key")]
    public void NormalizeApiKey_strips_accidental_bearer_prefix(string input, string expected)
    {
        OpenAiChatClient.NormalizeApiKey(input).Should().Be(expected);
    }

    [Fact]
    public async Task CompleteAsync_sends_bearer_token_to_openai_compatible_endpoint()
    {
        var handler = new StubHandler(CompletionJson);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://aigatewayport.com/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
        };
        var chatClient = new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options);
        var config = new ResolvedLlmConfig(
            Provider: "openai-compatible",
            Model: "gpt-test",
            ApiKey: "test-key",
            BaseUrl: "https://aigatewayport.com/v1",
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);
        var sut = new OpenAiChatClient(chatClient, config);

        await sut.CompleteAsync("system prompt", Array.Empty<ChatTurn>(), "new question");

        handler.AuthorizationScheme.Should().Be("Bearer");
        handler.AuthorizationParameter.Should().Be("test-key");
        handler.RequestPath.Should().Be("/v1/chat/completions");
    }

    [Fact]
    public async Task CompleteAsync_omits_token_cap_when_max_output_tokens_is_not_configured()
    {
        var sut = CreateClient(out var handler);

        await sut.CompleteAsync("system prompt", Array.Empty<ChatTurn>(), "new question");

        handler.RequestBody.Should().NotContain("max_completion_tokens");
    }

    [Fact]
    public async Task CompleteAsync_sends_token_cap_when_max_output_tokens_is_configured()
    {
        var handler = new StubHandler(CompletionJson);
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
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m,
            MaxOutputTokens: 123);
        var sut = new OpenAiChatClient(chatClient, config);

        await sut.CompleteAsync("system prompt", Array.Empty<ChatTurn>(), "new question");

        handler.RequestBody.Should().Contain("\"max_completion_tokens\":123");
    }

    [Fact]
    public async Task CompleteAsync_uses_default_rates_when_config_rates_are_missing()
    {
        var handler = new StubHandler(CompletionJson);
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
            InputUsdPer1M: null,
            OutputUsdPer1M: null);
        var sut = new OpenAiChatClient(chatClient, config);

        var reply = await sut.CompleteAsync("system prompt", Array.Empty<ChatTurn>(), "new question");

        reply.UsdCost.Should().Be(0.0009m);
    }

    [Fact]
    public async Task CompleteAsync_falls_back_to_direct_openai_compatible_shape_on_sdk_auth_failure()
    {
        var sdkHandler = new StubHandler("{}", HttpStatusCode.Forbidden);
        var directHandler = new StubHandler(CompletionJson);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://aigatewayport.com/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(sdkHandler)),
        };
        var chatClient = new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options);
        var config = new ResolvedLlmConfig(
            Provider: "openai-compatible",
            Model: "gpt-test",
            ApiKey: "Bearer test-key",
            BaseUrl: "https://aigatewayport.com/v1",
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m,
            MaxOutputTokens: 123);
        var sut = new OpenAiChatClient(chatClient, config, new HttpClient(directHandler));

        var reply = await sut.CompleteAsync(
            "system prompt",
            new[] { new ChatTurn("user", "old"), new ChatTurn("assistant", "ans") },
            "new question");

        reply.Should().Be(new ClaudeReply("Xin chao", 100, 40, 0.0009m, "gpt-test"));
        directHandler.AuthorizationScheme.Should().Be("Bearer");
        directHandler.AuthorizationParameter.Should().Be("test-key");
        directHandler.RequestPath.Should().Be("/v1/chat/completions");
        directHandler.RequestBody.Should().Contain("\"model\":\"gpt-test\"");
        directHandler.RequestBody.Should().Contain("\"content\":[{\"type\":\"text\",\"text\":\"system prompt\\n\\nnew question\"}]");
        directHandler.RequestBody.Should().NotContain("\"role\":\"system\"");
        directHandler.RequestBody.Should().Contain("\"max_tokens\":123");
        directHandler.RequestBody.Should().NotContain("max_completion_tokens");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task CompleteAsync_falls_back_for_custom_openai_base_url_on_sdk_request_failure(HttpStatusCode sdkStatusCode)
    {
        var sdkHandler = new StubHandler("{}", sdkStatusCode);
        var directHandler = new StubHandler(ContentArrayCompletionJson);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://aigatewayport.com/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(sdkHandler)),
        };
        var chatClient = new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options);
        var config = new ResolvedLlmConfig(
            Provider: "openai",
            Model: "gpt-test",
            ApiKey: "test-key",
            BaseUrl: "https://aigatewayport.com/v1",
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);
        var sut = new OpenAiChatClient(chatClient, config, new HttpClient(directHandler));

        var reply = await sut.CompleteAsync("system prompt", Array.Empty<ChatTurn>(), "new question");

        reply.Text.Should().Be("Xin chao");
        directHandler.RequestPath.Should().Be("/v1/chat/completions");
    }

    [Fact]
    public async Task CompleteAsync_does_not_fallback_for_official_openai_endpoint()
    {
        var sdkHandler = new StubHandler("{}", HttpStatusCode.Forbidden);
        var directHandler = new StubHandler(CompletionJson);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://api.openai.com/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(sdkHandler)),
        };
        var chatClient = new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options);
        var config = new ResolvedLlmConfig(
            Provider: "openai",
            Model: "gpt-test",
            ApiKey: "test-key",
            BaseUrl: "https://api.openai.com/v1",
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);
        var sut = new OpenAiChatClient(chatClient, config, new HttpClient(directHandler));

        var act = () => sut.CompleteAsync("system prompt", Array.Empty<ChatTurn>(), "new question");

        await act.Should().ThrowAsync<ClientResultException>();
        directHandler.RequestPath.Should().BeNull();
    }

    [Fact]
    public async Task StreamAsync_uses_direct_fallback_and_keeps_usage_when_sdk_auth_fails()
    {
        var sdkHandler = new StubHandler("{}", HttpStatusCode.Forbidden);
        var directHandler = new StubHandler(CompletionJson);
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://aigatewayport.com/v1"),
            Transport = new HttpClientPipelineTransport(new HttpClient(sdkHandler)),
        };
        var chatClient = new ChatClient("gpt-test", new ApiKeyCredential("test-key"), options);
        var config = new ResolvedLlmConfig(
            Provider: "openai-compatible",
            Model: "gpt-test",
            ApiKey: "test-key",
            BaseUrl: "https://aigatewayport.com/v1",
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);
        var sut = new OpenAiChatClient(chatClient, config, new HttpClient(directHandler));

        var chunks = new List<ClaudeStreamChunk>();
        await foreach (var chunk in sut.StreamAsync("system prompt", Array.Empty<ChatTurn>(), "new question"))
            chunks.Add(chunk);

        chunks.Where(c => !c.Final).Select(c => c.Text).Should().Equal("Xin chao");
        var final = chunks.Should().ContainSingle(c => c.Final).Subject;
        final.InputTokens.Should().Be(100);
        final.OutputTokens.Should().Be(40);
        final.UsdCost.Should().Be(0.0009m);
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
            InputUsdPer1M: 3m,
            OutputUsdPer1M: 15m);
        return new OpenAiChatClient(chatClient, config);
    }

    private sealed class StubHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }
        public string? AuthorizationParameter { get; private set; }
        public string? RequestPath { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestPath = request.RequestUri?.AbsolutePath;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
