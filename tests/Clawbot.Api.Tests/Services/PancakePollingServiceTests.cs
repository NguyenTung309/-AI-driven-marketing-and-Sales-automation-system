using Clawbot.Api.Services;
using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Multitenancy;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using static Clawbot.Api.Services.PancakePollingService;

namespace Clawbot.Api.Tests.Services;

public sealed class PancakePollingServiceTests
{
    private static AppDbContext CreateDbContext(string dbName)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new AppDbContext(opts, new NullTenantAccessor());
    }

    private sealed class NullTenantAccessor : ITenantAccessor
    {
        public TenantContext? Current => null;
        public TenantContext Require() => throw new NotSupportedException("Test");
    }
    [Fact]
    public async Task ProcessedMessage_Dedup_ShouldPreventDoubleProcessing()
    {
        await using var db = CreateDbContext("dedup_test_" + Guid.NewGuid());
        var msgId = "test-msg-123";
        db.ProcessedMessages.Add(new ProcessedMessage(Guid.NewGuid(), "zalo", msgId, "conv-1"));
        await db.SaveChangesAsync();

        var exists = await db.ProcessedMessages
            .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == msgId);
        Assert.True(exists);

        var existsAgain = await db.ProcessedMessages
            .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == msgId);
        Assert.True(existsAgain);
    }

    [Fact]
    public async Task ProcessedMessage_Dedup_ShouldBeFalseForUnknownMessage()
    {
        await using var db = CreateDbContext("dedup_unknown_" + Guid.NewGuid());
        var exists = await db.ProcessedMessages
            .AnyAsync(p => p.Platform == "zalo" && p.ExternalMessageId == "nonexistent");
        Assert.False(exists);
    }

    [Fact]
    public async Task DemoTenantResolver_ShouldReturnValidGuid()
    {
        await using var db = CreateDbContext("tenant_resolver_" + Guid.NewGuid());
        var resolver = new DemoTenantResolver(db);
        var tenantId = await resolver.ResolveTenantIdAsync();
        Assert.NotEqual(Guid.Empty, tenantId);
    }

    [Fact]
    public void ProcessedMessage_ShouldSetProperties()
    {
        var tenantId = Guid.NewGuid();
        var msg = new ProcessedMessage(tenantId, "zalo", "ext-123", "conv-456");
        Assert.Equal(tenantId, msg.TenantId);
        Assert.Equal("zalo", msg.Platform);
        Assert.Equal("ext-123", msg.ExternalMessageId);
        Assert.Equal("conv-456", msg.ConversationExternalId);
        Assert.NotEqual(Guid.Empty, msg.Id);
        Assert.True(msg.ProcessedAt > DateTime.MinValue);
    }

    [Fact]
    public void Processed_marker_is_staged_only_after_outbox_publish_succeeds()
    {
        var source = File.ReadAllText(FindRepoFile("src", "api", "Clawbot.Api", "Services", "PancakePollingService.cs"));

        var publishIndex = source.IndexOf("await publisher.Publish", StringComparison.Ordinal);
        var processedIndex = source.IndexOf("db.ProcessedMessages.Add", publishIndex, StringComparison.Ordinal);
        Assert.True(publishIndex >= 0 && processedIndex > publishIndex);
        Assert.Contains("ok &= await PollPageAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MapSender_TreatsEmptyAdminIdAsInboundCustomer()
    {
        var message = new PancakeMessage(
            "msg-1",
            "xin chao",
            new PancakeMessageSender("pzl_customer_1", "Khach", null, false, "", false),
            null,
            DateTime.UtcNow);
        var conversation = Conversation(lastSentBy: new PancakeLastSentBy("pzl_page_1", "Sale", null, null, null));

        var result = PancakePollingService.MapSender(message, conversation, "pzl_page_1");

        Assert.Equal("pzl_customer_1", result.ExternalUserId);
        Assert.Equal("pzl_customer_1", result.Metadata["sender_id"]);
        Assert.False(result.Metadata.ContainsKey("is_owner"));
    }

    [Fact]
    public void MapSender_MarksNonEmptyAdminIdAsOwner()
    {
        var message = new PancakeMessage(
            "msg-admin",
            "outbound",
            new PancakeMessageSender("admin-user", "Sale", null, false, "admin-1", false),
            null,
            DateTime.UtcNow);
        var conversation = Conversation(lastSentBy: null);

        var result = PancakePollingService.MapSender(message, conversation, "pzl_page_1");

        Assert.Equal("admin-user", result.ExternalUserId);
        Assert.Equal("true", result.Metadata["is_owner"]);
    }

    [Fact]
    public void MapSender_DoesNotReuseConversationLastSentByWhenMessageSenderIsMissing()
    {
        var message = new PancakeMessage("msg-2", "xin chao", null, null, DateTime.UtcNow);
        var conversation = Conversation(lastSentBy: new PancakeLastSentBy("pzl_page_1", "Sale", null, null, null));

        var result = PancakePollingService.MapSender(message, conversation, "pzl_page_1");

        Assert.Equal("pzl_customer_1", result.ExternalUserId);
        Assert.Equal("pzl_customer_1", result.Metadata["sender_id"]);
        Assert.False(result.Metadata.ContainsKey("is_owner"));
    }

    [Fact]
    public void MapSender_MarksPageSenderAsOwner()
    {
        var message = new PancakeMessage(
            "msg-3",
            "outbound",
            new PancakeMessageSender("pzl_page_1", null, null, false, null, false),
            null,
            DateTime.UtcNow);
        var conversation = Conversation(lastSentBy: null);

        var result = PancakePollingService.MapSender(message, conversation, "pzl_page_1");

        Assert.Equal("pzl_page_1", result.ExternalUserId);
        Assert.Equal("true", result.Metadata["is_owner"]);
        Assert.False(result.Metadata.ContainsKey("sender_name"));
        Assert.False(result.Metadata.ContainsKey("sender_avatar_url"));
    }

    [Fact]
    public void MapSender_MarksAutomatedSenderAsOwner()
    {
        var message = new PancakeMessage(
            "msg-4",
            "automated",
            new PancakeMessageSender("automation-bot", "AI", null, false, "", true),
            null,
            DateTime.UtcNow);
        var conversation = Conversation(lastSentBy: null);

        var result = PancakePollingService.MapSender(message, conversation, "pzl_page_1");

        Assert.Equal("automation-bot", result.ExternalUserId);
        Assert.Equal("true", result.Metadata["is_owner"]);
    }

    [Fact]
    public void MapSender_FallsBackToConversationCustomerForInboundIdentity()
    {
        var message = new PancakeMessage("msg-5", "inbound", null, null, DateTime.UtcNow);
        var conversation = Conversation(
            lastSentBy: new PancakeLastSentBy("pzl_page_1", "Sale", null, null, null),
            from: new PancakeFrom("pzl_thread_proxy", "Thread", null, false),
            customers: [new PancakeCustomer("pzl_customer_2", "Khach 2", null, null)]);

        var result = PancakePollingService.MapSender(message, conversation, "pzl_page_1");

        Assert.Equal("pzl_customer_2", result.ExternalUserId);
        Assert.Equal("pzl_customer_2", result.Metadata["sender_id"]);
        Assert.Equal("pzl_customer_2", result.Metadata["customer_id"]);
        Assert.False(result.Metadata.ContainsKey("is_owner"));
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }

    private static PancakeConversation Conversation(
        PancakeLastSentBy? lastSentBy,
        PancakeFrom? from = null,
        IReadOnlyList<PancakeCustomer>? customers = null) =>
        new(
            Id: "conv-1",
            Type: "INBOX",
            Snippet: "xin chao",
            MessageCount: 1,
            UpdatedAt: DateTime.UtcNow,
            InsertedAt: DateTime.UtcNow,
            PageId: "pzl_page_1",
            From: from ?? new PancakeFrom("pzl_customer_1", "Khach", null, false),
            LastSentBy: lastSentBy,
            Customers: customers ?? [new PancakeCustomer("pzl_customer_1", "Khach", null, null)]);
}
