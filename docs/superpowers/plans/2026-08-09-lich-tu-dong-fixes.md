# Kế hoạch sửa lỗi "Lịch tự động" (/agents)

Ngày: 2026-08-09
Nhánh: thang/ai-autoreply-kb-improvements
Trạng thái: đã triển khai và build/review (2026-08-09); chưa áp migration/restart service nên còn cần kiểm thử runtime sau deploy.

Yêu cầu gốc (5 mục): 1.0 thêm nút Xóa lịch; 1.1 đến giờ "Kế tiếp" phải thật sự chạy và cập nhật
NextRunAt/LastRunAt; 1.2 "Chạy ngay" phải tạo session + gọi orchestrator + trả về mốc thời gian mới;
1.3 bỏ checkbox "Cần duyệt trước khi chạy"; 1.4 giữ overlap skip, và khi chạy lỗi vẫn cập nhật
LastRunAt + hiện lỗi trên UI.

---

## 1. Bằng chứng thực tế (không phải suy đoán)

### 1.1 Log agent service

`src/agents/Clawbot.AgentService/logs/agent-20260808.log`

```
2026-08-08 11:12:02.201 +07:00 [ERR] Failed to run due agent schedule "5cf8649d-f8f0-47d9-ad00-4138d73a0764"
System.NullReferenceException: Object reference not set to an instance of an object.
   at OrchestrationPlanValidator.Validate(...) in OrchestrationPlanValidator.cs:line 38
   at SemanticKernelPlanGenerator.GenerateAsync(...) in SemanticKernelPlanGenerator.cs:line 87
   at AutonomousOrchestrator.ExecutePlanAsync(...) in AutonomousOrchestrator.cs:line 194
   at AutonomousOrchestrator.RunAsync(...) in AutonomousOrchestrator.cs:line 106
   at AgentScheduleRunner.RunDueAsync(...) in AgentScheduleRunner.cs:line 78
   at AgentScheduleWorker.ProcessDueAsync(...) in AgentScheduleWorker.cs:line 69
```

Cùng file, ~11:10 còn một `JsonException` từ `SemanticKernelPlanGenerator.cs:67` (deserialize plan).

### 1.2 Trạng thái DB hiện tại (clawbot, 127.0.0.1,11433)

```
agent_schedule_runs   completed=53  failed=16  skipped_overlap=42  started=5
   started  : cũ nhất 2026-07-16 03:26, mới nhất 2026-08-08 04:10  (mồ côi, finished_at NULL)
   skipped  : mới nhất 2026-08-09 03:26
agent_sessions        completed=196 failed=381 running=5            (zombie)
```

Chi tiết theo lịch:

| Lịch | started (mồ côi) | skipped_overlap | skip gần nhất |
|---|---|---|---|
| Giám sát hội thoại mở (daily) | 1 (07-16) | 24 | 08-09 03:26 |
| Phát hiện hội thoại chờ phản hồi quá hạn (daily) | 1 (07-31) | 13 | 08-08 16:20 |
| Duyệt nội dung nháp (weekly) | 1 (07-20) | 3 | 08-08 03:26 |
| Chăm sóc lead lạnh (weekly) | 1 (07-25) | 2 | 08-08 03:26 |
| Đánh giá chất lượng hội thoại (weekly) | 1 (08-08) | 0 | - |

Đọc bảng này ra: **5 lịch đã chết hẳn**. "Giám sát hội thoại mở" bị khóa 24 ngày liên tiếp chỉ vì
một lần chạy hỏng ngày 16/07. Đây chính là hiện tượng người dùng báo ở mục 1.1 — đến giờ mà không
có phiên nào hoạt động.

---

## 2. Chuỗi nguyên nhân (6 khiếm khuyết)

