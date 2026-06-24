# Dynamic Agent Orchestration v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build autonomous Semantic Kernel agent-to-agent orchestration with data-defined sub-agents, recurring daily/weekly/monthly/quarterly schedules, and encrypted local OpenAI-compatible demo LLM seed.

**Architecture:** Extend the V1 `AgentSession`/`agent_traces` orchestration foundation. Add data-defined sub-agents, persisted A2A mailbox, scheduler worker, V2 API endpoints, minimal UI, and demo seed path. Semantic Kernel coordinates bounded loops; DB persistence makes runs auditable and resumable.

**Tech Stack:** .NET/C#, EF Core, SQL Server migrations, gRPC/API endpoints, Semantic Kernel, existing `ScopedLlmChatClient`, existing LLM config encryption, React frontend.

## Global Constraints

- Do not commit plaintext API keys to source, docs, SQL, tests, logs, or appsettings.
- Demo local provider: `openai-compatible`, model `cx/gpt-5.5`, base URL `http://localhost:20128/v1`.
- SQL migrations must not contain `GO`.
- Indexes on columns added by `ALTER TABLE` go in separate migration files when needed.
- Persist derived customer/chat/document text only after PII redaction.
- Recurrence uses tenant timezone, not server local time.
- Default overlap policy is `skip`.
- Autonomous loops require max rounds, max runtime, cost cap, cancellation, RBAC, and approval gates.

---

## File Structure

Create/modify these files:

- Create: `src/shared/Clawbot.Domain/Agents/AgentDefinition.cs` — data-defined sub-agent/persona.
- Create: `src/shared/Clawbot.Domain/Agents/AgentA2AMessage.cs` — persisted A2A mailbox message.
- Create: `src/shared/Clawbot.Domain/Agents/AgentSchedule.cs` — recurring schedule policy.
- Create: `src/shared/Clawbot.Domain/Agents/AgentScheduleRun.cs` — idempotent schedule firing record.
- Modify: `src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs` — DbSets.
- Create: `src/shared/Clawbot.Infrastructure/Persistence/Configurations/AgentOrchestrationV2Configurations.cs` — EF mapping.
- Create: `deploy/migrations/0031_agent_orchestration_v2_tables.sql` — new tables.
- Create: `deploy/migrations/0032_agent_orchestration_v2_indexes.sql` — indexes/unique keys.
- Create: `src/agents/Clawbot.Agents.Core/Orchestrator/A2AMailbox.cs` — send/claim/complete/fail messages.
- Create: `src/agents/Clawbot.Agents.Core/Orchestrator/AgentDefinitionCatalog.cs` — load/create sub-agent definitions.
- Create: `src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs` — bounded SK coordinator loop.
- Create: `src/agents/Clawbot.AgentService/Services/RecurrenceCalculator.cs` — cadence/window calculation.
- Create: `src/agents/Clawbot.AgentService/Services/AgentScheduleWorker.cs` — due schedule worker.
- Create: `src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs` — V2 API surface.
- Modify: `run-all.bat` — local encrypted LLM seed option.
- Create: `src/shared/Clawbot.Infrastructure/Agents/DemoLlmConfigSeeder.cs` — encrypt/upsert local provider config.
- Modify: `src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx` or create `src/frontend/clawbot-web/src/features/orchestration/` — minimal schedule/run panels.
- Tests under matching `tests/` projects.

---

### Task 1: Domain Entities and State Transitions

**Files:**
- Create: `src/shared/Clawbot.Domain/Agents/AgentDefinition.cs`
- Create: `src/shared/Clawbot.Domain/Agents/AgentA2AMessage.cs`
- Create: `src/shared/Clawbot.Domain/Agents/AgentSchedule.cs`
- Create: `src/shared/Clawbot.Domain/Agents/AgentScheduleRun.cs`
- Test: `tests/shared/Clawbot.Domain.Tests/Agents/AgentOrchestrationV2EntityTests.cs`

