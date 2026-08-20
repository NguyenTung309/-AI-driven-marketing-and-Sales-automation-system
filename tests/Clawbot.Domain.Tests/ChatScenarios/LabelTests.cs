using Clawbot.Domain.ChatScenarios;
using FluentAssertions;

namespace Clawbot.Domain.Tests.ChatScenarios;

public sealed class LabelTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public void Create_SetsAllFields()
    {
        var label = Label.Create(TenantId, "VIP", "#ff0000");

        label.TenantId.Should().Be(TenantId);
        label.Name.Should().Be("VIP");
        label.Color.Should().Be("#ff0000");
        label.DeletedAt.Should().BeNull();
        label.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Update_ChangesNameAndColor()
    {
        var label = Label.Create(TenantId, "old", "#000");

        label.Update("new", "#fff");

        label.Name.Should().Be("new");
        label.Color.Should().Be("#fff");
    }

    [Fact]
    public void SoftDelete_SetsDeletedAt()
    {
        var label = Label.Create(TenantId, "n", "#c");

        label.SoftDelete();

        label.DeletedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }
}