**D1 — Plan validator ném NullReferenceException khi LLM bỏ field.**
`OrchestrationPlanDocument.cs:129-139` khai báo `OrchestrationPlanTask` với `Input` và `DependsOn`
là tham số positional non-nullable, không default. System.Text.Json khi thiếu property trong JSON
sẽ gán `null` (không có `required`). Sau đó:
- `OrchestrationPlanValidator.cs:29` — `InputSize(task.Input)` → NRE
- `OrchestrationPlanValidator.cs:38` — `foreach (var dependency in task.DependsOn)` → NRE (đúng dòng trong log)
- `OrchestrationPlanValidator.cs:73` — `byId[id].DependsOn` trong `HasCycle` → NRE

Tức là: model trả JSON thiếu `dependsOn` (chuyện rất thường) thì thay vì được coi là "kế hoạch sai
cấu trúc" và cho retry, cả lượt chạy nổ.

**D2 — Nhánh replan không có try/catch.**
`AutonomousOrchestrator.cs:190-196` gọi `_planner.ReplanAsync` trần. Lượt plan đầu được bọc
(`AutonomousOrchestrator.cs:77-89`) nên lỗi biến thành `plan_failed` tử tế, nhưng replan thì exception
thoát khỏi `ExecutePlanAsync` → `RunAsync` → ra ngoài. Session cũng nằm lại `running` (5 zombie trong DB).

**D3 — `RunDueAsync` không đóng run row khi exception không phải cancel.**
`AgentScheduleRunner.cs:76-92` chỉ bắt `OperationCanceledException`. Exception thường bay ra sau khi
run row đã được commit ở trạng thái `started` (`SaveOrGetDuplicateAsync`, dòng 72). Row đó
`finished_at = NULL` vĩnh viễn.

**D4 — Overlap check tin tuyệt đối vào status `started`, không có reaper.**
`AgentScheduleRunner.cs:200-203` (`HasStartedRunAsync`) làm mọi window sau đó thành `skipped_overlap`.
Một run mồ côi = khóa lịch mãi mãi. Không có cơ chế nào dọn run treo.

**D5 — Nhánh "window đã chạy" return sớm mà không đẩy NextRunAt.**
`AgentScheduleRunner.cs:35-39`: nếu đã có run cho window đó thì `return existing` — `NextRunAt` giữ
nguyên ở mốc quá khứ. Hệ quả kép:
- UI hiện "Kế tiếp" là một mốc đã qua và đứng im (đúng như ảnh 1.1).
- Lịch đó due ở **mọi** tick. `AgentScheduleWorker.cs:51-56` lấy `OrderBy(NextRunAt).Take(10)`, nên
  các row đóng băng (NextRunAt xa nhất trong quá khứ) chiếm hết batch và **bỏ đói** những lịch khác.

**D6 — "Chạy ngay" không chạy gì cả.**
`OrchestrationV2Endpoints.cs:722-733` chỉ `UpdateSchedule(..., nextRunAt: now, ...)`: không đụng
LastRunAt, không tạo session, không gọi orchestrator. Và vì kéo NextRunAt về đúng window đã chạy
trong ngày/tuần, nó rơi thẳng vào D5 → lịch đóng băng luôn sau khi bấm.
`AgentScheduleRunner.RunNowAsync`/`RunManualAsync` (dòng 97-143) đã viết sẵn từ SPEC-16 nhưng **là code
chết** — không caller nào; `docs/ai/testing/2026-06-24-feature-dynamic-agent-orchestration-v2.md:42`
ghi rõ "API exposure deferred to Phase 5".

### Bảng đối chiếu yêu cầu

| Yêu cầu | Khiếm khuyết liên quan | Hạng mục sửa |
|---|---|---|
| 1.0 Nút Xóa | (chưa có endpoint) | P3 |
| 1.1 Đến giờ phải chạy | D1, D2, D3, D4, D5 | P0, P1, P5 |
| 1.2 Chạy ngay | D6 (+ D5) | P2 |
| 1.3 Bỏ "Cần duyệt" | - | P2 (FE) |
| 1.4 Giữ overlap + hiện lỗi | D3, D4 | P1, P4 |

---

## 3. Quyết định đã chốt

1. **Xóa lịch = soft delete** qua `AgentSchedule.Archive()` (set `DeletedAt` + `IsActive=false`).
   Giữ lịch sử `agent_schedule_runs`; `ListSchedulesAsync` đã lọc `DeletedAt == null`.
