using Clawbot.Domain.Conversations;
using FluentAssertions;
using Xunit;

namespace Clawbot.Domain.Tests.Conversations;

public sealed class ConversationLabelTests
{
    [Fact]
    public void Create_sets_Ids()
    {
        var convId = Guid.NewGuid();
        var labelId = Guid.NewGuid();

        var cl = ConversationLabel.Create(convId, labelId);

        cl.ConversationId.Should().Be(convId);
        cl.LabelId.Should().Be(labelId);
    }
}