**Interfaces:**
- Produces: `AgentDefinition.Create(...)`, `AgentA2AMessage.Send(...)`, `AgentSchedule.Create(...)`, `AgentScheduleRun.Start(...)`.
- Consumes: existing `AggregateRoot<Guid>`, `ITenantOwned`.

- [ ] **Step 1: Write failing entity tests**

```csharp
using Clawbot.Domain.Agents;
using FluentAssertions;

namespace Clawbot.Domain.Tests.Agents;

public sealed class AgentOrchestrationV2EntityTests
{
    [Fact]
    public void ScheduleRun_SkipOverlap_MarksSkippedOverlap()
    {
        var run = AgentScheduleRun.Start(Guid.NewGuid(), Guid.NewGuid(), "2026-06-24:daily", DateTimeOffset.Parse("2026-06-24T00:00:00Z"));

        run.SkipOverlap(DateTimeOffset.Parse("2026-06-24T00:00:01Z"));

        run.Status.Should().Be("skipped_overlap");
        run.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void A2AMessage_Complete_MovesProcessingToCompleted()
    {
        var msg = AgentA2AMessage.Send(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), "task-1", "delegate", "{}", DateTimeOffset.Parse("2026-06-24T00:00:00Z"));
        msg.Claim(DateTimeOffset.Parse("2026-06-24T00:00:01Z"));

        msg.Complete("{\"ok\":true}", DateTimeOffset.Parse("2026-06-24T00:00:02Z"));

        msg.Status.Should().Be("completed");
        msg.PayloadJson.Should().Contain("ok");
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/shared/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --filter AgentOrchestrationV2EntityTests`

Expected: FAIL because entity types do not exist.

- [ ] **Step 3: Implement minimal entities**

```csharp
namespace Clawbot.Domain.Agents;

public sealed class AgentScheduleRun : AggregateRoot<Guid>, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ScheduleId { get; private set; }
    public Guid? SessionId { get; private set; }
    public string WindowKey { get; private set; } = string.Empty;
    public string Status { get; private set; } = "started";
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    private AgentScheduleRun() { }

    public static AgentScheduleRun Start(Guid tenantId, Guid scheduleId, string windowKey, DateTimeOffset startedAt) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        ScheduleId = scheduleId,
        WindowKey = windowKey,
        StartedAt = startedAt,
    };

    public void LinkSession(Guid sessionId) => SessionId = sessionId;
    public void Complete(DateTimeOffset at) { Status = "completed"; FinishedAt = at; }
    public void Fail(DateTimeOffset at) { Status = "failed"; FinishedAt = at; }
    public void SkipOverlap(DateTimeOffset at) { Status = "skipped_overlap"; FinishedAt = at; }
}
```

Implement matching `AgentDefinition`, `AgentA2AMessage`, and `AgentSchedule` with private setters, static factories, and explicit transition methods.

- [ ] **Step 4: Run tests and verify pass**

Run: `dotnet test tests/shared/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --filter AgentOrchestrationV2EntityTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/shared/Clawbot.Domain/Agents tests/shared/Clawbot.Domain.Tests/Agents/AgentOrchestrationV2EntityTests.cs
git commit -m "feat: add orchestration v2 domain entities"
```

---

### Task 2: EF Mapping and SQL Migrations

**Files:**
- Modify: `src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/shared/Clawbot.Infrastructure/Persistence/Configurations/AgentOrchestrationV2Configurations.cs`
- Create: `deploy/migrations/0031_agent_orchestration_v2_tables.sql`
- Create: `deploy/migrations/0032_agent_orchestration_v2_indexes.sql`
- Test: `tests/shared/Clawbot.Infrastructure.Tests/Persistence/AgentOrchestrationV2MappingTests.cs`

**Interfaces:**
- Consumes: entities from Task 1.
- Produces: DbSets and DB tables for later services.

- [ ] **Step 1: Write failing mapping test**