2. **"Chạy ngay" đi qua gRPC agent service**, không tự chạy trong API. Lý do: `AutonomousOrchestrator`,
   ToolRegistry, approval resolver chỉ có ở AgentService; và vòng đời run row phải nằm cùng một chỗ
   (`AgentScheduleRunner`) để không sinh thêm run mồ côi.
3. **Overlap: giữ skip** (theo yêu cầu 1.4). Bấm "Chạy ngay" khi phiên trước chưa xong → HTTP 409
   với thông báo rõ, **không** đổi LastRunAt/NextRunAt (vì thực tế không chạy gì).
4. **Chạy lỗi (failed/cancelled) vẫn cập nhật LastRunAt** — LastRunAt được set ngay lúc bắt đầu chạy,
   không phụ thuộc kết quả.
5. **Hiện lỗi trên UI**: DTO lịch thêm `lastRunStatus` + `lastRunError`; dòng lịch hiện pill đỏ
   "Lần cuối: lỗi" kèm tooltip lý do (đã xác nhận với người yêu cầu).
6. **`pending_approval` không phải lỗi**: lịch đã lập được kế hoạch và chờ người duyệt → run row
   `completed`. (Hiện tại bị đánh `failed`.)
7. **Lịch `event` khi chạy tay**: NextRunAt trả về `DateTimeOffset.MaxValue` (ngủ chờ sự kiện),
   không cộng theo cadence.
8. **Lịch đang tạm dừng vẫn cho "Chạy ngay"** (giữ hành vi hiện tại của UI) và vẫn cập nhật hai mốc
   thời gian — vô hại vì worker chỉ lấy lịch `IsActive`.
9. **Không cần migration cho 1.3.** Đã kiểm tra DB: cả 18 lịch đang sống đều có
   `requires_approval = 0`, nên bỏ checkbox không để lại lịch nào bị kẹt ở `pending_approval` mà UI
   không còn cách tắt. Vẫn giữ pill "Cần duyệt" ở dòng lịch để nếu sau này có row = 1 thì còn nhìn thấy.
   Xác nhận thêm: `request.RequiresApproval` (từ `schedule.RequiresApproval`) là thứ duy nhất đẩy
   session sang `pending_approval` (`AutonomousOrchestrator.cs:94,103` → `AutonomousRunSink.cs:64-66`);
   cờ tenant `RequireOrchestrationApproval` chỉ chặn tool High-risk (`AutonomousOrchestrator.cs:129-131`),
   không liên quan. Nên bỏ checkbox đúng là làm lịch tự chạy.

---

## 4. Kế hoạch thực thi

### P0 — Chặn nguồn crash (Clawbot.Agents.Core)

**P0-1. `Orchestrator/OrchestrationPlanDocument.cs`** — thêm chuẩn hóa null.

```csharp
public sealed record OrchestrationPlanDocument(...)
{
    // LLM hay bỏ hẳn field dependsOn/input; System.Text.Json để null vào record positional
    // non-nullable, và mọi consumer sau đó (validator, wave scheduler) nổ NRE.
    public OrchestrationPlanDocument Normalize() =>
        this with { Tasks = (Tasks ?? Array.Empty<OrchestrationPlanTask>()).Select(t => t.Normalize()).ToArray() };
}
```

Thêm `OrchestrationPlanTask.Normalize()`: `Id/Agent/Description/Status ?? string.Empty`,
`Input ?? empty dictionary`, `DependsOn ?? Array.Empty<string>()`.

**P0-2. `Orchestrator/SemanticKernelPlanGenerator.cs:67`**

```csharp
plan = JsonSerializer.Deserialize<OrchestrationPlanDocument>(NormalizeJson(json), JsonOptions)?.Normalize();
```

Cả `PlanAsync` và `ReplanAsync` đều đi qua `GenerateAsync` (`AutonomousPlanner.cs:18,27`) nên một
điểm sửa là đủ cho cả hai đường.

