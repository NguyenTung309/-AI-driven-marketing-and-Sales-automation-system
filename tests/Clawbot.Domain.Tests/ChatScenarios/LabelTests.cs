using Clawbot.Domain.ChatScenarios;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.ChatScenarios;

public sealed class LabelTests
{
    [Fact]
    public void Create_sets_properties()
    {
        var tenantId = Guid.NewGuid();
        var label = Label.Create(tenantId, "Quan trong", "#ef4444");

        label.TenantId.Should().Be(tenantId);
        label.Name.Should().Be("Quan trong");
        label.Color.Should().Be("#ef4444");
        label.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_uses_default_color_when_not_specified_in_factory()
    {
        var tenantId = Guid.NewGuid();
        var label = Label.Create(tenantId, "Mac dinh", "#6366f1");
        label.Color.Should().Be("#6366f1");
    }
}
