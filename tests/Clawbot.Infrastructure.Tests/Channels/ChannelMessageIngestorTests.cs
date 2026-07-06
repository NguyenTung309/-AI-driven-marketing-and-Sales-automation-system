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

    private static ChannelMessage Msg(string text, string thread = "page1:thread1", string? user = null,
        IReadOnlyDictionary<string, string>? meta = null)
    {
        if (user == null)
        {
            var idx = thread.IndexOf(':');
            user = idx > 0 ? thread[(idx + 1)..] : thread;
        }
        return new("facebook", thread, user, text, Now, meta ?? new Dictionary<string, string>());
    }

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

        await sut.IngestAsync(fx.TenantId, Msg("hi", thread: "page1:fb-12345", user: "fb-12345"));

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

        await sut.IngestAsync(fx.TenantId, Msg("hi", thread: "page1:ext-user-1", user: "ext-user-1", meta: meta));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.AvatarUrl.Should().Be("https://cdn.example.com/avatar.jpg");
    }

    [Fact]
    public async Task Owner_echo_does_not_overwrite_conversation_contact_or_create_sender_contact()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        // Customer message creates the conversation contact with the real name
        await sut.IngestAsync(fx.TenantId, Msg("hi", thread: "page1:pzl_u_c1", user: "pzl_u_c1",
            meta: new Dictionary<string, string>
            {
                ["conversation_name"] = "Khach Hang A",
                ["conversation_avatar_url"] = "https://cdn.example.com/khach.jpg",
                ["sender_id"] = "pzl_u_c1",
                ["page_id"] = "page1",
            }));

        // AI/admin echo: sender = page itself, carries the owner's name/avatar
        await sut.IngestAsync(fx.TenantId, Msg("auto reply", thread: "page1:pzl_u_c1", user: "page1",
            meta: new Dictionary<string, string>
            {
                ["sender_id"] = "page1",
                ["page_id"] = "page1",
                ["sender_name"] = "Le Minh Thang",
                ["sender_avatar_url"] = "https://cdn.example.com/owner.jpg",
                ["is_owner"] = "true",
            }));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.DisplayName.Should().Be("Khach Hang A");
        contact.AvatarUrl.Should().Be("https://cdn.example.com/khach.jpg");
        var outMsg = await fx.Db.Messages.IgnoreQueryFilters().Where(m => m.Direction == "out").SingleAsync();
        outMsg.SenderDisplayName.Should().Be("Le Minh Thang");
    }

    [Fact]
    public async Task Group_member_message_keeps_group_name_and_avatar_on_conversation_contact()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        await sut.IngestAsync(fx.TenantId, Msg("member msg", thread: "page1:pzl_g_grp1", user: "pzl_u_member1",
            meta: new Dictionary<string, string>
            {
                ["conversation_name"] = "Nhom Hoc Ba",
                ["conversation_avatar_url"] = "https://cdn.example.com/group.jpg",
                ["sender_id"] = "pzl_u_member1",
                ["sender_name"] = "Thanh Vien 1",
                ["sender_avatar_url"] = "https://cdn.example.com/member1.jpg",
                ["page_id"] = "page1",
                ["is_group"] = "true",
            }));

        var conv = await fx.Db.Conversations.IgnoreQueryFilters().SingleAsync();
        var groupContact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync(c => c.Id == conv.ContactId);
        groupContact.DisplayName.Should().Be("Nhom Hoc Ba");
        groupContact.AvatarUrl.Should().Be("https://cdn.example.com/group.jpg");

        // Member still gets their own sender contact with their own name/avatar
        var memberContact = await fx.Db.ContactExternalIds.IgnoreQueryFilters()
            .Where(x => x.ExternalId == "pzl_u_member1")
            .Join(fx.Db.Contacts.IgnoreQueryFilters(), x => x.ContactId, c => c.Id, (x, c) => c)
            .SingleAsync();
        memberContact.DisplayName.Should().Be("Thanh Vien 1");
        memberContact.AvatarUrl.Should().Be("https://cdn.example.com/member1.jpg");
    }

    [Fact]
    public async Task Owner_echo_of_locally_persisted_reply_is_deduplicated()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        // Customer message creates the conversation
        await sut.IngestAsync(fx.TenantId, Msg("hi", thread: "page1:pzl_u_e1", user: "pzl_u_e1",
            meta: new Dictionary<string, string> { ["external_message_id"] = "in1" }));

        // Reply persisted locally (sale manual send / AI auto-reply) - no external id
        var conv = await fx.Db.Conversations.IgnoreQueryFilters().SingleAsync();
        conv.AppendMessage("out", "agent", "chao ban", "text", Now.AddSeconds(30));
        await fx.Db.SaveChangesAsync();

        // Pancake echoes the same reply back with a fresh external id
        var echo = await sut.IngestAsync(fx.TenantId, Msg("chao ban", thread: "page1:pzl_u_e1", user: "page1",
            meta: new Dictionary<string, string>
            {
                ["external_message_id"] = "echo1",
                ["sender_id"] = "page1",
                ["page_id"] = "page1",
            }));

        echo.Deduplicated.Should().BeTrue();
        (await fx.Db.Messages.IgnoreQueryFilters().CountAsync(m => m.Direction == "out")).Should().Be(1);
    }

    [Fact]
    public async Task Conversation_name_heals_contact_stuck_with_wrong_name()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        // Contact created with the owner's name by the old buggy path (non-placeholder, so
        // the placeholder-only rename rule can never fix it)
        await sut.IngestAsync(fx.TenantId, Msg("old buggy msg", thread: "page1:pzl_g_grp2", user: "pzl_g_grp2",
            meta: new Dictionary<string, string> { ["display_name"] = "Le Minh Thang" }));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.DisplayName.Should().Be("Le Minh Thang");

        // Next poll carries the authoritative conversation_name -> self-heal
        await sut.IngestAsync(fx.TenantId, Msg("new msg", thread: "page1:pzl_g_grp2", user: "pzl_g_grp2",
            meta: new Dictionary<string, string>
            {
                ["conversation_name"] = "Nhom Hoc Ba",
                ["sender_id"] = "pzl_u_member2",
                ["page_id"] = "page1",
            }));

        contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync(c => c.Id == contact.Id);
        contact.DisplayName.Should().Be("Nhom Hoc Ba");
    }

   [Fact]
    public async Task Contact_display_name_updated_from_pzl_to_real_name()
    {
        using var fx = new TestAppDb();
        var (sut, _) = Build(fx);

        // Simulate a contact initially created with pzl_ prefix (from old system)
        await sut.IngestAsync(fx.TenantId, Msg("first msg", thread: "page1:pzl_u_abc123", user: "pzl_u_abc123", meta: new Dictionary<string, string>()));

        // Now ingest with real display_name
        var meta = new Dictionary<string, string>
        {
            ["display_name"] = "Nguyen Van B",
        };
        await sut.IngestAsync(fx.TenantId, Msg("second msg", thread: "page1:pzl_u_abc123", user: "pzl_u_abc123", meta: meta));

        var contact = await fx.Db.Contacts.IgnoreQueryFilters().SingleAsync();
        contact.DisplayName.Should().Be("Nguyen Van B");
    }

}
