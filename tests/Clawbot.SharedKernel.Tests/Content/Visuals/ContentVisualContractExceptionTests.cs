using Clawbot.SharedKernel.Content.Visuals;
using FluentAssertions;

namespace Clawbot.SharedKernel.Tests.Content.Visuals;

public sealed class ContentVisualContractExceptionTests
{
    [Fact]
    public void Constructor_SetsCodeAndPath()
    {
        var ex = new ContentVisualContractException("slot_duplicate", "$.slots.headline");

        ex.Code.Should().Be("slot_duplicate");
        ex.Path.Should().Be("$.slots.headline");
        ex.Message.Should().Contain("slot_duplicate");
        ex.Message.Should().Contain("$.slots.headline");
    }
}
