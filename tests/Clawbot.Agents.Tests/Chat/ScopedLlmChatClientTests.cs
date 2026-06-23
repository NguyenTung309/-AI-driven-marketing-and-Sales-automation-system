using Clawbot.Agents.Core.Chat;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Agents.Tests.Chat;

public sealed class ScopedLlmChatClientTests
{
    [Fact]
    public async Task CompleteAsync_resolves_current_ambient_context_and_delegates_to_provider_client()
    {
        var tenantId = Guid.NewGuid();
        var resolved = new ResolvedLlmConfig("anthropic", "claude-sonnet", "plain-key", null, 3m, 15m);
        var provider = Substitute.For<IClaudeChatClient>();
        provider.CompleteAsync("sys", Arg.Any<IReadOnlyList<ChatTurn>>(), "hello", Arg.Any<CancellationToken>())
            .Returns(new ClaudeReply("hi", 1, 2, 0.000033m, "claude-sonnet"));
        var resolver = Substitute.For<ILlmConfigResolver>();
        resolver.ResolveAsync(tenantId, "chat-agent", Arg.Any<CancellationToken>()).Returns(resolved);
        var factory = Substitute.For<ILlmChatClientFactory>();
        factory.Create(resolved).Returns(provider);
        var scope = new LlmCallScope();
        var sut = new ScopedLlmChatClient(scope, resolver, factory);

        using (scope.Begin(tenantId, "chat-agent"))
        {
            var reply = await sut.CompleteAsync("sys", Array.Empty<ChatTurn>(), "hello");
            reply.Text.Should().Be("hi");
        }

        await resolver.Received(1).ResolveAsync(tenantId, "chat-agent", Arg.Any<CancellationToken>());
        factory.Received(1).Create(resolved);
    }

    [Fact]
    public async Task CompleteAsync_throws_when_no_call_scope_is_set()
    {
        var sut = new ScopedLlmChatClient(
            new LlmCallScope(),
            Substitute.For<ILlmConfigResolver>(),
            Substitute.For<ILlmChatClientFactory>());

        var act = async () => await sut.CompleteAsync("sys", Array.Empty<ChatTurn>(), "hello");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*No LLM call scope set*");
    }

    [Fact]
    public void Begin_restores_previous_context_when_disposed()
    {
        var scope = new LlmCallScope();
        var outerTenant = Guid.NewGuid();
        var innerTenant = Guid.NewGuid();

        using (scope.Begin(outerTenant, "chat-agent"))
        {
            scope.Current.Should().Be(new LlmCallContext(outerTenant, "chat-agent"));
            using (scope.Begin(innerTenant, "sale-assist"))
            {
                scope.Current.Should().Be(new LlmCallContext(innerTenant, "sale-assist"));
            }
            scope.Current.Should().Be(new LlmCallContext(outerTenant, "chat-agent"));
        }

        scope.Current.Should().BeNull();
    }

    [Fact]
    public void Begin_inherits_outer_cost_timestamp_when_not_overridden()
    {
        var scope = new LlmCallScope();
        var tenant = Guid.NewGuid();
        var costAt = new DateTimeOffset(2026, 6, 30, 23, 59, 0, TimeSpan.Zero);

        using (scope.Begin(tenant, "orchestrator", costAt))
        {
            using (scope.Begin(tenant, "content-agent"))
            {
                scope.Current.Should().Be(new LlmCallContext(tenant, "content-agent", costAt));
            }
        }
    }
}
