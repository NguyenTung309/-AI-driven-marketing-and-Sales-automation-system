using Clawbot.SharedKernel.Notifications;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Notifications;

public sealed class NotificationRequestTests
{
    [Fact]
    public void Constructor_SetsRequiredFields()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new NotificationRequest(tenantId, userId, "job.failed", "Job thất bại");

        request.TenantId.Should().Be(tenantId);
        request.UserId.Should().Be(userId);
        request.Type.Should().Be("job.failed");
        request.Title.Should().Be("Job thất bại");
    }

    [Fact]
    public void Defaults_SeverityIsInfoAndOptionalsAreNull()
    {
        var request = new NotificationRequest(Guid.NewGuid(), null, "job.done", "Xong");

        request.Severity.Should().Be("info");
        request.Body.Should().BeNull();
        request.Link.Should().BeNull();
        request.GroupKey.Should().BeNull();
    }

    [Fact]
    public void NullUserId_MeansTenantBroadcast()
    {
        var request = new NotificationRequest(Guid.NewGuid(), null, "system.alert", "Cảnh báo");

        request.UserId.Should().BeNull();
    }

    [Fact]
    public void GroupKey_EnablesOccurrenceRollup()
    {
        var request = new NotificationRequest(
            Guid.NewGuid(),
            null,
            "content.publish.failed",
            "Đăng bài thất bại",
            Severity: "error",
            Body: "Token hết hạn",
            Link: "/content/123",
            GroupKey: "content.publish.failed:facebook");

        request.Severity.Should().Be("error");
        request.Body.Should().Be("Token hết hạn");
        request.Link.Should().Be("/content/123");
        request.GroupKey.Should().Be("content.publish.failed:facebook");
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var tenantId = Guid.NewGuid();
        var a = new NotificationRequest(tenantId, null, "t", "title");
        var b = new NotificationRequest(tenantId, null, "t", "title");

        a.Should().Be(b);
    }
}
