using Clawbot.Infrastructure.Channels;
using Clawbot.Infrastructure.Vectors;
using Clawbot.SharedKernel.Channels;
using Clawbot.SharedKernel.Inbox;
using Clawbot.SharedKernel.Time;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Clawbot.Infrastructure.Tests.Channels;

// M08 — ChannelMessageIngestor find-or-create + dedup pipeline.
public sealed class ChannelMessageIngestorTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 2, 9, 0, 0, TimeSpan.Zero);

    private static (ChannelMessageIngestor Sut, IInboxNotifier Notifier) Build(TestAppDb fx)
    {
        var notifier = Substitute.For<IInboxNotifier>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var embeddingSync = Substitute.For<IContactEmbeddingSync>();
        var sut = new ChannelMessageIngestor(fx.Db, notifier, clock, embeddingSync, NullLogger<ChannelMessageIngestor>.Instance);
        return (sut, notifier);
    }

    private static ChannelMessage Msg(string text, string thread = "page1:thread1", string user = "user1",
        IReadOnlyDictionary<string, string>? meta = null) =>
        new("facebook", thread, user, text, Now, meta ?? new Dictionary<string, string>());

    [Fact]
    public async Task New_message_creates_contact_conversation_and_message()
    {
        using var fx = new TestAppDb();
        var (sut, notifier) = Build(fx);

        var result = await sut.IngestAsync(fx.TenantId,
            Msg("hi", meta: new Dictionary<string, string> { ["display_name"] = "John" }));

        result.Deduplicated.Should().BeFalse();
        result.MessageId.Should().NotBeNull();
        (await fx.Db.Contacts.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await fx.Db.Conversations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        await notifier.Received(1).NotifyMessageAsync(fx.TenantId, Arg.Any<InboxMessageEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_message_same_thread_reuses_conversation_and_contact()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        await sut.IngestAsync(fx.TenantId, Msg("first"));
        await sut.IngestAsync(fx.TenantId, Msg("second"));

        (await fx.Db.Contacts.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await fx.Db.Conversations.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Duplicate_external_message_id_is_deduplicated()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);
        var meta = new Dictionary<string, string> { ["external_message_id"] = "m1" };

        await sut.IngestAsync(fx.TenantId, Msg("same", meta: meta));
        var second = await sut.IngestAsync(fx.TenantId, Msg("same", meta: meta));

        second.Deduplicated.Should().BeTrue();
        second.MessageId.Should().BeNull();
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Missing_display_name_uses_external_user_id()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        await sut.IngestAsync(fx.TenantId, Msg("hi", user: "fb-12345"));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.DisplayName.Should().Be("fb-12345");
    }

    [Fact]
    public async Task Empty_tenant_throws()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        var act = async () => await sut.IngestAsync(Guid.Empty, Msg("x"));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
