using Clawbot.Application.Abstractions;
using Clawbot.Application.Modules.Conversations.Commands.OpenConversation;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Application.Tests.Modules.Conversations;

public sealed class OpenConversationHandlerTests
{
    [Fact]
    public async Task HandleReturnsIdAndPersistsAggregate()
    {
        var tenantId = Guid.NewGuid();
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Require().Returns(new TenantContext(tenantId, "demo"));

        var conversations = Substitute.For<IConversationSet>();
        var db = Substitute.For<IAppDbContext>();
        db.Conversations.Returns(conversations);
        db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero));

        var sut = new OpenConversationHandler(db, tenants, clock);

        var result = await sut.Handle(new OpenConversationCommand("facebook", "thread-1"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        conversations.Received(1).Add(Arg.Is<Conversation>(c => c.TenantId == tenantId));
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
