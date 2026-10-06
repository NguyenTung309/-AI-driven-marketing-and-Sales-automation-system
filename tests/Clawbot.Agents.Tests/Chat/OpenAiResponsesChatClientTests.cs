using System.Net;
using System.Text;
using Clawbot.Agents.Core.Chat;
using Clawbot.Agents.Core.Orchestrator;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Chat;

public sealed class OpenAiResponsesChatClientTests
{
    private static readonly ResolvedLlmConfig DummyConfig = new(
        Provider: "openai-responses",
        Model: "gpt-5.5",
        ApiKey: "test-key",
        BaseUrl: "https://example.com/v1",
        InputUsdPer1M: 3m,
        OutputUsdPer1M: 15m);

    [Fact]
    public void ParseReply_WithFunctionCall_SynthesizesReActJson()
    {
        var client = new OpenAiResponsesChatClient(new HttpClient(new StubHandler()), DummyConfig);
        var jsonBody = """
            {
                "output": [
                    {
                        "type": "function_call",
                        "name": "research-agent",
                        "arguments": "{\"geo\":\"VN\",\"keywords\":[\"hsk\"]}"
                    }
                ],
                "usage": {
                    "input_tokens": 100,
                    "output_tokens": 20
                }
            }
            """;

        var reply = client.ParseReply(jsonBody);

        reply.Text.Should().Be("{\"tool\":\"research-agent\",\"args\":{\"geo\":\"VN\",\"keywords\":[\"hsk\"]}}");
        ReActAction.TryParse(reply.Text, out var action).Should().BeTrue();
        action.Tool.Should().Be("research-agent");
        action.Args.Should().ContainKey("geo");
    }

    [Fact]
    public void ParseReply_WithNormalOutputText_PreservesText()
    {
        var client = new OpenAiResponsesChatClient(new HttpClient(new StubHandler()), DummyConfig);
        var jsonBody = """
            {
                "output": [
                    {
                        "type": "message",
                        "role": "assistant",
                        "content": [
                            {
                                "type": "output_text",
                                "text": "Hoàn thành nghiên cứu."
                            }
                        ]
                    }
                ]
            }
            """;

        var reply = client.ParseReply(jsonBody);

        reply.Text.Should().Be("Hoàn thành nghiên cứu.");
    }

    [Fact]
    public async Task CompleteAsync_WithSseFunctionCallStream_SynthesizesReActJson()
    {
        var ssePayload = """
            data: {"type":"response.created"}
            data: {"type":"response.output_item.added","output_index":0,"item":{"id":"fc_1","type":"function_call","name":"content-agent"}}
            data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","delta":"{\"platform\":\"facebook\""}
            data: {"type":"response.function_call_arguments.delta","item_id":"fc_1","delta":",\"brief\":\"test\"}"}
            data: {"type":"response.output_item.done","output_index":0,"item":{"id":"fc_1","type":"function_call","name":"content-agent","arguments":"{\"platform\":\"facebook\",\"brief\":\"test\"}"}}
            data: {"type":"response.completed","response":{"usage":{"input_tokens":50,"output_tokens":15}}}
            data: [DONE]

            """;

        var handler = new StubHandler(HttpStatusCode.OK, ssePayload, "text/event-stream");
        var client = new OpenAiResponsesChatClient(new HttpClient(handler), DummyConfig);

        var reply = await client.CompleteAsync("system", [], "task");

        reply.Text.Should().Be("{\"tool\":\"content-agent\",\"args\":{\"platform\":\"facebook\",\"brief\":\"test\"}}");
        ReActAction.TryParse(reply.Text, out var action).Should().BeTrue();
        action.Tool.Should().Be("content-agent");
        action.Args["platform"].Should().Be("facebook");
    }

    private sealed class StubHandler(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string content = "{}",
        string mediaType = "application/json") : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, mediaType),
            };
            return Task.FromResult(response);
        }
    }
}
