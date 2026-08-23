using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Clawbot.Api.Contracts.Content;
using Clawbot.Domain.Content;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// Batch bổ sung /api/content: generate validation, upload/delete asset (LocalDocumentStorage
/// thật — không có Docs:Storage:Minio:Endpoint trong test config), repurpose, hooks,
/// chain-metrics, calendar range, agent-review retry not-draft.
/// </summary>
public sealed class ContentEndpointsBatch2Tests : IAsyncLifetime
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

    private async Task<Guid> SeedItemAsync(Guid tenantId, string platform = "website", Action<ContentItem>? mutate = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = ContentItem.Create(tenantId, platform, "Nội dung test", Guid.NewGuid(), DateTimeOffset.UtcNow);
        mutate?.Invoke(item);
        db.ContentItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    // ------------------------------------------------------------------
    // POST /items/generate
    // ------------------------------------------------------------------

    [Fact]
    public async Task Generate_MissingBriefIdAndText_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/content/items/generate", UriKind.Relative),
            new GenerateContentItemRequest(null, "facebook", null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Generate_UnknownBriefId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/content/items/generate", UriKind.Relative),
            new GenerateContentItemRequest(Guid.NewGuid(), null, null));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Generate_ValidBriefText_EnqueuesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/content/items/generate", UriKind.Relative),
            new GenerateContentItemRequest(null, "facebook", "Viết bài giới thiệu khóa học"));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ------------------------------------------------------------------
    // POST /image-prompts
    // ------------------------------------------------------------------

    [Fact]
    public async Task GenerateImagePrompt_BlankBrief_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/content/image-prompts", UriKind.Relative),
            new GenerateImagePromptRequest("   ", "facebook", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateImagePrompt_UnsupportedPlatform_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/content/image-prompts", UriKind.Relative),
            new GenerateImagePromptRequest("mo ta anh", "tiktok", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GenerateImagePrompt_Valid_EnqueuesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(new Uri("/api/content/image-prompts", UriKind.Relative),
            new GenerateImagePromptRequest("mo ta anh minh hoa", "facebook", null, null));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ------------------------------------------------------------------
    // Assets: upload / get / delete (LocalDocumentStorage thật)
    // ------------------------------------------------------------------

    private static MultipartFormDataContent BuildPngUpload()
    {
        // PNG signature hợp lệ tối thiểu để ResolveAssetContentType nhận diện đúng image/png.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01, 0x02, 0x03];
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(png);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "anh-test.png");
        return content;
    }

    [Fact]
    public async Task UploadAsset_ValidPng_ReturnsUrlAndUpdatesItem()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        using var content = BuildPngUpload();

        var response = await client.PostAsync(
            new Uri($"/api/content/items/{itemId}/assets", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ContentAssetUploadResponse>();
        body!.Url.Should().NotBeNullOrWhiteSpace();
        body.AssetId.Should().NotBeEmpty();
        body.AssetsJson.Should().Contain(body.AssetId.ToString());
    }

    [Fact]
    public async Task UploadAsset_UnknownItem_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var content = BuildPngUpload();

        var response = await client.PostAsync(
            new Uri($"/api/content/items/{Guid.NewGuid()}/assets", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadAsset_InvalidContent_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes("khong phai anh"));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "gia.png");

        var response = await client.PostAsync(
            new Uri($"/api/content/items/{itemId}/assets", UriKind.Relative), content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.asset_invalid_type");
    }

    [Fact]
    public async Task GetAndDeleteAsset_RoundTrip()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        using var uploadContent = BuildPngUpload();
        var uploaded = await client.PostAsync(
            new Uri($"/api/content/items/{itemId}/assets", UriKind.Relative), uploadContent);
        var uploadBody = await uploaded.Content.ReadFromJsonAsync<ContentAssetUploadResponse>();

        var getResponse = await client.GetAsync(
            new Uri($"/api/content/items/{itemId}/assets/{uploadBody!.AssetId}", UriKind.Relative));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var deleteResponse = await client.DeleteAsync(
            new Uri($"/api/content/items/{itemId}/assets/{uploadBody.AssetId}", UriKind.Relative));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await client.GetAsync(
            new Uri($"/api/content/items/{itemId}/assets/{uploadBody.AssetId}", UriKind.Relative));
        afterDelete.StatusCode.Should().Be(HttpStatusCode.NotFound, "asset đã xoá không còn đọc được");
    }

    [Fact]
    public async Task GetAsset_UnknownAsset_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.GetAsync(
            new Uri($"/api/content/items/{itemId}/assets/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Repurpose
    // ------------------------------------------------------------------

    [Fact]
    public async Task Repurpose_EmptyTargets_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/repurpose", UriKind.Relative),
            new RepurposeContentItemRequest([]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Repurpose_UnsupportedPlatform_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/repurpose", UriKind.Relative),
            new RepurposeContentItemRequest(["tiktok"]));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Repurpose_UnknownItem_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{Guid.NewGuid()}/repurpose", UriKind.Relative),
            new RepurposeContentItemRequest(["zalo"]));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Repurpose_ValidRequest_EnqueuesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/repurpose", UriKind.Relative),
            new RepurposeContentItemRequest(["zalo", "instagram"]));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ------------------------------------------------------------------
    // Hooks
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetHooks_ItemWithoutChain_ReturnsUnavailable()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var body = await client.GetFromJsonAsync<ContentItemHooksResponse>(
            new Uri($"/api/content/items/{itemId}/hooks", UriKind.Relative));

        body!.Available.Should().BeFalse();
        body.Hooks.Should().BeEmpty();
    }

    [Fact]
    public async Task RegenerateHook_NegativeIndex_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/regenerate-hook", UriKind.Relative),
            new RegenerateHookApiRequest(-1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RegenerateHook_UnknownItem_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{Guid.NewGuid()}/regenerate-hook", UriKind.Relative),
            new RegenerateHookApiRequest(0));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RegenerateHook_ValidItem_EnqueuesJob()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/regenerate-hook", UriKind.Relative),
            new RegenerateHookApiRequest(0));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }

    // ------------------------------------------------------------------
    // Agent review retry
    // ------------------------------------------------------------------

    [Fact]
    public async Task RetryAgentReview_MissingExpectedRevision_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/agent-review/retry", UriKind.Relative),
            new RetryAgentReviewRequest(0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RetryAgentReview_RevisionMismatch_ReturnsConflict()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/content/items/{itemId}/agent-review/retry", UriKind.Relative),
            new RetryAgentReviewRequest(999));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.revision_changed");
    }

    // ------------------------------------------------------------------
    // Chain metrics
    // ------------------------------------------------------------------

    [Fact]
    public async Task ChainMetrics_WithTraces_AggregatesTokensAndCost()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var chainRunId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ContentGenerationTraces.Add(ContentGenerationTrace.Create(
                tenantId, chainRunId, "plan", "v1", "gpt-test",
                inputTokens: 100, outputTokens: 50, usdCost: 0.01m, latencyMs: 200,
                gateResult: "passed", payloadJson: null, DateTimeOffset.UtcNow));
            db.ContentGenerationTraces.Add(ContentGenerationTrace.Create(
                tenantId, chainRunId, "outline", "v1", "gpt-test",
                inputTokens: 80, outputTokens: 40, usdCost: 0.008m, latencyMs: 150,
                gateResult: "failed", payloadJson: null, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }

        var body = await client.GetFromJsonAsync<ContentChainMetricsResponse>(
            new Uri("/api/content/chain-metrics?days=7", UriKind.Relative));

        body!.TotalRuns.Should().Be(1);
        body.AvgTokensPerRun.Should().Be(270, "100+50+80+40 gộp trên 1 chainRunId");
        var outlineStep = body.Steps.First(s => s.StepId == "outline");
        outlineStep.GateFailures.Should().Be(1);
    }

    [Fact]
    public async Task ChainMetrics_NoData_ReturnsZeroedResponse()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var body = await client.GetFromJsonAsync<ContentChainMetricsResponse>(
            new Uri("/api/content/chain-metrics", UriKind.Relative));

        body!.TotalRuns.Should().Be(0);
        body.FallbackRate.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Calendar
    // ------------------------------------------------------------------

    [Fact]
    public async Task Calendar_InvalidRange_ReturnsBadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var from = DateTimeOffset.UtcNow;
        var to = from.AddDays(-1);

        var response = await client.GetAsync(new Uri(
            $"/api/content/calendar?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}",
            UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.calendar_range_invalid");
    }

    [Fact]
    public async Task Calendar_DefaultRange_ReturnsOk()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/content/calendar", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------
    // Publish targets — nhánh platform không hỗ trợ
    // ------------------------------------------------------------------

    [Fact]
    public async Task PublishTargets_UnsupportedPlatform_ReturnsEmptyWithHeader()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/content/publish-targets?platform=tiktok", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Clawbot-Publish-Target-Mode").Should().Contain("unsupported");
        (await response.Content.ReadAsStringAsync()).Should().Be("[]");
    }

    // ------------------------------------------------------------------
    // Delete schedule — nhánh trạng thái không huỷ được
    // ------------------------------------------------------------------

    [Fact]
    public async Task DeleteSchedule_PostedStatus_IsNotCancelable()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var itemId = await SeedItemAsync(tenantId);
        Guid scheduleId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTimeOffset.UtcNow;
            var schedule = ContentSchedule.Schedule(
                tenantId, itemId, contentRevision: 1, "website", now.AddMinutes(-5), now.AddMinutes(-10));
            schedule.MarkPublishing(now.AddMinutes(-1));
            schedule.MarkPosted("https://example.com/post", externalPostId: null, now);
            db.ContentSchedules.Add(schedule);
            await db.SaveChangesAsync();
            scheduleId = schedule.Id;
        }

        var response = await client.DeleteAsync(new Uri($"/api/content/schedule/{scheduleId}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("content.schedule_not_cancelable");
    }
}
