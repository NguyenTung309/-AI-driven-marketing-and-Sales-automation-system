using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/jobs — job center của tenant (dialog /agents đọc). Admin có jobs:view + jobs:manage.
/// Retry enqueue Hangfire thật (SQL storage đăng ký kể cả passive mode) nên chỉ assert hàng đợi
/// chứ không chờ job chạy.
/// </summary>
public sealed class JobsEndpointsTests : IClassFixture<ApiTestFactory>
{
    private readonly ApiTestFactory _factory;

    public JobsEndpointsTests(ApiTestFactory factory) => _factory = factory;

    private async Task<Guid> GetAdminTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    /// <summary>Seed job; mutate là hành động domain (MarkFailed/MarkRunning...) trước khi lưu.</summary>
    private async Task<Guid> SeedJobAsync(Guid tenantId, Guid? userId, Action<BackgroundJob>? mutate = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = BackgroundJob.Queue(tenantId, userId, "content.generate",
            $"Job test {Guid.NewGuid():N}", "{}", DateTimeOffset.UtcNow);
        mutate?.Invoke(job);
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    // ------------------------------------------------------------------
    // GET list
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_ReturnsJobs_AndFiltersByStatus()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var queuedId = await SeedJobAsync(tenantId, userId: null);
        var failedId = await SeedJobAsync(tenantId, userId: null, j => j.MarkFailed("loi test", DateTimeOffset.UtcNow));

        var all = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/jobs", UriKind.Relative));
        var allIds = all.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
        allIds.Should().Contain(queuedId).And.Contain(failedId);
        all.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(2);

        var failed = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/jobs?status=failed", UriKind.Relative));
        failed.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("status").GetString() == "failed");

        // "active" = queued + running — tab mặc định của dialog.
        var active = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/jobs?status=active", UriKind.Relative));
        active.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("status").GetString())
            .Should().OnlyContain(s => s == "queued" || s == "running");
    }

    [Fact]
    public async Task List_MineFilter_ReturnsOnlyCurrentUserJobs()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        Guid adminId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminId = (await db.Users.IgnoreQueryFilters()
                .FirstAsync(u => u.Email == ApiTestFactory.AdminEmail)).Id;
        }

        var mineId = await SeedJobAsync(tenantId, adminId);
        var otherId = await SeedJobAsync(tenantId, userId: null);

        var body = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/jobs?mine=true", UriKind.Relative));

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => Guid.Parse(i.GetProperty("id").GetString()!)).ToList();
        ids.Should().Contain(mineId).And.NotContain(otherId);
    }

    // ------------------------------------------------------------------
    // GET detail
    // ------------------------------------------------------------------

    [Fact]
    public async Task Get_ReturnsJobDetail()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var jobId = await SeedJobAsync(tenantId, userId: null);

        var body = await client.GetFromJsonAsync<JsonElement>(
            new Uri($"/api/jobs/{jobId}", UriKind.Relative));

        body.GetProperty("id").GetString().Should().Be(jobId.ToString());
        body.GetProperty("type").GetString().Should().Be("content.generate");
        body.GetProperty("status").GetString().Should().Be("queued");
    }

    [Fact]
    public async Task Get_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri($"/api/jobs/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("job_not_found");
    }

    // ------------------------------------------------------------------
    // Cancel
    // ------------------------------------------------------------------

    [Fact]
    public async Task Cancel_QueuedJob_MarksCancelledImmediately()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var jobId = await SeedJobAsync(tenantId, userId: null);

        var response = await client.PostAsync(new Uri($"/api/jobs/{jobId}/cancel", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("cancelled", "job queued huỷ được ngay");
    }

    [Fact]
    public async Task Cancel_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/jobs/{Guid.NewGuid()}/cancel", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Retry
    // ------------------------------------------------------------------

    [Fact]
    public async Task Retry_FailedJob_RequeuesOnSameRow()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var jobId = await SeedJobAsync(tenantId, userId: null, j => j.MarkFailed("loi se retry", DateTimeOffset.UtcNow));

        var response = await client.PostAsync(new Uri($"/api/jobs/{jobId}/retry", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var job = await db.BackgroundJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be("queued", "retry chạy lại trên chính row cũ");
        job.Error.Should().BeNull();
        job.HangfireJobId.Should().NotBeNull("retry phải enqueue Hangfire");
    }

    [Fact]
    public async Task Retry_NonTerminalJob_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var tenantId = await GetAdminTenantIdAsync();
        var jobId = await SeedJobAsync(tenantId, userId: null);

        var response = await client.PostAsync(new Uri($"/api/jobs/{jobId}/retry", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("job_not_retryable");
    }

    [Fact]
    public async Task Retry_Unknown_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri($"/api/jobs/{Guid.NewGuid()}/retry", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
