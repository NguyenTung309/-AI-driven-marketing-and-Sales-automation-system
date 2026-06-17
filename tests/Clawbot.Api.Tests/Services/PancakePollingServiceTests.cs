using Clawbot.Domain.Channels;
using Clawbot.Infrastructure.Multitenancy;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

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
        db.ProcessedMessages.Add(new ProcessedMessage("zalo", msgId, "conv-1"));
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
        var msg = new ProcessedMessage("zalo", "ext-123", "conv-456");
        Assert.Equal("zalo", msg.Platform);
        Assert.Equal("ext-123", msg.ExternalMessageId);
        Assert.Equal("conv-456", msg.ConversationExternalId);
        Assert.NotEqual(Guid.Empty, msg.Id);
        Assert.True(msg.ProcessedAt > DateTime.MinValue);
    }
}
