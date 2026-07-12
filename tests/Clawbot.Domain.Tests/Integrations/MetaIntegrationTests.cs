using Clawbot.Domain.Integrations;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Integrations;

public sealed class MetaIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 11, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OAuth_state_can_be_consumed_only_once_before_expiry()
    {
        var state = MetaOAuthState.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", Now.AddMinutes(10), Now);

        state.TryConsume(Now.AddMinutes(1)).Should().BeTrue();
        state.TryConsume(Now.AddMinutes(2)).Should().BeFalse();
        state.ConsumedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Expired_OAuth_state_cannot_be_consumed()
    {
        var state = MetaOAuthState.Create(Guid.NewGuid(), Guid.NewGuid(), "hash", Now, Now.AddMinutes(-10));

        state.TryConsume(Now).Should().BeFalse();
        state.ConsumedAt.Should().BeNull();
    }
}
