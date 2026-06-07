using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace Clawbot.Agents.Core.Content;

public sealed class ContentLlmOptions
{
    public const string SectionName = "Content:Llm";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public int MaxOutputTokens { get; set; } = 800;
}

public sealed record ContentLlmRequest(Guid TenantId, string Platform, string Prompt);

public sealed record ContentLlmResult(string Text, int InputTokens, int OutputTokens);

public interface IContentLlmClient
{
    Task<ContentLlmResult> CompleteAsync(ContentLlmRequest request, CancellationToken ct = default);
}

internal sealed class OpenAiCompatibleChatClient(IOptions<ContentLlmOptions> options) : IContentLlmClient
{
    private readonly ContentLlmOptions _options = options.Value;
    private readonly ChatClient _client = CreateClient(options.Value);

    private static ChatClient CreateClient(ContentLlmOptions opts)
    {
        var clientOptions = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(opts.BaseUrl))
            clientOptions.Endpoint = new Uri(opts.BaseUrl, UriKind.Absolute);
        return new ChatClient(opts.Model, new ApiKeyCredential(opts.ApiKey), clientOptions);
    }

    public async Task<ContentLlmResult> CompleteAsync(ContentLlmRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Prompt);
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Content:Llm:ApiKey not configured.");
        if (string.IsNullOrWhiteSpace(_options.Model))
            throw new InvalidOperationException("Content:Llm:Model not configured.");

        var completion = await _client.CompleteChatAsync(
            [ChatMessage.CreateUserMessage(request.Prompt)],
            new ChatCompletionOptions { MaxOutputTokenCount = _options.MaxOutputTokens },
            ct).ConfigureAwait(false);

        var value = completion.Value;
        var text = string.Concat(value.Content.Select(part => part.Text));
        return new ContentLlmResult(
            text,
            value.Usage?.InputTokenCount ?? 0,
            value.Usage?.OutputTokenCount ?? 0);
    }
}
