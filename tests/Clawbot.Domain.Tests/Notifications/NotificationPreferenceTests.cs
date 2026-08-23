using Clawbot.Domain.Notifications;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Notifications;

public sealed class NotificationPreferenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var pref = NotificationPreference.Create(TenantId, UserId, "job_completed", true, false, true, Now);

        pref.TenantId.Should().Be(TenantId);
        pref.UserId.Should().Be(UserId);
        pref.Type.Should().Be("job_completed");
        pref.InApp.Should().BeTrue();
        pref.Push.Should().BeFalse();
        pref.Email.Should().BeTrue();
        pref.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Update_ChangesAllChannels()
    {
        var pref = NotificationPreference.Create(TenantId, UserId, "alert", true, true, false, Now);

        pref.Update(false, false, true, Now.AddMinutes(5));

        pref.InApp.Should().BeFalse();
        pref.Push.Should().BeFalse();
        pref.Email.Should().BeTrue();
        pref.UpdatedAt.Should().Be(Now.AddMinutes(5));
    }
}

public sealed class PushSubscriptionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var sub = PushSubscription.Create(TenantId, UserId, "https://push.example.com/abc", "p256dh-key", "auth-key", Now);

        sub.TenantId.Should().Be(TenantId);
        sub.UserId.Should().Be(UserId);
        sub.Endpoint.Should().Be("https://push.example.com/abc");
        sub.P256dh.Should().Be("p256dh-key");
        sub.Auth.Should().Be("auth-key");
        sub.CreatedAt.Should().Be(Now);
    }
}
