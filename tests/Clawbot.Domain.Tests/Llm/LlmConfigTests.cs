using Clawbot.Domain.Llm;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Llm;

public sealed class LlmConfigTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RequireKeyRotation_clears_secret_and_deactivates_config()
    {
        var config = LlmConfig.Create(Guid.NewGuid(), "anthropic", "claude-test", "cipher", Now);

        config.RequireKeyRotation(Now.AddMinutes(1));

        config.ApiKeyEncrypted.Should().BeEmpty();
        config.IsActive.Should().BeFalse();
        config.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void Activate_throws_when_key_rotation_is_required()
    {
        var config = LlmConfig.Create(Guid.NewGuid(), "anthropic", "claude-test", "cipher", Now);
        config.RequireKeyRotation(Now.AddMinutes(1));

        var act = () => config.Activate(Now.AddMinutes(2));

        act.Should().Throw<InvalidOperationException>().WithMessage("*key rotation*");
    }
}
