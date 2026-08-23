using System.Net;
using System.Net.Http.Json;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Jobs;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/admin/jobs/*. Hangfire ở ApiTestFactory dùng SqlServerStorage thật (ConnectionStrings:SqlServer
/// trỏ (local)\clawbot_test) — không phải InMemory — nên JobStorage.Current/IBackgroundJobClient hoạt
/// động thật trong test host (đã kiểm chứng: /api/admin/jobs nằm trong AuthenticatedReadEndpointTests
/// sweep và pass từ trước). StartupMode passive chỉ tắt AddHangfireServer + ScheduleClawbotJobs — nên
/// recurring jobs LUÔN rỗng ở test host (chưa từng AddOrUpdate) dù JobMeta có sẵn entries tĩnh.
/// RunScheduleNowAsync gọi Orchestrator.OrchestratorClient (gRPC, AgentService:Url không có server
/// thật) — chỉ giữ 1 test cho nhánh lỗi 502/503, không lặp cho từng biến thể (xem OrchestrationV2EndpointTests).
/// </summary>
public sealed class AdminJobsEndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    private async Task<Guid> DefaultTenantIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        return tenant.Id;
    }

    private async Task<Guid> SeedExecutionAsync(
        Guid tenantId, string definitionId, string status, Guid? requestedByUserId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var execution = RecurringJobExecution.CreateManual(
            definitionId, requestedByUserId ?? Guid.NewGuid(), tenantId, Guid.NewGuid().ToString("D"),
            DateTimeOffset.UtcNow);
        if (status != RecurringJobExecutionStatuses.Requested)
        {
            db.Entry(execution).Property(nameof(RecurringJobExecution.Status)).CurrentValue = status;
            if (RecurringJobExecutionStatuses.IsTerminal(status))
                db.Entry(execution).Property(nameof(RecurringJobExecution.FinishedAt)).CurrentValue = DateTimeOffset.UtcNow;
        }
        db.RecurringJobExecutions.Add(execution);
        await db.SaveChangesAsync();
        return execution.Id;
    }

    private async Task<Guid> SeedScheduleAsync(Guid tenantId, string? goalTemplate = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var schedule = AgentSchedule.Create(
            tenantId, $"Lịch {Guid.NewGuid():N}", goalTemplate ?? "Goal mẫu", "daily", null,
            "Asia/Ho_Chi_Minh", DateTimeOffset.UtcNow.AddHours(1), requiresApproval: false,
            createdAt: DateTimeOffset.UtcNow, triggerType: "cadence", eventKey: null,
            initiatorUserId: Guid.NewGuid());
        db.AgentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.Id;
    }

    private async Task<Guid> SeedScheduleRunAsync(Guid tenantId, Guid scheduleId, string? error = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = AgentScheduleRun.Start(tenantId, scheduleId, $"window-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        if (error is not null) run.Fail(error, DateTimeOffset.UtcNow);
        db.AgentScheduleRuns.Add(run);
        await db.SaveChangesAsync();
        return run.Id;
    }

    private static HttpRequestMessage WithIdempotencyKey(HttpMethod method, string path, string? key)
    {
        var request = new HttpRequestMessage(method, path);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    // ------------------------------------------------------------------
    // List
    // ------------------------------------------------------------------

    [Fact]
    public async Task List_NoScheduledRecurringJobs_ReturnsEmptyRecurring()
    {
        // StartupMode passive -> ScheduleClawbotJobs() không chạy -> Hangfire storage không có
        // recurring job nào đăng ký, dù JobMeta tĩnh có nhiều entries.
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/jobs", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"recurring\":[]");
    }

    [Fact]
    public async Task List_WithTrendScanSchedule_MapsToTrendScanKind()
    {
        var tenantId = await DefaultTenantIdAsync();
        await SeedScheduleAsync(tenantId, Clawbot.SharedKernel.Content.ContentTrendSettings.ScheduleGoalMarker);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/jobs", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"kind\":\"trend-scan\"");
        body.Should().Contain("\"agent\":\"research-agent\"");
    }

    [Fact]
    public async Task List_WithRegularSchedule_MapsToOrchestrationKind()
    {
        var tenantId = await DefaultTenantIdAsync();
        await SeedScheduleAsync(tenantId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/admin/jobs", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"kind\":\"orchestration\"");
    }

    // ------------------------------------------------------------------
    // Trigger recurring (HealthCheck) — idempotency-key + enqueue thật qua Hangfire
    // ------------------------------------------------------------------

    [Fact]
    public async Task TriggerRecurring_UnknownDefinition_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, "/api/admin/jobs/recurring/khong-ton-tai/trigger", Guid.NewGuid().ToString("D")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TriggerRecurring_MissingIdempotencyKey_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, "/api/admin/jobs/recurring/health-check/trigger", key: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("idempotency_key_required");
    }

    [Fact]
    public async Task TriggerRecurring_InvalidIdempotencyKey_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, "/api/admin/jobs/recurring/health-check/trigger", "not-a-guid"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("idempotency_key_invalid");
    }

    [Fact]
    public async Task TriggerRecurring_ValidRequest_EnqueuesAndReturnsAccepted()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, "/api/admin/jobs/recurring/health-check/trigger", Guid.NewGuid().ToString("D")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("health-check");
    }

    [Fact]
    public async Task TriggerRecurring_SameIdempotencyKeyTwice_ReusesExecution()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var key = Guid.NewGuid().ToString("D");

        var first = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, "/api/admin/jobs/recurring/health-check/trigger", key));
        var firstBody = await first.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var firstId = firstBody.GetProperty("trackingId").GetGuid();

        var second = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, "/api/admin/jobs/recurring/health-check/trigger", key));
        // Execution đã có HangfireBackgroundJobId từ lần đầu -> EnqueueManualExecutionAsync trả
        // Ok (200) thay vì Accepted (202) ở nhánh reuse.
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        secondBody.GetProperty("trackingId").GetGuid().Should().Be(firstId);
    }

    // ------------------------------------------------------------------
    // Executions listing / detail
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetRecurringExecutions_UnknownDefinition_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri("/api/admin/jobs/recurring/khong-ton-tai/executions?pageSize=20", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRecurringExecutions_KnownDefinition_ReturnsSeededExecution()
    {
        var tenantId = await DefaultTenantIdAsync();
        await SeedExecutionAsync(tenantId, "health-check", RecurringJobExecutionStatuses.Succeeded);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri("/api/admin/jobs/recurring/health-check/executions?pageSize=20", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"total\"");
    }

    [Fact]
    public async Task GetRecurringExecution_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/executions/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRecurringExecution_KnownId_ReturnsDetail()
    {
        var tenantId = await DefaultTenantIdAsync();
        var executionId = await SeedExecutionAsync(tenantId, "health-check", RecurringJobExecutionStatuses.Running);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/executions/{executionId:D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"running\"");
    }

    [Fact]
    public async Task GetRecurringExecutionAttempts_UnknownExecution_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/executions/{Guid.NewGuid():D}/attempts?pageSize=20", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetRecurringExecutionAttempts_KnownExecution_ReturnsPagedEnvelope()
    {
        var tenantId = await DefaultTenantIdAsync();
        var executionId = await SeedExecutionAsync(tenantId, "health-check", RecurringJobExecutionStatuses.Running);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.RecurringJobExecutionAttempts.Add(
                RecurringJobExecutionAttempt.Start(executionId, "hf-job-1", 0, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
        }
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/executions/{executionId:D}/attempts?pageSize=20", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("hf-job-1");
    }

    // ------------------------------------------------------------------
    // Retry
    // ------------------------------------------------------------------

    [Fact]
    public async Task RetryRecurringExecution_MissingIdempotencyKey_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, $"/api/admin/jobs/executions/{Guid.NewGuid():D}/retry", key: null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RetryRecurringExecution_UnknownExecution_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, $"/api/admin/jobs/executions/{Guid.NewGuid():D}/retry", Guid.NewGuid().ToString("D")));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RetryRecurringExecution_NotYetTerminal_IsRejected()
    {
        var tenantId = await DefaultTenantIdAsync();
        var executionId = await SeedExecutionAsync(tenantId, "health-check", RecurringJobExecutionStatuses.Running);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, $"/api/admin/jobs/executions/{executionId:D}/retry", Guid.NewGuid().ToString("D")));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("recurring_execution_not_retryable");
    }

    [Fact]
    public async Task RetryRecurringExecution_Failed_EnqueuesRetry()
    {
        var tenantId = await DefaultTenantIdAsync();
        var executionId = await SeedExecutionAsync(tenantId, "health-check", RecurringJobExecutionStatuses.Failed);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(WithIdempotencyKey(
            HttpMethod.Post, $"/api/admin/jobs/executions/{executionId:D}/retry", Guid.NewGuid().ToString("D")));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("health-check");
    }

    // ------------------------------------------------------------------
    // Backfill KPI
    // ------------------------------------------------------------------

    [Fact]
    public async Task TriggerKpiBackfill_DefaultDays_Enqueues30DayBackfill()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(new Uri("/api/admin/jobs/backfill-kpi", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"days\":30");
    }

    [Fact]
    public async Task TriggerKpiBackfill_CustomDays_UsesRequestedValue()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri("/api/admin/jobs/backfill-kpi?days=7", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"days\":7");
    }

    // ------------------------------------------------------------------
    // Schedule run detail
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetScheduleRun_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/schedule-runs/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetScheduleRun_WithError_MasksDetailWithGenericMessage()
    {
        var tenantId = await DefaultTenantIdAsync();
        var scheduleId = await SeedScheduleAsync(tenantId);
        var runId = await SeedScheduleRunAsync(tenantId, scheduleId, error: "chi tiết lỗi nhạy cảm nội bộ");
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/schedule-runs/{runId:D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Tác vụ thực thi không thành công.");
        body.Should().NotContain("chi tiết lỗi nhạy cảm nội bộ");
    }

    [Fact]
    public async Task GetScheduleRun_WithoutError_HasNullError()
    {
        var tenantId = await DefaultTenantIdAsync();
        var scheduleId = await SeedScheduleAsync(tenantId);
        var runId = await SeedScheduleRunAsync(tenantId, scheduleId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/admin/jobs/schedule-runs/{runId:D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"error\":null");
    }

    // ------------------------------------------------------------------
    // Schedules pause/activate/run-now
    // ------------------------------------------------------------------

    [Fact]
    public async Task PauseSchedule_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/admin/jobs/schedules/{Guid.NewGuid():D}/pause", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PauseThenActivateSchedule_TogglesIsActive()
    {
        var tenantId = await DefaultTenantIdAsync();
        var scheduleId = await SeedScheduleAsync(tenantId);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var paused = await client.PostAsync(
            new Uri($"/api/admin/jobs/schedules/{scheduleId:D}/pause", UriKind.Relative), content: null);
        paused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await paused.Content.ReadAsStringAsync()).Should().Contain("\"isActive\":false");

        var activated = await client.PostAsync(
            new Uri($"/api/admin/jobs/schedules/{scheduleId:D}/activate", UriKind.Relative), content: null);
        activated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await activated.Content.ReadAsStringAsync()).Should().Contain("\"isActive\":true");
    }

    [Fact]
    public async Task RunScheduleNow_GrpcUnreachable_MapsToServiceUnavailable()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/admin/jobs/schedules/{Guid.NewGuid():D}/run-now", UriKind.Relative), content: null);

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }
}
