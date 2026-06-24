using Clawbot.Agents.Core.Skills.Nlp;
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
        var piiRedactor = Substitute.For<IPiiRedactor>();
        piiRedactor.RedactAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => new RedactionResult(ci.ArgAt<string>(0), Array.Empty<PiiSpan>()));
        var sut = new ChannelMessageIngestor(fx.Db, notifier, clock, embeddingSync, piiRedactor, NullLogger<ChannelMessageIngestor>.Instance);
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


    [Fact]
    public async Task Direction_out_when_sender_id_matches_page_id()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);
        var meta = new Dictionary<string, string>
        {
            ["sender_id"] = "page1",
            ["page_id"] = "page1",
            ["sender_name"] = "Page Owner",
        };

        await sut.IngestAsync(fx.TenantId, Msg("hello from owner", thread: "page1:thread2", meta: meta));

        var msg = await fx.Db.Messages.IgnoreQueryFilters().SingleAsync();
        msg.Direction.Should().Be("out");
        msg.SenderType.Should().Be("user");
        msg.SenderDisplayName.Should().Be("Page Owner");
    }

    [Fact]
    public async Task Direction_in_when_sender_id_differs_from_page_id()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);
        var meta = new Dictionary<string, string>
        {
            ["sender_id"] = "customer1",
            ["page_id"] = "page1",
            ["sender_name"] = "Nguyen Van A",
        };

        await sut.IngestAsync(fx.TenantId, Msg("hello from customer", thread: "page1:thread3", meta: meta));

        var msg = await fx.Db.Messages.IgnoreQueryFilters().SingleAsync();
        msg.Direction.Should().Be("in");
        msg.SenderType.Should().Be("contact");
        msg.SenderDisplayName.Should().Be("Nguyen Van A");
    }

    [Fact]
    public async Task Contact_avatar_stored_from_metadata()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);
        var meta = new Dictionary<string, string>
        {
            ["display_name"] = "Test Contact",
            ["avatar_url"] = "https://cdn.example.com/avatar.jpg",
        };

        await sut.IngestAsync(fx.TenantId, Msg("hi", user: "ext-user-1", meta: meta));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.AvatarUrl.Should().Be("https://cdn.example.com/avatar.jpg");
    }

    [Fact]
    public async Task Inbox_name_updated_from_page_admin_name()
    {
        using var fx = new TestAppDb();
        var inbox = Clawbot.Domain.Channels.Inbox.Create(fx.TenantId, "Old Name",
            "facebook", "fb_page_1");
        fx.Db.Inboxes.Add(inbox);
        await fx.Db.SaveChangesAsync();
        var (sut, _) = Build(fx);
        var meta = new Dictionary<string, string>
        {
            ["sender_id"] = "fb_page_1",
            ["page_id"] = "fb_page_1",
            ["page_admin_name"] = "Le Minh Thang",
            ["sender_name"] = "Le Minh Thang",
        };
        await sut.IngestAsync(fx.TenantId, Msg("hello",
            thread: "fb_page_1:thread_x",
            user: "user1",
            meta: meta));
        var updatedInbox = await fx.Db.Inboxes.IgnoreQueryFilters()
            .FirstAsync(i => i.Id == inbox.Id);
        updatedInbox.Name.Should().Be("Le Minh Thang");
    }
    [Fact]
    public async Task Contact_display_name_updated_from_pzl_to_real_name()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        // Simulate a contact initially created with pzl_ prefix (from old system)
        await sut.IngestAsync(fx.TenantId, Msg("first msg", user: "pzl_u_abc123", meta: new Dictionary<string, string>()));

        // Now ingest with real display_name
        var meta = new Dictionary<string, string>
        {
            ["display_name"] = "Nguyen Van B",
        };
        await sut.IngestAsync(fx.TenantId, Msg("second msg", user: "pzl_u_abc123", meta: meta));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.DisplayName.Should().Be("Nguyen Van B");
    }

}
