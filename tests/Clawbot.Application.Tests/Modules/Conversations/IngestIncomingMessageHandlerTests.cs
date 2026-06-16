using Clawbot.Application.Abstractions;
using Clawbot.Application.Modules.Conversations.Commands.IngestIncomingMessage;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Clawbot.Application.Tests.Modules.Conversations;

public sealed class IngestIncomingMessageHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 4, 9, 0, 0, TimeSpan.Zero);

    private static ChannelMessage Inbound(string thread = "thread-1") =>
        new("facebook", thread, "ext-user-9", "Xin chào, học phí thế nào ạ?", Now,
            new Dictionary<string, string>());

    private static (IngestIncomingMessageHandler sut, IConversationSet conversations, IAppDbContext db)
        BuildSut(Conversation? existing)
    {
        var tenants = Substitute.For<ITenantAccessor>();
        tenants.Require().Returns(new TenantContext(TenantId, "demo"));

        var conversations = Substitute.For<IConversationSet>();
        conversations
            .FindByThreadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(existing);

        var db = Substitute.For<IAppDbContext>();
        db.Conversations.Returns(conversations);
        db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        return (new IngestIncomingMessageHandler(db, tenants, clock), conversations, db);
    }

    [Fact]
    public async Task WhenNoConversationExists_CreatesOneAndAppendsMessage()
    {
        var (sut, conversations, db) = BuildSut(existing: null);

        var result = await sut.Handle(new IngestIncomingMessageCommand(Inbound()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        conversations.Received(1).Add(Arg.Is<Conversation>(c =>
            c.TenantId == TenantId && c.Platform == "facebook" && c.Messages.Count == 1));
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenConversationExists_AppendsMessageWithoutCreatingNew()
    {
        var existing = Conversation.Open(TenantId, "facebook", "thread-1", Now);
        var (sut, conversations, db) = BuildSut(existing);

        var result = await sut.Handle(new IngestIncomingMessageCommand(Inbound()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(existing.Id);
        conversations.DidNotReceive().Add(Arg.Any<Conversation>());
        existing.Messages.Should().ContainSingle(m => m.Direction == "in" && m.SenderType == "contact");
        await db.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
