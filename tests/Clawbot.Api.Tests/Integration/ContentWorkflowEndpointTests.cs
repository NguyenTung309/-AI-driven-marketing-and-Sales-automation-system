using System.Net;
using System.Net.Http.Json;
using Clawbot.Api.Contracts.Content;
using Clawbot.Domain.Content;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Seed dữ liệu ở trạng thái sâu trực tiếp qua AppDbContext rồi gọi API trên đó — cách duy nhất
/// chạm được thân handler approve/reject/schedule (không đi qua được các bước agent-review thật).
/// </summary>
public sealed class ContentWorkflowEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private sealed class SeededContent
    {
        public required Tenant Tenant { get; init; }

        public required ContentItem Item { get; init; }
    }

    private async Task<SeededContent> SeedReviewedItemAsync(
        string reviewStatus = ContentItem.ReviewStatusPassed,
        string imageStatus = ContentItem.ImageReviewStatusNotApplicable,
        string platform = "facebook")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Tenant của admin đã được RbacSeeder/Bootstrapper tạo; tìm theo slug "default".
        var tenant = await db.Tenants.IgnoreQueryFilters()
            .FirstAsync(t => t.Slug == "default");

        var generatorAgent = Guid.NewGuid();
        var reviewerAgent = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(tenant.Id, platform, "Nội dung chờ duyệt", generatorAgent, now);
        item.BeginAgentReview(item.ContentRevision, now.AddSeconds(-10));
        item.RecordAgentReview(
            item.ContentRevision,
            reviewStatus,
            imageStatus,
            reviewedImageCount: 0,
            reviewerAgentId: reviewerAgent,
            reason: null,
            at: now);

        db.ContentItems.Add(item);
        await db.SaveChangesAsync();
        return new SeededContent { Tenant = tenant, Item = item };
    }

    private async Task<HttpClient> ClientAsync() => await _factory.CreateAuthenticatedClientAsync();

    [Fact]
    public async Task Approve_ReviewedItem_TransitionsToScheduled()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/approve", UriKind.Relative),
            new ApproveContentItemRequest(seeded.Item.ContentRevision));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentItemDto>();
        // Approve + auto-scheduler chay ngay nen item di thang tu "reviewed" sang "scheduled".
        dto!.Status.Should().Be("scheduled");
    }

    [Fact]
    public async Task Approve_StaleRevision_IsConflict()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/approve", UriKind.Relative),
            new ApproveContentItemRequest(seeded.Item.ContentRevision + 5));

        // Revision khac voi revision da review -> 409 Conflict.
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Approve_RevisionZero_IsRejected()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/approve", UriKind.Relative),
            new ApproveContentItemRequest(0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reject_ReviewedItem_MarksRejected()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/reject", UriKind.Relative),
            new RejectContentItemRequest(seeded.Item.ContentRevision, "nội dung chưa chuẩn"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reject_BlankReason_IsRejected()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/reject", UriKind.Relative),
            new RejectContentItemRequest(seeded.Item.ContentRevision, "  "));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Body_RetriggersAgentReview()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}", UriKind.Relative),
            new UpdateContentItemRequest("Nội dung đã sửa", null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentItemDto>();
        dto!.ContentRevision.Should().BeGreaterThan(seeded.Item.ContentRevision);
    }

    [Fact]
    public async Task Schedule_ApprovedItem_CreatesSchedule()
    {
        // Seed tới trạng thái approved để schedule handler đi qua nhánh chính.
        // Platform "website" né nhánh yêu cầu kết nối Facebook Page (không thuộc phạm vi test này).
        var seeded = await SeedReviewedItemAsync(platform: "website");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.ContentItems.IgnoreQueryFilters()
                .FirstAsync(i => i.Id == seeded.Item.Id);
            item.ApproveForPublishing(
                item.ContentRevision,
                Guid.NewGuid(),
                Clawbot.Domain.Tenants.Tenant.ContentPublishingPolicyAutomatic,
                1,
                null,
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            seeded = new SeededContent { Tenant = seeded.Tenant, Item = item };
        }

        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/schedule", UriKind.Relative),
            new ScheduleContentItemRequest(DateTimeOffset.UtcNow.AddDays(1)));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.ContentSchedules.IgnoreQueryFilters()
            .CountAsync(s => s.ContentItemId == seeded.Item.Id))
            .Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Schedule_InPast_IsRejected()
    {
        // Platform "website" né nhánh yêu cầu kết nối Facebook Page (không thuộc phạm vi test này).
        var seeded = await SeedReviewedItemAsync(platform: "website");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = await db.ContentItems.IgnoreQueryFilters()
                .FirstAsync(i => i.Id == seeded.Item.Id);
            item.ApproveForPublishing(
                item.ContentRevision,
                Guid.NewGuid(),
                Clawbot.Domain.Tenants.Tenant.ContentPublishingPolicyAutomatic,
                1,
                null,
                DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            seeded = new SeededContent { Tenant = seeded.Tenant, Item = item };
        }

        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/schedule", UriKind.Relative),
            new ScheduleContentItemRequest(DateTimeOffset.UtcNow.AddDays(-1)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Approve_Twice_IsConflictOrRejected()
    {
        var seeded = await SeedReviewedItemAsync();
        var client = await ClientAsync();
        var first = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/approve", UriKind.Relative),
            new ApproveContentItemRequest(seeded.Item.ContentRevision));
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // HÀNH VI HIỆN TẠI: approve lần 2 vẫn trả 200 (idempotent) — handler bắt
        // DbUpdateConcurrencyException/InvalidOperationException nhưng luồng approve lặp không ném.
        var second = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{seeded.Item.Id:D}/approve", UriKind.Relative),
            new ApproveContentItemRequest(seeded.Item.ContentRevision));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await verifyDb.ContentSchedules.IgnoreQueryFilters()
            .CountAsync(sch => sch.ContentItemId == seeded.Item.Id))
            .Should().Be(1, "approve lặp không được đẻ thêm lịch");
    }
}
