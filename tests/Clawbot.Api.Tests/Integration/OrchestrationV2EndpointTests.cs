using System.Net;
using System.Net.Http.Json;
using Clawbot.Domain.Agents;
using Clawbot.Domain.Tenants;
using Clawbot.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Clawbot.Api.Tests.Integration;

/// <summary>
/// /api/orchestration/v2/*. AgentService:Url trỏ https://localhost:5001 (không có server thật) —
/// mọi endpoint gọi qua Orchestrator.OrchestratorClient (CreateRun/GetRun/UpdateRunPlan/ApproveRun/
/// ControlRun/InterveneTask/RunScheduleNow) không thể test đường happy-path thật, chỉ test nhánh
/// validate trước gRPC + nhánh lỗi 502/503 khi kênh gRPC không kết nối được (mỗi request ~10s do
/// grpc chờ subchannel — CHỈ giữ 2 test loại này, không lặp cho từng endpoint). Các endpoint thuần
/// EF (schedules CRUD, agents CRUD, archive/unarchive, cost-summary) test đầy đủ vì nhanh và an toàn.
/// DeleteScheduleAsync dùng FromSqlInterpolated raw SQL — InMemory provider không hỗ trợ (đã bị
/// loại khỏi ParameterisedRouteSweepTests với cùng lý do) nên không test ở đây.
/// SuggestPlansAsync launch qua Hangfire IJobLauncher — ngoài phạm vi, không test.
/// </summary>
public sealed class OrchestrationV2EndpointTests : IAsyncLifetime
{
    private readonly ApiTestFactory _factory = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Validate trước gRPC (không chạm network, nhanh)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateRun_BlankGoal_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/runs", UriKind.Relative),
            new { Goal = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateRunPlan_BlankPlanJson_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PutAsJsonAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}/plan", UriKind.Relative),
            new { PlanJson = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("teleport")]
    public async Task ControlRun_InvalidAction_IsRejected(string action)
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}/control", UriKind.Relative),
            new { Action = action });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InterveneTask_BlankTaskId_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // taskId trong route là string, để trắng thì route không match segment -> dùng khoảng trắng mã hoá.
        var response = await client.PostAsJsonAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}/tasks/%20/intervene", UriKind.Relative),
            new { Action = "retry" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task InterveneTask_InvalidAction_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}/tasks/t1/intervene", UriKind.Relative),
            new { Action = "delete_everything" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------
    // gRPC không kết nối được -> mapping lỗi (chỉ 2 đại diện, ~10s mỗi cái)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateRun_GrpcUnreachable_MapsToBadGateway()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/runs", UriKind.Relative),
            new { Goal = "Chăm sóc khách hàng tuần này" });

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task RunScheduleNow_GrpcUnreachable_MapsToServiceUnavailable()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/orchestration/v2/schedules/{Guid.NewGuid():D}/run-now", UriKind.Relative),
            content: null);

        // ToScheduleRunGrpcResult map StatusCode.Unavailable -> 503, khác ToGrpcResult (502 default).
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    // ------------------------------------------------------------------
    // Schedules CRUD (thuần EF)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateSchedule_MissingRequiredFields_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new { Name = "", GoalTemplate = "", Cadence = "", TimezoneId = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateSchedule_RequiresApprovalTrue_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = "Báo cáo tuần",
                GoalTemplate = "Tổng hợp KPI tuần",
                Cadence = "weekly",
                TimezoneId = "Asia/Ho_Chi_Minh",
                RequiresApproval = true,
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("schedule_approval_not_supported");
    }

    [Fact]
    public async Task CreateSchedule_InvalidCadence_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = "Báo cáo",
                GoalTemplate = "Tổng hợp KPI",
                Cadence = "hourly",
                TimezoneId = "Asia/Ho_Chi_Minh",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_cadence");
    }

    [Fact]
    public async Task CreateSchedule_InvalidTimezone_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = "Báo cáo",
                GoalTemplate = "Tổng hợp KPI",
                Cadence = "weekly",
                TimezoneId = "Khong/Ton_Tai",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_timezone");
    }

    [Fact]
    public async Task CreateSchedule_EventTriggerWithUnknownEventKey_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = "Báo cáo",
                GoalTemplate = "Tổng hợp KPI",
                Cadence = "weekly",
                TimezoneId = "Asia/Ho_Chi_Minh",
                TriggerType = "event",
                EventKey = "khong-ton-tai",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_event_key");
    }

    [Fact]
    public async Task CreateSchedule_ValidRequest_CreatesAndListsSchedule()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var name = $"Chăm sóc khách {Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = name,
                GoalTemplate = "Nhắn lại khách tiềm năng lâu không tương tác",
                Cadence = "daily",
                TimezoneId = "Asia/Ho_Chi_Minh",
            });

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await client.GetAsync(new Uri("/api/orchestration/v2/schedules", UriKind.Relative));
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().Contain(name);
    }

    [Fact]
    public async Task PauseSchedule_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/orchestration/v2/schedules/{Guid.NewGuid():D}/pause", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PauseThenActivateSchedule_TogglesIsActive()
    {
        var scheduleId = await SeedScheduleAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var paused = await client.PostAsync(
            new Uri($"/api/orchestration/v2/schedules/{scheduleId:D}/pause", UriKind.Relative),
            content: null);
        paused.StatusCode.Should().Be(HttpStatusCode.OK);
        var pausedBody = await paused.Content.ReadAsStringAsync();
        pausedBody.Should().Contain("\"isActive\":false");

        var activated = await client.PostAsync(
            new Uri($"/api/orchestration/v2/schedules/{scheduleId:D}/activate", UriKind.Relative),
            content: null);
        activated.StatusCode.Should().Be(HttpStatusCode.OK);
        var activatedBody = await activated.Content.ReadAsStringAsync();
        activatedBody.Should().Contain("\"isActive\":true");
    }

    // ------------------------------------------------------------------
    // Runs: list / archive / unarchive (thuần EF)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListRuns_ReturnsPagedEnvelope()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/orchestration/v2/runs?pageSize=5", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"items\"");
        body.Should().Contain("\"nextCursor\"");
    }

    [Fact]
    public async Task ArchiveRun_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}/archive", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ArchiveRun_StillRunning_IsRejected()
    {
        // AgentSession.Archive() chỉ chấp nhận completed/failed/cancelled; session mới Start() ở
        // trạng thái running -> phải bị từ chối.
        var sessionId = await SeedSessionAsync(AgentSessionStatuses.Running);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{sessionId:D}/archive", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("session_not_archivable");
    }

    [Fact]
    public async Task ArchiveThenUnarchiveRun_TogglesArchivedAt()
    {
        var sessionId = await SeedSessionAsync(AgentSessionStatuses.Completed);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var archived = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{sessionId:D}/archive", UriKind.Relative),
            content: null);
        archived.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var session = await db.AgentSessions.IgnoreQueryFilters().FirstAsync(s => s.Id == sessionId);
            session.ArchivedAt.Should().NotBeNull();
        }

        var unarchived = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{sessionId:D}/unarchive", UriKind.Relative),
            content: null);
        unarchived.StatusCode.Should().Be(HttpStatusCode.OK);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await verifyDb.AgentSessions.IgnoreQueryFilters().FirstAsync(s => s.Id == sessionId);
        reloaded.ArchivedAt.Should().BeNull();
    }

    [Fact]
    public async Task UnarchiveRun_UnknownId_ReturnsNotFound()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}/unarchive", UriKind.Relative),
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ------------------------------------------------------------------
    // Agents CRUD (thuần EF)
    // ------------------------------------------------------------------

    [Fact]
    public async Task ListAgents_ReturnsItemsEnvelope()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/orchestration/v2/agents", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"items\"");
    }

    [Fact]
    public async Task UpsertAgent_MissingRequiredFields_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new { Code = "", DisplayName = "", AgentType = "worker", PersonaPrompt = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpsertAgent_InvalidAllowedToolsJson_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new
            {
                Code = $"agent-{Guid.NewGuid():N}",
                DisplayName = "Test Agent",
                AgentType = "worker",
                PersonaPrompt = "Bạn là trợ lý test.",
                AllowedToolsJson = "not-json-array",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_allowed_tools_json");
    }

    [Fact]
    public async Task UpsertAgent_InvalidInputSchemaJson_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new
            {
                Code = $"agent-{Guid.NewGuid():N}",
                DisplayName = "Test Agent",
                AgentType = "worker",
                PersonaPrompt = "Bạn là trợ lý test.",
                InputSchemaJson = "[1,2,3]",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_input_schema_json");
    }

    [Fact]
    public async Task UpsertAgent_UnknownToolName_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new
            {
                Code = $"agent-{Guid.NewGuid():N}",
                DisplayName = "Test Agent",
                AgentType = "worker",
                PersonaPrompt = "Bạn là trợ lý test.",
                AllowedToolsJson = "[\"cong_cu_khong_ton_tai\"]",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("unknown_tool:");
    }

    [Fact]
    public async Task UpsertAgent_UnknownLlmConfigId_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new
            {
                Code = $"agent-{Guid.NewGuid():N}",
                DisplayName = "Test Agent",
                AgentType = "worker",
                PersonaPrompt = "Bạn là trợ lý test.",
                LlmConfigId = Guid.NewGuid(),
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("llm_config_not_found");
    }

    [Fact]
    public async Task UpsertAgent_ValidCreateThenUpdate_UpsertsSameCode()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var code = $"agent-{Guid.NewGuid():N}";

        var created = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new
            {
                Code = code,
                DisplayName = "Trợ lý Test",
                AgentType = "worker",
                PersonaPrompt = "Bạn là trợ lý test.",
            });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var createdBody = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var id = createdBody.GetProperty("id").GetGuid();

        var updated = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/agents", UriKind.Relative),
            new
            {
                Code = code,
                DisplayName = "Trợ lý Test (đã sửa)",
                AgentType = "worker",
                PersonaPrompt = "Bạn là trợ lý test đã cập nhật.",
            });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedBody = await updated.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        // Cùng code -> update tại chỗ, không tạo bản ghi mới.
        updatedBody.GetProperty("id").GetGuid().Should().Be(id);
        updatedBody.GetProperty("displayName").GetString().Should().Be("Trợ lý Test (đã sửa)");
    }

    // ------------------------------------------------------------------
    // Cost summary (thuần in-memory tracker)
    // ------------------------------------------------------------------

    [Fact]
    public async Task CostSummary_ReturnsMonthToDateAndCap()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/orchestration/v2/cost-summary", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("monthToDateUsd");
        body.Should().Contain("capUsd");
    }

    // ------------------------------------------------------------------
    // Schedule event-trigger + ArchiveRun idempotent + ListRuns mine
    // ------------------------------------------------------------------

    [Fact]
    public async Task CreateSchedule_EventTriggerWithKnownEventKey_Succeeds()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = $"Su kien {Guid.NewGuid():N}",
                GoalTemplate = "Xu ly khi lead thanh hot",
                Cadence = "daily",
                TimezoneId = "Asia/Ho_Chi_Minh",
                TriggerType = "event",
                EventKey = Clawbot.SharedKernel.Orchestration.ScheduleEventKeys.LeadBecameHot,
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        // Lich event ngu toi khi su kien xay ra -> NextRunAt = DateTimeOffset.MaxValue.
        body.Should().Contain("\"triggerType\":\"event\"");
    }

    [Fact]
    public async Task CreateSchedule_InvalidTriggerType_IsRejected()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync(
            new Uri("/api/orchestration/v2/schedules", UriKind.Relative),
            new
            {
                Name = "Lich la",
                GoalTemplate = "Goal",
                Cadence = "daily",
                TimezoneId = "Asia/Ho_Chi_Minh",
                TriggerType = "webhook",
            });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("invalid_trigger_type");
    }

    [Fact]
    public async Task ArchiveRun_AlreadyArchived_IsIdempotent()
    {
        var sessionId = await SeedSessionAsync(AgentSessionStatuses.Completed);
        var client = await _factory.CreateAuthenticatedClientAsync();

        var first = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{sessionId:D}/archive", UriKind.Relative), content: null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Archive lần 2 -> nhánh session.ArchivedAt is not null -> trả OK ngay, không throw.
        var second = await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{sessionId:D}/archive", UriKind.Relative), content: null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListRuns_MineFilter_ReturnsOnlyOwnRuns()
    {
        var adminUserId = await GetAdminUserIdAsync();
        var mineSessionId = await SeedSessionWithUserAsync(adminUserId);
        var otherSessionId = await SeedSessionWithUserAsync(Guid.NewGuid());
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(new Uri("/api/orchestration/v2/runs?mine=true&pageSize=100", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(mineSessionId.ToString());
        body.Should().NotContain(otherSessionId.ToString());
    }

    [Fact]
    public async Task ListRuns_ArchivedFilter_ReturnsOnlyArchivedRuns()
    {
        var archivedSessionId = await SeedSessionAsync(AgentSessionStatuses.Completed);
        var client = await _factory.CreateAuthenticatedClientAsync();
        await client.PostAsync(
            new Uri($"/api/orchestration/v2/runs/{archivedSessionId:D}/archive", UriKind.Relative), content: null);

        var response = await client.GetAsync(new Uri("/api/orchestration/v2/runs?archived=true&pageSize=100", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(archivedSessionId.ToString());
    }

    [Fact]
    public async Task GetRun_GrpcUnreachable_MapsToBadGateway()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync(
            new Uri($"/api/orchestration/v2/runs/{Guid.NewGuid():D}", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    // DeleteScheduleAsync dùng FromSqlInterpolated raw SQL — InMemory provider không hỗ trợ, request
    // trả 500 thay vì propagate exception (đã ghi chú ở đầu file), nên không test nhánh này ở đây.

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private async Task<Guid> GetAdminUserIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Users.IgnoreQueryFilters()
            .FirstAsync(u => u.Email == ApiTestFactory.AdminEmail)).Id;
    }

    private async Task<Guid> SeedSessionWithUserAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var session = AgentSession.Start(tenant.Id, null, null, "Goal mine filter", DateTimeOffset.UtcNow, userId);
        db.AgentSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Guid> SeedScheduleAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var schedule = AgentSchedule.Create(
            tenant.Id, $"Lịch {Guid.NewGuid():N}", "Goal mẫu", "daily", null, "Asia/Ho_Chi_Minh",
            DateTimeOffset.UtcNow.AddHours(1), requiresApproval: false, createdAt: DateTimeOffset.UtcNow,
            triggerType: "cadence", eventKey: null, initiatorUserId: Guid.NewGuid());
        db.AgentSchedules.Add(schedule);
        await db.SaveChangesAsync();
        return schedule.Id;
    }

    private async Task<Guid> SeedSessionAsync(string status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Slug == "default");
        var session = AgentSession.Start(tenant.Id, null, null, "Goal mẫu", DateTimeOffset.UtcNow);
        db.AgentSessions.Add(session);
        if (status != AgentSessionStatuses.Running)
            db.Entry(session).Property(nameof(AgentSession.Status)).CurrentValue = status;
        await db.SaveChangesAsync();
        return session.Id;
    }
}
