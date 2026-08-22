using Clawbot.SharedKernel.Orchestration;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Orchestration;

public sealed class ScheduleEventKeysTests
{
    [Fact]
    public void All_ContainsEveryDeclaredKey()
    {
        ScheduleEventKeys.All.Should().Equal(
            ScheduleEventKeys.TrendsScanned,
            ScheduleEventKeys.LeadBecameHot,
            ScheduleEventKeys.ContentPublishFailed);
    }

    [Fact]
    public void Keys_UseNamespacedDotFormat()
    {
        ScheduleEventKeys.TrendsScanned.Should().Be("content.trends.scanned");
        ScheduleEventKeys.LeadBecameHot.Should().Be("lead.became_hot");
        ScheduleEventKeys.ContentPublishFailed.Should().Be("content.publish.failed");
    }

    [Fact]
    public void All_HasNoDuplicates()
    {
        ScheduleEventKeys.All.Should().OnlyHaveUniqueItems();
    }
}
