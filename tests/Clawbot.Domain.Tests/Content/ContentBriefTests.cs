using Clawbot.Domain.Content;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Content;

public sealed class ContentBriefTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var userId = Guid.NewGuid();
        var brief = ContentBrief.Create(TenantId, "facebook", "Viết bài về IELTS", userId, Now);

        brief.TenantId.Should().Be(TenantId);
        brief.Platform.Should().Be("facebook");
        brief.Brief.Should().Be("Viết bài về IELTS");
        brief.Status.Should().Be("pending");
        brief.CreatedBy.Should().Be(userId);
        brief.CreatedAt.Should().Be(Now);
        brief.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public void Update_ChangesPlatformAndBrief()
    {
        var brief = ContentBrief.Create(TenantId, "fb", "old", null, Now);

        brief.Update("zalo", "new brief", Now.AddHours(1));

        brief.Platform.Should().Be("zalo");
        brief.Brief.Should().Be("new brief");
        brief.UpdatedAt.Should().Be(Now.AddHours(1));
    }

    [Fact]
    public void MarkStatus_ChangesStatusAndTimestamp()
    {
        var brief = ContentBrief.Create(TenantId, "fb", "b", null, Now);

        brief.MarkStatus("completed", Now.AddMinutes(30));

        brief.Status.Should().Be("completed");
        brief.UpdatedAt.Should().Be(Now.AddMinutes(30));
    }
}
