using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Api.Contracts.Content;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using Clawbot.SharedKernel.Content;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Batch 3 bổ sung /api/content: GET /trends, POST /trends/scan, POST /schedules/{id}/publish/retry
/// và POST /schedules/{id}/publish/reconcile (Phase 4.6, permission content:publish). Trùng lặp có chủ đích
/// với ContentReadEndpointTests (chỉ kiểm 200 chung) — ở đây soi sâu từng nhánh lỗi/thành công thật của 4 handler.
/// </summary>
public sealed class ContentEndpointsBatch3Tests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<Guid> SeedItemAsync(Guid tenantId, string platform = "website")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = ContentItem.Create(tenantId, platform, "Nội dung batch3", Guid.NewGuid(), DateTimeOffset.UtcNow);
        db.ContentItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    // Đưa item qua đủ review + approve + schedule để CanPublishCurrentRevision() = true —
    // cần thiết cho nhánh reconcile "succeeded" gọi tới item.MarkPublished(now).
    private async Task<Guid> SeedPublishableItemAsync(Guid tenantId, string platform = "website")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var generatorAgent = Guid.NewGuid();
        var reviewerAgent = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var item = ContentItem.Create(tenantId, platform, "Nội dung sẵn sàng đăng", generatorAgent, now.AddMinutes(-30));
        item.BeginAgentReview(item.ContentRevision, now.AddMinutes(-20));
        item.RecordAgentReview(
            item.ContentRevision,
            ContentItem.ReviewStatusPassed,
            ContentItem.ImageReviewStatusNotApplicable,
            reviewedImageCount: 0,
            reviewerAgentId: reviewerAgent,
            reason: null,
            at: now.AddMinutes(-10));
        item.ApproveForPublishing(
            item.ContentRevision,
            Guid.NewGuid(),
            Clawbot.Domain.Tenants.Tenant.ContentPublishingPolicyAutomatic,
            appliedPolicyVersion: 1,
            overrideReason: null,
            at: now.AddMinutes(-5));
        item.MarkScheduled(now);

        db.ContentItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task<Guid> SeedScheduleAsync(
        Guid tenantId, Guid itemId, string status, string platform = "website")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTimeOffset.UtcNow;
        var schedule = ContentSchedule.Schedule(
            tenantId, itemId, contentRevision: 1, platform, now.AddMinutes(30), now.AddMinutes(-30));

        if (status == ContentSchedule.StatusFailed)
        {
            schedule.MarkPublishing(now.AddMinutes(-20));
            schedule.MarkFailed(now, "publisher_error");
        }
        else if (status == ContentSchedule.StatusPosted)
        {
            schedule.MarkPublishing(now.AddMinutes(-20));
            schedule.MarkPosted("https://example.com/post/1", "ext-post-1", now);
        }
        else if (status == ContentSchedule.StatusOutcomeUnknown)
        {
            schedule.MarkPublishing(now.AddMinutes(-20));
            schedule.MarkOutcomeUnknown(now, "publish_timeout");
        }
        else if (status != ContentSchedule.StatusPending)
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "unsupported seed status in test helper");
        }

        db.ContentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.Id;
    }

    private async Task<HttpClient> ClientAsync() => await _factory.CreateAuthenticatedClientAsync();

    // ------------------------------------------------------------------
    // GET /trends
    // ------------------------------------------------------------------

    [Fact]
    public async Task Trends_NoMatchingBriefs_ReturnsEmptyList()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync(new Uri("/api/content/trends", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrendScanResponse>();
        body!.Trends.Should().BeEmpty();
    }

    [Fact]
    public async Task Trends_InvalidWeekFormat_ReturnsBadRequest()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync(new Uri("/api/content/trends?week=2026-13", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.week_invalid");
    }

    [Fact]
    public async Task Trends_ValidWeekWithMatchingBrief_ReturnsTrend()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var trend = new ContentTrendBrief(
            "2026-W10",
            "Học tiếng Anh online",
            "google-trends",
            "search_volume=1000",
            0.85,
            ["Ý tưởng bài viết A", "Ý tưởng bài viết B"]);
        var briefText = ContentTrendBriefFormatter.Format(trend);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var brief = ContentBrief.Create(tenantId, "facebook", briefText, null, DateTimeOffset.UtcNow);
            db.ContentBriefs.Add(brief);
            await db.SaveChangesAsync();
        }

        var client = await ClientAsync();
        var response = await client.GetAsync(new Uri("/api/content/trends?week=2026-W10", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrendScanResponse>();
        body!.Trends.Should().ContainSingle();
        var found = body.Trends[0];
        found.Topic.Should().Be("Học tiếng Anh online");
        found.Source.Should().Be("google-trends");
        found.WeekOf.Should().Be("2026-W10");
    }

    [Fact]
    public async Task Trends_ValidWeekWithoutMatchingBrief_ReturnsEmptyList()
    {
        var client = await ClientAsync();

        var response = await client.GetAsync(new Uri("/api/content/trends?week=2026-W11", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TrendScanResponse>();
        body!.Trends.Should().BeEmpty();
    }

    // ------------------------------------------------------------------
    // POST /trends/scan
    // ------------------------------------------------------------------

    [Fact]
    public async Task ScanTrends_InvalidWeekFormat_ReturnsBadRequest()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(
            new Uri("/api/content/trends/scan?week=khong-hop-le", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.week_invalid");
    }

    [Fact]
    public async Task ScanTrends_NoWeekParam_ReturnsAcceptedWithJobId()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(new Uri("/api/content/trends/scan", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("jobId").GetGuid().Should().NotBeEmpty();
    }

    // ------------------------------------------------------------------
    // POST /schedules/{id}/publish/retry
    // ------------------------------------------------------------------

    [Fact]
    public async Task RetryPublishSchedule_UnknownId_ReturnsNotFound()
    {
        var client = await ClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/content/schedules/{Guid.NewGuid():D}/publish/retry", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.schedule_not_found");
    }

    [Fact]
    public async Task RetryPublishSchedule_PendingSchedule_IsRetryable()
    {
        // HÀNH VI THẬT: TryResetForRetry() coi "pending" cũng nằm trong tập trạng thái reset được
        // (failed/held/pending) — không phải chỉ failed mới thử lại được.
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusPending);
        var client = await ClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/retry", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentScheduleDto>();
        dto!.Status.Should().Be(ContentSchedule.StatusPending);
        dto.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task RetryPublishSchedule_FailedSchedule_ResetsToPending()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusFailed);
        var client = await ClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/retry", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentScheduleDto>();
        dto!.Status.Should().Be(ContentSchedule.StatusPending);
        dto.LastError.Should().BeNull();
    }

    [Fact]
    public async Task RetryPublishSchedule_PostedSchedule_ReturnsNotRetryable()
    {
        // "posted" là trạng thái chung cuộc — không nằm trong tập failed/held/pending nên
        // TryResetForRetry() trả false.
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusPosted);
        var client = await ClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/retry", UriKind.Relative), null);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.schedule_not_retryable");
    }

    // ------------------------------------------------------------------
    // POST /schedules/{id}/publish/reconcile
    // ------------------------------------------------------------------

    [Fact]
    public async Task ReconcilePublishSchedule_BlankOutcome_ReturnsBadRequest()
    {
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{Guid.NewGuid():D}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest(""));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.reconcile_outcome_required");
    }

    [Fact]
    public async Task ReconcilePublishSchedule_InvalidOutcome_ReturnsBadRequest()
    {
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{Guid.NewGuid():D}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest("unknown"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.reconcile_outcome_invalid");
    }

    [Fact]
    public async Task ReconcilePublishSchedule_UnknownScheduleId_ReturnsNotFound()
    {
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{Guid.NewGuid():D}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest("succeeded", "ext-post-x"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.schedule_not_found");
    }

    [Fact]
    public async Task ReconcilePublishSchedule_ScheduleNotOutcomeUnknown_ReturnsUnprocessable()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusPending);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest("succeeded", "ext-post-x"));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.schedule_not_outcome_unknown");
    }

    [Fact]
    public async Task ReconcilePublishSchedule_SucceededWithInvalidExternalPostId_ReturnsBadRequest()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedPublishableItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusOutcomeUnknown);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest("succeeded", "id có ký tự lạ !!!"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.external_post_id_invalid");
    }

    [Fact]
    public async Task ReconcilePublishSchedule_Succeeded_MarksScheduleAndItemPosted()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedPublishableItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusOutcomeUnknown);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest("succeeded", "ext-post-ok-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentScheduleDto>();
        dto!.Status.Should().Be(ContentSchedule.StatusPosted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await db.ContentItems.IgnoreQueryFilters().FirstAsync(i => i.Id == itemId);
        item.Status.Should().Be("published", "reconcile succeeded phải đẩy item sang published khi không có active attempt");
    }

    [Fact]
    public async Task ReconcilePublishSchedule_Failed_MarksScheduleFailed()
    {
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        var scheduleId = await SeedScheduleAsync(tenantId, itemId, ContentSchedule.StatusOutcomeUnknown);
        var client = await ClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/schedules/{scheduleId}/publish/reconcile", UriKind.Relative),
            new ReconcilePublishRequest("failed", null, "provider_rejected"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentScheduleDto>();
        dto!.Status.Should().Be(ContentSchedule.StatusFailed);
        dto.LastError.Should().Be("provider_rejected");
    }

    [Fact]
    public async Task SyncPostPerformance_ReturnsUpdatedPerformance()
    {
        var client = await ClientAsync();
        var response = await client.PostAsync(
            new Uri("/api/content/post-performance/sync?days=30", UriKind.Relative),
            null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ContentPostPerformanceResponse>();
        dto.Should().NotBeNull();
        dto!.WindowDays.Should().Be(30);
        dto.Totals.Should().NotBeNull();
        dto.Freshness.Should().NotBeNull();
    }
}
