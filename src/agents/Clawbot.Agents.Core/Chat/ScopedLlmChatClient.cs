using System.Runtime.CompilerServices;

namespace Clawbot.Agents.Core.Chat;

// The IClaudeChatClient that all consumers inject. On each call it reads the ambient (tenant, agent)
// context, resolves + decrypts that agent's bound config, builds the provider client, and delegates.
// No active config bound → LlmConfigNotConfiguredException (D1, no fallback).
public sealed class ScopedLlmChatClient(
    ILlmCallScope scope,
    ILlmConfigResolver resolver,
    ILlmChatClientFactory factory) : IClaudeChatClient
{
    private readonly ILlmCallScope _scope = scope;
    private readonly ILlmConfigResolver _resolver = resolver;
    private readonly ILlmChatClientFactory _factory = factory;

    public async Task<ClaudeReply> CompleteAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        CancellationToken ct = default)
    {
        var client = await ResolveClientAsync(ct).ConfigureAwait(false);
        return await client.CompleteAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ClaudeStreamChunk> StreamAsync(
        string systemPrompt,
        IReadOnlyList<ChatTurn> history,
        string userMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = await ResolveClientAsync(ct).ConfigureAwait(false);
        await foreach (var chunk in client.StreamAsync(systemPrompt, history, userMessage, ct).ConfigureAwait(false))
            yield return chunk;
    }

    private async Task<IClaudeChatClient> ResolveClientAsync(CancellationToken ct)
    {
        var ctx = _scope.Current
            ?? throw new InvalidOperationException(
                "No LLM call scope set. Call ILlmCallScope.Begin(tenantId, agentCode) at the agent entry point before invoking the chat client.");
        var resolved = await _resolver.ResolveAsync(ctx.TenantId, ctx.AgentCode, ct).ConfigureAwait(false);
        return _factory.Create(resolved);
    }
}
