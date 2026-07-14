using Clawbot.Domain.Notifications;
using Clawbot.Infrastructure.Notifications;
using FluentAssertions;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Notifications;

// Chốt cứng của P4: cảnh báo lỗi luôn đẩy, user KHÔNG tắt được.
// Nhóm việc máy móc thì ngược lại: vào feed nhưng mặc định không rung chuông.
public sealed class NotificationDeliveryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);

    private static NotificationPreference Muted(string type) =>
        NotificationPreference.Create(Guid.NewGuid(), Guid.NewGuid(), type, inApp: false, push: false, email: false, Now);

    [Theory]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("critical")]
    public void Warning_always_pushed_even_when_user_muted_it(string severity)
    {
        NotificationDeliveryPolicy.ShouldPush(Muted("job_failed"), "job_failed", severity).Should().BeTrue();
        NotificationDeliveryPolicy.ShouldShowInApp(Muted("job_failed"), severity).Should().BeTrue();
    }

    [Fact]
    public void Mechanical_types_are_quiet_by_default_but_still_in_feed()
    {
        NotificationDeliveryPolicy.DefaultPush("ads_daypart").Should().BeFalse();
        NotificationDeliveryPolicy.DefaultPush("drip_sent").Should().BeFalse();
        NotificationDeliveryPolicy.ShouldPush(preference: null, "ads_daypart", "info").Should().BeFalse();
        NotificationDeliveryPolicy.ShouldShowInApp(preference: null, "info").Should().BeTrue();
    }

    [Fact]
    public void User_choice_wins_for_non_warning_types()
    {
        NotificationDeliveryPolicy.ShouldPush(Muted("job_succeeded"), "job_succeeded", "info").Should().BeFalse();
        NotificationDeliveryPolicy.ShouldShowInApp(Muted("job_succeeded"), "info").Should().BeFalse();

        var loud = NotificationPreference.Create(
            Guid.NewGuid(), Guid.NewGuid(), "ads_daypart", inApp: true, push: true, email: false, Now);
        NotificationDeliveryPolicy.ShouldPush(loud, "ads_daypart", "info").Should().BeTrue();
    }

    [Fact]
    public void Default_push_on_for_ordinary_types()
    {
        NotificationDeliveryPolicy.DefaultPush("job_succeeded").Should().BeTrue();
        NotificationDeliveryPolicy.ShouldPush(preference: null, "job_succeeded", "info").Should().BeTrue();
    }
}