**P0-3. `Orchestrator/OrchestrationPlanJson.cs`** — `TryParse` cũng `.Normalize()`: plan cũ đã nằm
trong cột `agent_sessions.plan_json` có thể thiếu field.

**P0-4. `Orchestrator/OrchestrationPlanValidator.cs`** — phòng vệ tại chỗ (dòng 29, 38, 73): dùng
`task.DependsOn ?? Array.Empty<string>()`, `task.Input` null-safe. Validator là biên kiểm tra dữ liệu
LLM, nó phải trả `Invalid`, tuyệt đối không được ném.

**P0-5. `Orchestrator/AutonomousOrchestrator.cs:190-196`** — bọc replan:

```csharp
try
{
    using (_llmScope.Begin(request.TenantId, OrchestratorAgentCode))
        plan = await _planner.ReplanAsync(...).ConfigureAwait(false);
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    await _sink.TraceAsync(..., "replan_failed", ex.Message, ...);
    await _sink.FailAsync(request.TenantId, request.SessionId, "replan_failed", _clock.UtcNow, ct);
    return AutonomousRunResult.Failed("replan_failed", replans);
}
```

### P1 — Lịch không bao giờ tự khóa (Clawbot.AgentService)

**P1-1. `Services/AgentScheduleRunner.cs`** — bọc phần thực thi trong `RunDueAsync` (và đường chạy
tay mới) bằng `catch (Exception ex)`: `run.Fail(ex.Message, now)` + `SaveChangesAsync(None)` rồi
`throw` lại (giữ nguyên stack cho `AgentScheduleWorker.LogScheduleRunFailed`). Bắt buộc: run row
luôn kết thúc ở trạng thái terminal.

**P1-2. Map kết quả orchestrator** (`AgentScheduleRunner.cs:81-85`):

```csharp
if (result.Status is "completed" or "pending_approval") run.Complete(_clock.UtcNow);
else run.Fail(result.Reason ?? result.Status, _clock.UtcNow);
```

**P1-3. Nhánh "window đã chạy" (`AgentScheduleRunner.cs:35-39`)** — trước khi `return existing`,
nếu `schedule.NextRunAt <= dueAtUtc` thì đẩy mốc kế tiếp (không đụng LastRunAt vì không chạy gì):

```csharp
if (existing is not null)
{
    if (schedule.NextRunAt <= dueAtUtc)
    {
        schedule.Reschedule(NextRunFor(schedule), _clock.UtcNow);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
    return existing;
}
```

Cần method mới `AgentSchedule.Reschedule(DateTimeOffset nextRunAt, DateTimeOffset updatedAt)` trong
`src/shared/Clawbot.Domain/Agents/AgentSchedule.cs` (hiện chỉ có `RecordRun` ép ghi cả LastRunAt).
Helper `NextRunFor(schedule)` = `MaxValue` nếu `TriggerType == "event"`, ngược lại
`RecurrenceCalculator.NextRunUtc(cadence, dueAt, tz)` — dùng chung cho cả 3 nhánh.

**P1-4. Reaper run treo** — trong `Services/AgentScheduleWorker.cs`, đầu mỗi tick, trước khi lấy batch:

```csharp
private static readonly TimeSpan StaleRunAfter = TimeSpan.FromHours(2);
```

Một `ExecuteUpdate` trên `AgentScheduleRuns.IgnoreQueryFilters()` với
`Status == "started" && FinishedAt == null && StartedAt < now - StaleRunAfter` → `failed`,
`error = "stale_run_reaped"`, `finished_at = now`. Đây là điều kiện để giữ được overlap skip (yêu cầu
1.4) mà không biến nó thành khóa vĩnh viễn.

Kèm theo: đóng luôn `AgentSessions` zombie **chỉ của các run vừa reap** (join qua `SessionId`), điều
kiện `Status == "running" && StartedAt < now - 6h`. Giới hạn phạm vi như vậy để không bao giờ đụng
phiên do người dùng bấm chạy.

### P2 — "Chạy ngay" thật sự chạy

**P2-1. `proto/orchestrator.proto`** — thêm rpc (additive, không đổi field number cũ):

