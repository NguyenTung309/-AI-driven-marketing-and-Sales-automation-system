using Clawbot.Application.Abstractions;
using Clawbot.Application.Modules.Conversations.Commands.IngestIncomingMessage;
using Clawbot.Application.Modules.Conversations.Commands.OpenConversation;
using Clawbot.Domain.Conversations;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Multitenancy;
using Clawbot.SharedKernel.Time;
using FluentAssertions;

namespace Clawbot.Application.Tests.Conversations;

public sealed class OpenConversationHandlerTests
{
    [Fact]
    public async Task Handle_AddsConversationAndSaves()
    {
        var fixture = new ConversationFixture();
        var handler = new OpenConversationHandler(fixture.Db, fixture.Tenants, fixture.Clock);

        var result = await handler.Handle(
            new OpenConversationCommand("facebook", "thread-1"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Conversations.Added.Should().ContainSingle();
        fixture.Conversations.Added[0].Platform.Should().Be("facebook");
        fixture.Conversations.Added[0].ExternalThreadId.Should().Be("thread-1");
        fixture.Conversations.Added[0].TenantId.Should().Be(fixture.TenantId);
        fixture.Db.SaveCalls.Should().Be(1);
        result.Value.Should().Be(fixture.Conversations.Added[0].Id);
    }
}

public sealed class IngestIncomingMessageHandlerTests
{
    private static readonly DateTimeOffset SentAt =
        new(2026, 8, 17, 10, 0, 0, TimeSpan.FromHours(7));

    private static ChannelMessage Message(string threadId = "thread-1") => new(
        "facebook",
        threadId,
        "psid-1",
        "Học phí bao nhiêu?",
        SentAt,
        new Dictionary<string, string>());

    [Fact]
    public async Task Handle_NoExistingConversation_OpensOneAndAppends()
    {
        var fixture = new ConversationFixture();
        var handler = new IngestIncomingMessageHandler(fixture.Db, fixture.Tenants, fixture.Clock);

        var result = await handler.Handle(
            new IngestIncomingMessageCommand(Message()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        fixture.Conversations.Added.Should().ContainSingle();
        fixture.Conversations.Added[0].Messages.Should().ContainSingle();
        fixture.Db.SaveCalls.Should().Be(1);
    }

    [Fact]
    public async Task Handle_ExistingConversation_AppendsWithoutCreating()
    {
        var fixture = new ConversationFixture();
        var existing = Conversation.Open(
            fixture.TenantId, "facebook", "thread-1", fixture.Clock.UtcNow);
        fixture.Conversations.Existing["facebook|thread-1"] = existing;
        var handler = new IngestIncomingMessageHandler(fixture.Db, fixture.Tenants, fixture.Clock);

        var result = await handler.Handle(
            new IngestIncomingMessageCommand(Message()),
            CancellationToken.None);

        result.Value.Should().Be(existing.Id);
        fixture.Conversations.Added.Should().BeEmpty();
        existing.Messages.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AppendsInboundContactMessage()
    {
        var fixture = new ConversationFixture();
        var handler = new IngestIncomingMessageHandler(fixture.Db, fixture.Tenants, fixture.Clock);

        await handler.Handle(new IngestIncomingMessageCommand(Message()), CancellationToken.None);

        var message = fixture.Conversations.Added[0].Messages.Single();
        message.Direction.Should().Be("in");
        message.SenderType.Should().Be("contact");
        message.Content.Should().Be("Học phí bao nhiêu?");
        message.ContentType.Should().Be("text");
    }
}

internal sealed class ConversationFixture
{
    public ConversationFixture()
    {
        TenantId = Guid.NewGuid();
        Tenants = new StubTenantAccessor(TenantId);
        Clock = new StubClock(new DateTimeOffset(2026, 8, 17, 3, 0, 0, TimeSpan.Zero));
        Conversations = new FakeConversationSet();
        Db = new FakeAppDbContext(Conversations);
    }

    public Guid TenantId { get; }
    public StubTenantAccessor Tenants { get; }
    public StubClock Clock { get; }
    public FakeConversationSet Conversations { get; }
    public FakeAppDbContext Db { get; }
}

internal sealed class FakeConversationSet : IConversationSet
{
    public List<Conversation> Added { get; } = [];

    public Dictionary<string, Conversation> Existing { get; } = new(StringComparer.Ordinal);

    public void Add(Conversation conversation) => Added.Add(conversation);

    public Task<Conversation?> FindByThreadAsync(
        string platform,
        string externalThreadId,
        CancellationToken ct = default) =>
        Task.FromResult(
            Existing.TryGetValue($"{platform}|{externalThreadId}", out var conversation)
                ? conversation
                : null);
}

internal sealed class FakeAppDbContext(IConversationSet conversations) : IAppDbContext
{
    public IConversationSet Conversations { get; } = conversations;

    public int SaveCalls { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCalls++;
        return Task.FromResult(1);
    }
}

internal sealed class StubTenantAccessor(Guid tenantId) : ITenantAccessor
{
    private readonly TenantContext _context = new(tenantId, "test-tenant");

    public TenantContext? Current => _context;

    public TenantContext Require() => _context;
}

internal sealed class StubClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow;
}
