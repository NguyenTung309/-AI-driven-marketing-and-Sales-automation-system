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

    [Fact]
    public void SupportsVision_is_nullable_tri_state_and_updatable()
    {
        var config = LlmConfig.Create(
            Guid.NewGuid(),
            "openai",
            "gpt-4o",
            "cipher",
            Now,
            supportsVision: true);
        config.SupportsVision.Should().BeTrue();

        config.UpdateConnection(
            "openai",
            "gpt-4o",
            baseUrl: null,
            displayName: "vision on",
            Now.AddMinutes(1),
            supportsVision: false);
        config.SupportsVision.Should().BeFalse();

        config.UpdateConnection(
            "openai",
            "gpt-4o",
            baseUrl: null,
            displayName: "vision auto",
            Now.AddMinutes(2),
            supportsVision: null);
        config.SupportsVision.Should().BeNull();
    }
}
