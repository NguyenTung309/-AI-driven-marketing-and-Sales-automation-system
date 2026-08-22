using Clawbot.Domain.Channels;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Channels;

public sealed class ProcessedMessageTests
{
    [Fact]
    public void Constructor_SetsAllFields()
    {
        var tenantId = Guid.NewGuid();

        var msg = new ProcessedMessage(tenantId, "facebook", "ext-msg-1", "conv-ext-1");

        msg.TenantId.Should().Be(tenantId);
        msg.Platform.Should().Be("facebook");
        msg.ExternalMessageId.Should().Be("ext-msg-1");
        msg.ConversationExternalId.Should().Be("conv-ext-1");
        msg.Id.Should().NotBeEmpty();
        msg.ProcessedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }
}