```csharp
[Fact]
public void Model_ContainsOrchestrationV2Tables()
{
    using var db = TestDbContextFactory.Create();

    db.Model.FindEntityType(typeof(AgentDefinition))!.GetTableName().Should().Be("agent_definitions");
    db.Model.FindEntityType(typeof(AgentA2AMessage))!.GetTableName().Should().Be("agent_a2a_messages");
    db.Model.FindEntityType(typeof(AgentSchedule))!.GetTableName().Should().Be("agent_schedules");
    db.Model.FindEntityType(typeof(AgentScheduleRun))!.GetTableName().Should().Be("agent_schedule_runs");
}
```

- [ ] **Step 2: Run test and verify failure**

Run: `dotnet test tests/shared/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter AgentOrchestrationV2MappingTests`

Expected: FAIL because mappings are absent.

- [ ] **Step 3: Add DbSets and mappings**

Add DbSets to `AppDbContext`:

```csharp
public DbSet<AgentDefinition> AgentDefinitions => Set<AgentDefinition>();
public DbSet<AgentA2AMessage> AgentA2AMessages => Set<AgentA2AMessage>();
public DbSet<AgentSchedule> AgentSchedules => Set<AgentSchedule>();
public DbSet<AgentScheduleRun> AgentScheduleRuns => Set<AgentScheduleRun>();
```

Add EF config with snake_case columns and tenant query filters matching existing patterns.

- [ ] **Step 4: Add migrations**

`0031_agent_orchestration_v2_tables.sql` creates the four tables with FKs.

`0032_agent_orchestration_v2_indexes.sql` adds:

```sql
CREATE UNIQUE INDEX UX_agent_schedule_runs_schedule_window ON agent_schedule_runs (schedule_id, window_key);
CREATE INDEX IX_agent_schedules_due ON agent_schedules (tenant_id, is_active, next_run_at);
CREATE INDEX IX_agent_a2a_messages_claim ON agent_a2a_messages (tenant_id, session_id, status, created_at);
```

- [ ] **Step 5: Run tests and migration syntax check**

Run: `dotnet test tests/shared/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter AgentOrchestrationV2MappingTests`

Run: `powershell -NoProfile -ExecutionPolicy Bypass -Command "if (Select-String -Path deploy/migrations/0031_agent_orchestration_v2_tables.sql,deploy/migrations/0032_agent_orchestration_v2_indexes.sql -Pattern '^\s*GO\s*$') { exit 1 }"`

Expected: PASS and no `GO` found.

- [ ] **Step 6: Commit**

```bash
git add src/shared/Clawbot.Infrastructure/Persistence deploy/migrations tests/shared/Clawbot.Infrastructure.Tests/Persistence/AgentOrchestrationV2MappingTests.cs
git commit -m "feat: add orchestration v2 persistence"
```

---

### Task 3: Encrypted Local LLM Demo Seed

**Files:**
- Create: `src/shared/Clawbot.Infrastructure/Agents/DemoLlmConfigSeeder.cs`
- Modify: `run-all.bat`
- Test: `tests/shared/Clawbot.Infrastructure.Tests/Agents/DemoLlmConfigSeederTests.cs`

**Interfaces:**
- Produces: `DemoLlmConfigSeeder.SeedAsync(Guid tenantId, string apiKey, CancellationToken ct)`.
- Consumes: existing LLM config encryption and `LlmConfig.Create(...)`.

- [ ] **Step 1: Write failing seed tests**

```csharp
[Fact]
public async Task SeedAsync_StoresEncryptedKey_NotPlaintext()
{
    var tenantId = Guid.NewGuid();
    using var db = TestDbContextFactory.Create();
    var seeder = new DemoLlmConfigSeeder(db, new FakeSecretEncryptor());

    await seeder.SeedAsync(tenantId, "sk-local-demo", CancellationToken.None);

    var config = await db.LlmConfigs.SingleAsync(x => x.TenantId == tenantId);
    config.Provider.Should().Be("openai-compatible");
    config.ModelId.Should().Be("cx/gpt-5.5");
    config.BaseUrl.Should().Be("http://localhost:20128/v1");
    config.ApiKeyEncrypted.Should().NotBe("sk-local-demo");
    config.ApiKeyEncrypted.Should().StartWith("encrypted:");
}
```

