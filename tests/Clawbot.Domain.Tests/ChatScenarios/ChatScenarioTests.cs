using Clawbot.Domain.ChatScenarios;
using FluentAssertions;

namespace Clawbot.Domain.Tests.ChatScenarios;

public sealed class ChatScenarioTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    private static ChatScenario CreateDefault() => ChatScenario.Create(
        TenantId, "KB-001", "Greeting", @"^xin chào", "Chào bạn!", "facebook,zalo", Now);

    // ── Create ────────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields()
    {
        var s = CreateDefault();

        s.TenantId.Should().Be(TenantId);
        s.Code.Should().Be("KB-001");
        s.GroupName.Should().Be("Greeting");
        s.TriggerText.Should().Be(@"^xin chào");
        s.ResponseTemplate.Should().Be("Chào bạn!");
        s.Platforms.Should().Be("facebook,zalo");
        s.ToneVoice.Should().BeNull();
        s.SuccessRate.Should().BeNull();
        s.CreatedAt.Should().Be(Now);
        s.UpdatedAt.Should().Be(Now);
    }

    // ── Update ────────────────────────────────────────────────────────

    [Fact]
    public void Update_ChangesAllMutableFields()
    {
        var s = CreateDefault();
        var updatedAt = Now.AddMinutes(5);

        s.Update("Farewell", @"^tạm biệt", "Bye!", "facebook", "friendly", updatedAt);

        s.GroupName.Should().Be("Farewell");
        s.TriggerText.Should().Be(@"^tạm biệt");
        s.ResponseTemplate.Should().Be("Bye!");
        s.Platforms.Should().Be("facebook");
        s.ToneVoice.Should().Be("friendly");
        s.UpdatedAt.Should().Be(updatedAt);
    }

    // ── RecordOutcome ─────────────────────────────────────────────────

    [Fact]
    public void RecordOutcome_FirstConvertedSampleSeedsRateToOne()
    {
        var s = CreateDefault();

        s.RecordOutcome(converted: true, Now.AddMinutes(1));

        s.SuccessRate.Should().Be(1m);
        s.UpdatedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public void RecordOutcome_FirstFailedSampleSeedsRateToZero()
    {
        var s = CreateDefault();

        s.RecordOutcome(converted: false, Now.AddMinutes(1));

        s.SuccessRate.Should().Be(0m);
    }

    [Fact]
    public void RecordOutcome_EmaConvergesTowardsRecentSamples()
    {
        var s = CreateDefault();
        s.RecordOutcome(true, Now);

        // After seed at 1.0, a failure pulls rate toward 0 with alpha=0.1
        s.RecordOutcome(false, Now.AddMinutes(1));

        // 1.0 + 0.1 * (0 - 1.0) = 0.9
        s.SuccessRate.Should().Be(0.9m);
    }

    [Fact]
    public void RecordOutcome_MultipleOutcomesSmoothGradually()
    {
        var s = CreateDefault();
        s.RecordOutcome(true, Now);       // rate = 1.0
        s.RecordOutcome(false, Now.AddMinutes(1)); // rate = 0.9
        s.RecordOutcome(false, Now.AddMinutes(2)); // rate = 0.9 + 0.1*(0-0.9) = 0.81

        s.SuccessRate.Should().Be(0.81m);
    }
}
