using System.Text.Json;
using Clawbot.Api.Contracts.SaleAssist;
using Clawbot.Api.Endpoints;
using Clawbot.Api.Jobs;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Multitenancy;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Clawbot.Api.Tests;

public sealed class SaleAssistSummaryEndpointTests
{
    [Fact]
    public async Task FindLatestSummaryAsync_ReturnsNewestSucceededSummaryForConversation()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var otherConversationId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var now = new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero);

        db.BackgroundJobs.AddRange(
            SucceededSummary(tenantId, conversationId, "Tóm tắt cũ", now.AddMinutes(-10)),
            SucceededSummary(tenantId, conversationId, "Tóm tắt mới", now),
            SucceededSummary(tenantId, otherConversationId, "Sai hội thoại", now.AddMinutes(1)),
            SucceededSummary(otherTenantId, conversationId, "Sai tenant", now.AddMinutes(2)),
            FailedSummary(tenantId, conversationId, now.AddMinutes(3)));
        await db.SaveChangesAsync();

        // Act
        var result = await SaleAssistEndpoints.FindLatestSummaryAsync(
            db,
            tenantId,
            conversationId,
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Summary.Should().Be("Tóm tắt mới");
    }

    [Fact]
    public async Task FindLatestSummaryAsync_ReturnsNull_WhenConversationHasNoSucceededSummary()
    {
        // Arrange
        var tenantId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        db.BackgroundJobs.Add(FailedSummary(
            tenantId,
            conversationId,
            new DateTimeOffset(2026, 8, 14, 8, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        // Act
        var result = await SaleAssistEndpoints.FindLatestSummaryAsync(
            db,
            tenantId,
            conversationId,
            CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    private static AppDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options, new FixedTenantAccessor(tenantId));
    }

    private static BackgroundJob SucceededSummary(
        Guid tenantId,
        Guid conversationId,
        string summary,
        DateTimeOffset at)
    {
        var job = BackgroundJob.Queue(
            tenantId,
            userId: null,
            SaleAssistSummaryJobHandler.JobType,
            "Tóm tắt hội thoại",
            payloadJson: null,
            at);
        var result = JsonSerializer.Serialize(
            new SaleAssistSummaryResponse(summary, 0),
            JobResultJson.Web);
        job.MarkSucceeded($"/inbox?conversation={conversationId}", result, at);
        return job;
    }

    private static BackgroundJob FailedSummary(
        Guid tenantId,
        Guid conversationId,
        DateTimeOffset at)
    {
        var job = BackgroundJob.Queue(
            tenantId,
            userId: null,
            SaleAssistSummaryJobHandler.JobType,
            "Tóm tắt hội thoại",
            payloadJson: null,
            at);
        job.MarkFailed($"Không tóm tắt được {conversationId}", at);
        return job;
    }

    private sealed class FixedTenantAccessor(Guid tenantId) : ITenantAccessor
    {
        public TenantContext? Current { get; } = new(tenantId, "test-tenant");

        public TenantContext Require() => Current!;
    }
}