```proto
rpc RunSchedule (RunScheduleRequest) returns (RunScheduleResponse);

message RunScheduleRequest {
  string tenant_id = 1;
  string schedule_id = 2;
  string user_id = 3;
}

message RunScheduleResponse {
  string status = 1;              // started | skipped_overlap | not_found
  string session_id = 2;
  google.protobuf.Timestamp next_run_at = 3;
  google.protobuf.Timestamp last_run_at = 4;
}
```

**P2-2. `Services/AgentScheduleRunner.cs`** — viết lại `RunNowAsync` (thay code chết hiện tại):

1. Load schedule theo `scheduleId` + `TenantId` (`IgnoreQueryFilters`, `DeletedAt == null`).
   Không có → trả `not_found`.
2. `HasStartedRunAsync(schedule.Id)` → true thì trả `skipped_overlap` ngay, **không** tạo run row,
   **không** đổi timestamp.
3. `windowKey = $"manual:{now.UtcTicks}"` (luôn duy nhất → không đụng dedup theo cadence window).
4. Tạo `AgentScheduleRun.Start(...)`; nếu không phải trend-scan thì tạo
   `AgentSession.CreatePlan(tenantId, goal, "{}", schedule.RequiresApproval, now, userId)` +
   `run.LinkSession(session.Id)`.
5. `schedule.RecordRun(lastRunAt: now, nextRunAt: NextRunFor(schedule), updatedAt: now)`.
6. `SaveChangesAsync` → **trả về ngay** (status `started`, sessionId, nextRunAt, lastRunAt).
7. Chạy orchestrator ở **background**: `Task.Run` + `IServiceScopeFactory.CreateScope()` → lấy
   `AgentScheduleRunner` mới trong scope đó và gọi một method `ContinueRunAsync(runId, sessionId)`
   (load lại entity trong DbContext của scope mới — không dùng chung DbContext như
   `agent-schedule-batch-shared-dbcontext` đã dặn). Kết thúc: `run.Complete/Fail` + SaveChanges,
   bọc try/catch như P1-1.
   Cần thêm `IServiceScopeFactory` vào ctor `AgentScheduleRunner`.
   Trend-scan (`IsTrendScan`) đi nhánh `ExecuteTrendScanAsync` trong cùng background task.

**P2-3. `Services/OrchestratorGrpcService.cs`** — override `RunSchedule`: parse tenant/schedule/user,
gọi `AgentScheduleRunner` (inject trực tiếp, cả hai đều scoped, không tạo vòng DI), map sang
`RunScheduleResponse`.

**P2-4. `src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs`** — `RunScheduleNowAsync` viết lại:
validate schedule thuộc tenant → `grpc.RunScheduleAsync(...)` → map:

| gRPC status | HTTP |
|---|---|
| `started` | 202 Accepted `{ status, sessionId, nextRunAt, lastRunAt }` |
| `skipped_overlap` | 409 Conflict `{ error = "schedule_run_in_progress", message = "Lịch đang có phiên chạy chưa xong — chờ phiên đó kết thúc rồi thử lại." }` |
| `not_found` | 404 |

`RpcException` → `ToGrpcResult(ex)` như các endpoint khác.

**P2-5. FE `src/shared/api/orchestrationV2.ts`**
- `OrchestrationV2RunNowResponse` thêm `sessionId: string`, `lastRunAt: string`.
- `OrchestrationV2Schedule` thêm `lastRunStatus?: string | null`, `lastRunError?: string | null`.
- Thêm `deleteOrchestrationV2Schedule(id)` → `apiClient.delete(...)`.

**P2-6. FE `src/features/agents/SchedulesCard.tsx`**
- `runNowMutation.onSuccess`: invalidate **cả** `["orchestration","schedules"]` và
  `["orchestration","runs"]` (key dùng chung với `OrchestrationPanel.tsx:115` và
  `AgentDashboardPage.tsx:657`) để phiên mới hiện ngay ở "Phiên gần đây" / metric "Agent đang hoạt động".
