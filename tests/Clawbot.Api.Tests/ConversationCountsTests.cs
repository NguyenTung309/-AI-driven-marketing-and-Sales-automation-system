using Clawbot.Api.Endpoints;
using Clawbot.Domain.Conversations;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class ConversationCountsTests
{
    [Fact]
    public async Task QueryConversationCountsAsync_CountsAllMatchingRowsBeyondFirstPage()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var conversations = Enumerable.Range(1, 75)
            .Select(index => Conversation.Open(
                tenantId,
                "facebook",
                $"thread-{index:D3}",
                new DateTimeOffset(2026, 8, 14, 8, index % 60, 0, TimeSpan.Zero),
                inboxId: inboxId))
            .ToArray();
        foreach (var conversation in conversations.Take(7))
            conversation.Assign(userId);
        conversations[70].Escalate();
        conversations[71].Resolve();
        db.Conversations.AddRange(conversations);
        await db.SaveChangesAsync();

        var query = InboxEndpoints.BuildVisibleConversations(
            db,
            [inboxId],
            inboxId: null,
            platform: null,
            q: null);

        // Act
        var result = await InboxEndpoints.QueryConversationCountsAsync(
            query,
            userId,
            CancellationToken.None);

        // Assert
        result.Total.Should().Be(75);
        result.Open.Should().Be(73);
        result.Escalated.Should().Be(1);
        result.Resolved.Should().Be(1);
        result.Mine.Should().Be(7);
    }

    [Fact]
    public async Task BuildVisibleConversations_IntersectsExplicitInboxWithResolvedScope()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var allowedInboxId = Guid.NewGuid();
        var blockedInboxId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.Conversations.AddRange(
            Conversation.Open(tenantId, "facebook", "allowed", DateTimeOffset.UtcNow, inboxId: allowedInboxId),
            Conversation.Open(tenantId, "facebook", "blocked", DateTimeOffset.UtcNow, inboxId: blockedInboxId));
        await db.SaveChangesAsync();

        // Act
        var outOfScope = await InboxEndpoints.BuildVisibleConversations(
                db,
                [allowedInboxId],
                blockedInboxId,
                platform: null,
                q: null)
            .CountAsync();
        var noInboxAccess = await InboxEndpoints.BuildVisibleConversations(
                db,
                [Guid.Empty],
                inboxId: null,
                platform: null,
                q: null)
            .CountAsync();
        var unrestricted = await InboxEndpoints.BuildVisibleConversations(
                db,
                [],
                blockedInboxId,
                platform: null,
                q: null)
            .CountAsync();

        // Assert
        outOfScope.Should().Be(0);
        noInboxAccess.Should().Be(0);
        unrestricted.Should().Be(1);
    }

    [Fact]
    public async Task BuildVisibleConversations_AppliesPlatformAndSearchFilters()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var inboxId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.Conversations.AddRange(
            Conversation.Open(tenantId, "zalo", "thread-HSK-4", DateTimeOffset.UtcNow, inboxId: inboxId),
            Conversation.Open(tenantId, "facebook", "thread-HSK-4", DateTimeOffset.UtcNow, inboxId: inboxId),
            Conversation.Open(tenantId, "zalo", "thread-HSK-3", DateTimeOffset.UtcNow, inboxId: inboxId));
        await db.SaveChangesAsync();

        // Act
        var items = await InboxEndpoints.BuildVisibleConversations(
                db,
                [inboxId],
                inboxId: null,
                platform: "zalo",
                q: "HSK-4")
            .Select(conversation => conversation.ExternalThreadId)
            .ToListAsync();

        // Assert
        items.Should().Equal("thread-HSK-4");
    }

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenantAccessor(tenantId));
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