- [ ] **Step 2: Run test and verify failure**

Run: `dotnet test tests/shared/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter DemoLlmConfigSeederTests`

Expected: FAIL because seeder does not exist.

- [ ] **Step 3: Implement seeder**

```csharp
public sealed class DemoLlmConfigSeeder(AppDbContext db, ISecretEncryptor encryptor)
{
    public async Task SeedAsync(Guid tenantId, string apiKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("CLAWBOT_DEMO_LLM_API_KEY is required for demo LLM seed.");

        var encrypted = encryptor.Encrypt(apiKey);
        var now = DateTimeOffset.UtcNow;
        var existing = await db.LlmConfigs.FirstOrDefaultAsync(
            x => x.TenantId == tenantId && x.DisplayName == "Local OpenAI-compatible demo",
            ct);

        if (existing is null)
        {
            db.LlmConfigs.Add(LlmConfig.Create(
                tenantId,
                "openai-compatible",
                "cx/gpt-5.5",
                encrypted,
                now,
                "http://localhost:20128/v1",
                "Local OpenAI-compatible demo"));
        }
        else
        {
            existing.UpdateConnection("openai-compatible", "cx/gpt-5.5", "http://localhost:20128/v1", "Local OpenAI-compatible demo", now);
            existing.RotateApiKey(encrypted, now);
            existing.Activate(now);
        }

        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Add runner option without echoing key**

Add flags to `run-all.bat`: `--seed-demo-llm` and optional `--llm-key`. If `--llm-key` absent, read `%CLAWBOT_DEMO_LLM_API_KEY%`. Dry-run prints only `[DRY-RUN] Would seed local OpenAI-compatible LLM config (key hidden).`

- [ ] **Step 5: Run tests and dry-run**

Run: `dotnet test tests/shared/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter DemoLlmConfigSeederTests`

Run: `run-all.bat --dry-run --seed --seed-demo-llm`

Expected: PASS. Dry-run must not print `sk-` or key text.

- [ ] **Step 6: Commit**

```bash
git add src/shared/Clawbot.Infrastructure/Agents/DemoLlmConfigSeeder.cs tests/shared/Clawbot.Infrastructure.Tests/Agents/DemoLlmConfigSeederTests.cs run-all.bat
git commit -m "feat: seed local llm config for orchestration demo"
```

---

### Task 4: A2A Mailbox and Sub-Agent Catalog

**Files:**
- Create: `src/agents/Clawbot.Agents.Core/Orchestrator/A2AMailbox.cs`
- Create: `src/agents/Clawbot.Agents.Core/Orchestrator/AgentDefinitionCatalog.cs`
- Test: `tests/agents/Clawbot.Agents.Core.Tests/Orchestrator/A2AMailboxTests.cs`

**Interfaces:**
- Consumes: Task 1/2 entities and DbSets.
- Produces: `IA2AMailbox.SendAsync`, `ClaimNextAsync`, `CompleteAsync`, `FailAsync`; `IAgentDefinitionCatalog.ListAsync`, `EnsureAsync`.

- [ ] **Step 1: Write failing mailbox tests**

```csharp
[Fact]
public async Task ClaimNextAsync_ClaimsOnlyTenantSessionPendingMessage()
{
    using var db = TestDbContextFactory.Create();
    var mailbox = new A2AMailbox(db);
    var tenantId = Guid.NewGuid();
    var sessionId = Guid.NewGuid();
    var toAgentId = Guid.NewGuid();

    await mailbox.SendAsync(tenantId, sessionId, null, toAgentId, "task-1", "delegate", "{}", CancellationToken.None);

    var claimed = await mailbox.ClaimNextAsync(tenantId, sessionId, toAgentId, CancellationToken.None);

    claimed.Should().NotBeNull();
    claimed!.Status.Should().Be("processing");
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/agents/Clawbot.Agents.Core.Tests/Clawbot.Agents.Core.Tests.csproj --filter A2AMailboxTests`

Expected: FAIL.

- [ ] **Step 3: Implement mailbox/catalog**

Implement simple EF-backed services. Claim uses ordered pending query, transition to processing, then `SaveChangesAsync`.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/agents/Clawbot.Agents.Core.Tests/Clawbot.Agents.Core.Tests.csproj --filter A2AMailboxTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/agents/Clawbot.Agents.Core/Orchestrator tests/agents/Clawbot.Agents.Core.Tests/Orchestrator/A2AMailboxTests.cs
git commit -m "feat: add a2a mailbox for orchestration"
```

---

### Task 5: Recurrence Calculator and Schedule Worker

**Files:**
- Create: `src/agents/Clawbot.AgentService/Services/RecurrenceCalculator.cs`
- Create: `src/agents/Clawbot.AgentService/Services/AgentScheduleWorker.cs`
- Test: `tests/agents/Clawbot.AgentService.Tests/Services/RecurrenceCalculatorTests.cs`
- Test: `tests/agents/Clawbot.AgentService.Tests/Services/AgentScheduleWorkerTests.cs`

**Interfaces:**
- Produces: `RecurrenceCalculator.GetNextRun(...)`, `AgentScheduleWorker.ProcessDueAsync(...)`.
- Consumes: `AgentSchedule`, `AgentScheduleRun`, `AgentSession.CreatePlan(...)`.

- [ ] **Step 1: Write failing recurrence tests**

```csharp
[Theory]
[InlineData("daily", "2026-06-24T02:00:00Z", "2026-06-25")]
[InlineData("weekly", "2026-06-24T02:00:00Z", "2026-07-01")]
[InlineData("monthly", "2026-06-24T02:00:00Z", "2026-07-24")]
[InlineData("quarterly", "2026-06-24T02:00:00Z", "2026-09-24")]
public void GetNextRun_ComputesCadence(string cadence, string currentUtc, string expectedDatePrefix)
{
    var next = RecurrenceCalculator.GetNextRun(cadence, "Asia/Ho_Chi_Minh", DateTimeOffset.Parse(currentUtc));

    next.ToString("yyyy-MM-dd").Should().Be(expectedDatePrefix);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/agents/Clawbot.AgentService.Tests/Clawbot.AgentService.Tests.csproj --filter RecurrenceCalculatorTests`

Expected: FAIL.

- [ ] **Step 3: Implement calculator and worker**

Use `TimeZoneInfo.FindSystemTimeZoneById`. If Windows lacks IANA support, add a tiny mapping for `Asia/Ho_Chi_Minh` to `SE Asia Standard Time`.

Worker flow:

1. Query active schedules with `NextRunAt <= now`.
2. Compute `windowKey` from cadence and tenant-local date.
3. Insert `AgentScheduleRun`; if unique conflict, skip.
4. If overlap exists, mark `skipped_overlap`.
5. Create `AgentSession` and start V2 orchestrator.
6. Update `last_run_at` and `next_run_at`.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/agents/Clawbot.AgentService.Tests/Clawbot.AgentService.Tests.csproj --filter "RecurrenceCalculatorTests|AgentScheduleWorkerTests"`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/agents/Clawbot.AgentService/Services tests/agents/Clawbot.AgentService.Tests/Services
git commit -m "feat: schedule autonomous orchestration runs"
```

---

### Task 6: Autonomous Semantic Kernel Coordinator

**Files:**
- Create: `src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs`
- Test: `tests/agents/Clawbot.Agents.Core.Tests/Orchestrator/AutonomousOrchestratorTests.cs`

**Interfaces:**
- Consumes: `IA2AMailbox`, `IAgentDefinitionCatalog`, existing LLM scope/client, cost guard.
- Produces: `RunAsync(AutonomousRunRequest request, CancellationToken ct)`.

- [ ] **Step 1: Write failing bounded-loop test**

```csharp
[Fact]
public async Task RunAsync_StopsAtMaxRounds()
{
    var orchestrator = AutonomousOrchestratorTestHarness.CreateAlwaysContinue(maxRounds: 2);

    var result = await orchestrator.RunAsync(new AutonomousRunRequest(Guid.NewGuid(), "session-1", "goal", "manual"), CancellationToken.None);

    result.Status.Should().Be("failed");
    result.Reason.Should().Be("max_rounds");
    result.RoundCount.Should().Be(2);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/agents/Clawbot.Agents.Core.Tests/Clawbot.Agents.Core.Tests.csproj --filter AutonomousOrchestratorTests`

Expected: FAIL.

- [ ] **Step 3: Implement coordinator skeleton**

Coordinator loop:

```csharp
for (var round = 1; round <= options.MaxRounds; round++)
{
    ct.ThrowIfCancellationRequested();
    await costGuard.ThrowIfBlockedAsync(request.TenantId, ct);
    var decision = await planner.NextAsync(state, ct);
    await mailbox.SendAsync(..., ct);
    var result = await workers.ExecuteReadyAsync(..., ct);
    state = state.Apply(result);
    if (decision.ShouldStop) return AutonomousRunResult.Completed(round);
}
return AutonomousRunResult.Failed("max_rounds", options.MaxRounds);
```

- [ ] **Step 4: Add tests for cost/cancel/finalize**

Add tests asserting `cost_cap`, cancellation, and finalize path.

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/agents/Clawbot.Agents.Core.Tests/Clawbot.Agents.Core.Tests.csproj --filter AutonomousOrchestratorTests`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs tests/agents/Clawbot.Agents.Core.Tests/Orchestrator/AutonomousOrchestratorTests.cs
git commit -m "feat: add autonomous a2a orchestrator"
```

---

### Task 7: V2 API Endpoints

**Files:**
- Create: `src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs`
- Modify: API route registration file matching existing endpoint pattern.
- Test: `tests/api/Clawbot.Api.Tests/Endpoints/OrchestrationV2EndpointsTests.cs`

**Interfaces:**
- Consumes: schedule worker/orchestrator services.
- Produces: REST endpoints for runs, agents, schedules, control.

- [ ] **Step 1: Write failing auth test**

```csharp
[Fact]
public async Task CreateSchedule_ReturnsForbidden_WhenUserLacksManagePermission()
{
    using var app = ApiTestApp.CreateUserWithoutPermission("orchestration:manage");

    var response = await app.Client.PostAsJsonAsync("/api/orchestration/v2/schedules", new
    {
        name = "Daily lead triage",
        cadence = "daily",
        timezoneId = "Asia/Ho_Chi_Minh",
        goalTemplate = "Review hot leads"
    });

    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
}
```

- [ ] **Step 2: Run and verify failure**

Run: `dotnet test tests/api/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --filter OrchestrationV2EndpointsTests`

Expected: FAIL.

- [ ] **Step 3: Implement endpoints**

Routes:

- `POST /api/orchestration/v2/runs`
- `GET /api/orchestration/v2/runs/{id}`
- `POST /api/orchestration/v2/runs/{id}/control`
- `GET /api/orchestration/v2/agents`
- `POST /api/orchestration/v2/agents`
- `GET /api/orchestration/v2/schedules`
- `POST /api/orchestration/v2/schedules`
- `POST /api/orchestration/v2/schedules/{id}/run-now`

Validate goal/name/cadence/timezone. Enforce `orchestration:*` perms.

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/api/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --filter OrchestrationV2EndpointsTests`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs tests/api/Clawbot.Api.Tests/Endpoints/OrchestrationV2EndpointsTests.cs
git commit -m "feat: expose orchestration v2 endpoints"
```

---

### Task 8: Minimal Frontend Panel

**Files:**
- Create: `src/frontend/clawbot-web/src/features/orchestration/orchestrationApi.ts`
- Create: `src/frontend/clawbot-web/src/features/orchestration/OrchestrationV2Panel.tsx`
- Modify: `src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx`
- Test: existing frontend test location for feature components.

**Interfaces:**
- Consumes: Task 7 endpoints.
- Produces: schedule list, run-now, A2A trace display, pause/cancel controls.

- [ ] **Step 1: Write failing UI test**

```tsx
it('renders scheduled orchestration panel', () => {
  render(<OrchestrationV2Panel schedules={[{ id: 's1', name: 'Daily lead triage', cadence: 'daily', isActive: true }]} />)

  expect(screen.getByText('Daily lead triage')).toBeInTheDocument()
  expect(screen.getByRole('button', { name: /run now/i })).toBeInTheDocument()
})
```

- [ ] **Step 2: Run and verify failure**

Run: `npm test -- OrchestrationV2Panel` from `src/frontend/clawbot-web` if project test script exists, otherwise run project lint/typecheck after implementation.

Expected: FAIL before component exists.

- [ ] **Step 3: Implement panel**

Keep UI minimal: table/list, run-now button, status badge, trace timeline. No new UI library.

- [ ] **Step 4: Run frontend checks**

Run: `npm run lint` and `npm run build` from `src/frontend/clawbot-web`.

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/frontend/clawbot-web/src/features/orchestration src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx
git commit -m "feat: add orchestration v2 panel"
```

---

### Task 9: Demo Script and AI DevKit Docs Finalization

**Files:**
- Modify: `docs/demo-latest-flow.md`
- Modify: `docs/ai/requirements/2026-06-24-feature-dynamic-agent-orchestration-v2.md`
- Modify: `docs/ai/design/2026-06-24-feature-dynamic-agent-orchestration-v2.md`
- Modify: `docs/ai/planning/2026-06-24-feature-dynamic-agent-orchestration-v2.md`
- Modify: `docs/ai/testing/2026-06-24-feature-dynamic-agent-orchestration-v2.md`
- Modify: `docs/ai/deployment/2026-06-24-feature-dynamic-agent-orchestration-v2.md`
- Modify: `docs/ai/monitoring/2026-06-24-feature-dynamic-agent-orchestration-v2.md`

**Interfaces:**
- Consumes: implemented behavior from Tasks 1–8.
- Produces: demo-ready documentation.

- [ ] **Step 1: Verify demo doc claims match shipped behavior**

Search for overclaim words:

Run: `rg -n "production-ready|đã hoàn chỉnh|cho phép người dùng nhập mục tiêu" docs/demo-latest-flow.md docs/ai/*/2026-06-24-feature-dynamic-agent-orchestration-v2.md`

Expected: No misleading claims that V2 is live unless Tasks 1–8 shipped and smoke passed.

- [ ] **Step 2: Run AI DevKit memory store**

Run:

```bash
npx ai-devkit@latest memory store --title "Dynamic orchestration v2 direction" --content "Dynamic Agent Orchestration v2 means Semantic Kernel autonomous A2A coordination: input from chat/document/manual, sub-agents as data, daily/weekly/monthly/quarterly schedules, A2A mailbox, encrypted local OpenAI-compatible demo LLM seed, and guardrails for RBAC/cost/approval/cancel." --tags "orchestration,semantic-kernel,a2a,scheduler,llm-config"
```

Expected: memory stored successfully.

- [ ] **Step 3: Commit docs**

```bash
git add docs/demo-latest-flow.md docs/ai docs/superpowers/plans/2026-06-24-dynamic-agent-orchestration-v2.md
git commit -m "docs: define orchestration v2 implementation plan"
```

---

## Final Verification

Run these before PR/hand-off:

```bash
dotnet build Clawbot.sln --no-restore
dotnet test
rg -n "sk-[A-Za-z0-9_-]{12,}|api[_-]?key\s*[:=]\s*['\"][^'\"]+" docs src deploy tests run-all.bat
```

Expected:

- Build succeeds.
- Tests pass.
- Secret scan command returns no real/plaintext key hits.

## Execution Options

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.