- Lỗi 409 hiện qua `Alert` sẵn có (biến `error` đã gom mọi mutation).
- **Bỏ checkbox "Cần duyệt trước khi chạy"** (dòng 262-265), bỏ state `requiresApproval`, luôn gửi
  `requiresApproval: false`. Sửa câu chú thích nhánh event (dòng 270) vì nó đang khuyên giữ "Cần duyệt".
  Giữ pill "Cần duyệt" ở dòng lịch (dòng 129) để lịch cũ/seed có `requires_approval = 1` vẫn được giải thích.
- Thêm nút **"Xóa"** + `ConfirmDialog` (`shared/ui/ConfirmDialog.tsx`, `danger` mặc định true),
  gate bằng `canManage`.
- Hiện lỗi lần chạy cuối: cạnh "Lần cuối", nếu `lastRunStatus` là `failed`/`cancelled` thì thêm
  `<StatusPill tone="error">` với `title={lastRunError}`.

### P3 — Endpoint xóa lịch

`OrchestrationV2Endpoints.cs`:

```csharp
group.MapDelete("/schedules/{id:guid}", DeleteScheduleAsync).RequirePermission("orchestration:manage");
```

Handler: load theo tenant + `DeletedAt == null` → `schedule.Archive(clock.UtcNow)` → `SaveChanges` →
`Results.Ok(new { id })`. Không cần seed permission mới (`orchestration:manage` đã dùng cho
pause/activate).

### P4 — DTO trạng thái lần chạy cuối

`ListSchedulesAsync` (`OrchestrationV2Endpoints.cs:461-470`): thêm một query lấy run mới nhất mỗi
schedule của tenant (group theo `ScheduleId`, `OrderByDescending(StartedAt)`), rồi map vào
`OrchestrationV2ScheduleDto` hai field mới `LastRunStatus`, `LastRunError`. Một round-trip phụ, không
N+1.

### P5 — Migration sửa dữ liệu đang hỏng

`deploy/migrations/0096_reap_stale_agent_schedule_runs.sql` (một file = một `SqlCommand`, **không có
`GO`** — theo `clawbot-migration-no-go`):

1. `UPDATE agent_schedule_runs SET status='failed', error='stale_run_reaped (0096)',
   finished_at=SYSUTCDATETIME() WHERE status='started' AND finished_at IS NULL AND
   started_at < DATEADD(hour,-2,SYSUTCDATETIME());`
2. Đóng session zombie gắn với các run đó: `UPDATE agent_sessions SET status='failed',
   finished_at=SYSUTCDATETIME() WHERE status='running' AND started_at < DATEADD(hour,-6,SYSUTCDATETIME())
   AND id IN (SELECT session_id FROM agent_schedule_runs WHERE session_id IS NOT NULL AND
   error='stale_run_reaped (0096)');`

Kết quả mong đợi: 5 run mồ côi + 5 session zombie được đóng → 5 lịch chết sống lại ở tick kế tiếp.
`run-all.bat` đã tự áp migration pending qua ledger `schema_migrations`, chỉ cần thêm file.

---

## 5. Danh sách file sẽ sửa

| File | Việc |
|---|---|
| `src/agents/Clawbot.Agents.Core/Orchestrator/OrchestrationPlanDocument.cs` | thêm `Normalize()` cho document + task |
| `src/agents/Clawbot.Agents.Core/Orchestrator/SemanticKernelPlanGenerator.cs` | `.Normalize()` sau deserialize |
| `src/agents/Clawbot.Agents.Core/Orchestrator/OrchestrationPlanJson.cs` | `.Normalize()` trong `TryParse` |
| `src/agents/Clawbot.Agents.Core/Orchestrator/OrchestrationPlanValidator.cs` | null-safe, không ném |
| `src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs` | bọc replan → `replan_failed` |
| `src/shared/Clawbot.Domain/Agents/AgentSchedule.cs` | thêm `Reschedule(...)` |
| `src/agents/Clawbot.AgentService/Services/AgentScheduleRunner.cs` | try/catch, map kết quả, đẩy NextRunAt, viết lại `RunNowAsync` + background |
| `src/agents/Clawbot.AgentService/Services/AgentScheduleWorker.cs` | reaper run/session treo |
| `src/agents/Clawbot.AgentService/Services/OrchestratorGrpcService.cs` | override `RunSchedule` |
| `proto/orchestrator.proto` | rpc + 2 message mới |
| `src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs` | run-now qua gRPC, DELETE, DTO 2 field |
| `src/frontend/clawbot-web/src/shared/api/orchestrationV2.ts` | type + `deleteOrchestrationV2Schedule` |
| `src/frontend/clawbot-web/src/features/agents/SchedulesCard.tsx` | nút Xóa, bỏ checkbox duyệt, hiện lỗi, invalidate runs |
| `deploy/migrations/0096_reap_stale_agent_schedule_runs.sql` | sửa dữ liệu |

Thứ tự làm: P0 → P1 → P5 (dữ liệu) → P2 → P3 → P4 → FE. P0/P1/P5 đã đủ để yêu cầu 1.1 hết lỗi;
P2 cần deploy API + AgentService cùng lúc (proto đổi).

---

## 6. Kiểm chứng

Tự động:
- `dotnet build Clawbot.sln` (gate NuGetAudit + analyzer theo `clawbot-build-gates`).
- `npm run lint` + `tsc --noEmit` trong `src/frontend/clawbot-web`.
- Thêm unit test vào `tests/Clawbot.Agents.Tests`:
  1. `Validate` trả `Invalid` (không ném) khi task thiếu `dependsOn`/`input`.
  2. `GenerateAsync` chuẩn hóa được JSON thiếu `dependsOn`.
  3. `RunDueAsync` đóng run row thành `failed` khi orchestrator ném exception.

Thủ công (sau `run-all.bat`):
1. `sqlcmd` xác nhận `agent_schedule_runs` không còn `started` treo.
2. Đợi tick: lịch "Giám sát hội thoại mở" tạo được run mới (`completed`/`failed`, không `skipped_overlap`).
3. Tạo lịch daily mới → không còn checkbox "Cần duyệt"; bấm "Chạy ngay" → phản hồi 202 có `sessionId`;
   dòng lịch cập nhật **ngay** "Kế tiếp" (+1 ngày) và "Lần cuối" (= giờ hiện tại); `/agents` hiện phiên
   `running` ở "Phiên gần đây" và ở `/agents/runs`.
4. Bấm "Chạy ngay" lần 2 khi phiên đầu chưa xong → 409, Alert hiện thông báo, hai mốc thời gian **không** đổi.
5. Ép lỗi (unbind LLM của orchestrator) → run `failed`, "Lần cuối" vẫn cập nhật, dòng lịch hiện pill lỗi + tooltip.
6. Bấm "Xóa" → ConfirmDialog → lịch mất khỏi danh sách; reload vẫn mất; `deleted_at` có giá trị trong DB.

---

## 7. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Đổi `proto` → API và AgentService phải deploy cùng nhau | rpc là additive; deploy đồng thời qua `run-all.bat`/compose |
| Process chết giữa background run → lại sinh run `started` mồ côi | reaper P1-4 xử lý trong 2h, không còn khóa vĩnh viễn |
| Reap session sai (đóng phiên đang chạy thật) | chỉ reap session gắn với run vừa reap + ngưỡng 6h |
| `Task.Run` background dùng lại DbContext của request | bắt buộc `CreateScope()` riêng (bài học `agent-schedule-batch-shared-dbcontext`) |
| Lịch `event` bị cộng cadence khi chạy tay | `NextRunFor()` trả `MaxValue` cho `TriggerType == "event"` |
| Bỏ checkbox duyệt làm lịch tự chạy tác vụ rủi ro cao | cổng riêng `Tenant.RequireOrchestrationApproval` (tool High-risk) độc lập, không bị ảnh hưởng |
| Lịch "Tối ưu chiến dịch quảng cáo" còn trong DB dù module Ads đã gỡ 09/08 | sau P4 nó sẽ hiện lỗi rõ trên dòng lịch → người dùng tự bấm Xóa (nút mới của P3) |
