# Kế hoạch nâng cấp Orchestrator: can thiệp vào output của task thay vì replan lại từ đầu

Ngày: 2026-08-09
Nhánh: thang/ai-autoreply-kb-improvements
Trạng thái: kế hoạch hoàn chỉnh (chưa code). Mọi điểm mở đã chốt — xem §9.

Yêu cầu gốc của khách:

> Khi bấm vào từng task đã hoàn thành, có thể tạm dừng luồng đang chạy để kiểm soát, chỉnh sửa output
> của task đó, để có thể đẩy output mới (đã chỉnh sửa) cho task tiếp theo tiếp tục hoạt động.
>
> => Mục đích giảm chi phí vận hành Orchestrator, vì hiện tại khi Plan fail do 1 task nào đó fail thì
> sẽ gọi hàm ReLoop để Orchestrator gen ra Plan mới và chạy lại plan mới, task mới đó từ đầu
> => dẫn đến tốn chi phí vận hành.

---

## 1. Bằng chứng thực tế (không phải suy đoán)

### 1.1 Chẩn đoán của khách là ĐÚNG — replan sinh plan mới hoàn toàn, vứt sạch việc đã làm

`AutonomousPlanner.ReplanAsync` không "sửa" plan cũ. Nó dựng một goal mới rồi gọi thẳng
`generator.GenerateAsync` — tức là sinh một `OrchestrationPlanDocument` mới tinh, id task mới, mọi
task đều `status = pending`:

```csharp
// src/agents/Clawbot.AgentService/Services/AutonomousPlanner.cs
public Task<OrchestrationPlanDocument> ReplanAsync(Guid tenantId, string goal, ..., IReadOnlyList<OrchestrationPlanTask> failed, ...)
{
    var replanGoal = BuildReplanGoal(goal, failed);
    using (llmScope.Begin(tenantId, OrchestratorAgentCode))
        return generator.GenerateAsync(replanGoal, catalog, ct);
}

private static string BuildReplanGoal(string goal, IReadOnlyList<OrchestrationPlanTask> failed)
{
    var failedSummary = string.Join("; ", failed.Select(f => $"{f.Agent}:{f.Error ?? "failed"}"));
    return $"Original goal: {goal}. Previous tasks failed ({failedSummary}). Produce a revised plan that avoids the failed approach.";
}
```

Chú ý: `BuildReplanGoal` **chỉ truyền các task fail** vào prompt. Các task đã `completed` cùng output
của chúng không hề được truyền lên LLM, nên LLM không có cách nào giữ lại. Sau replan,
`PersistPlanAsync` ghi đè `PlanJson` — output cũ biến mất khỏi DB luôn.

### 1.2 Vòng lặp tự động replan — không có cửa cho con người can thiệp

`AutonomousOrchestrator.ExecutePlanAsync` (`src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs`):

| Dòng | Hành vi |
|---|---|
| `:147` | `hasFailed = plan.Tasks.Any(t => IsFailed(t.Status))` |
| `:172` | wave khỏe mạnh → `continue`, không tiêu round |
| `:176` | có task fail → lấy danh sách failed |
| `:186` | `if (replans >= _options.MaxRounds)` → fail hẳn với reason `max_rounds` |
| `:190-205` | ngược lại: trace "re-planned", gọi `ReplanAsync`, `replans++`, quay lại đầu vòng |

Không có nhánh nào dừng lại hỏi người dùng. Task fail → replan, tự động, ngay lập tức.

### 1.3 Chi phí một lần fail (tính theo cấu hình mặc định đang chạy)

`AutonomousOrchestratorOptions` (`AutonomousRunContracts.cs:20-30`): `MaxRounds = 3`,
`PerTaskEstimateUsd = 0.01m`.

Với plan 3 bước như ảnh khách gửi (research → content → publishing), nếu bước 3 fail:

```
Lần chạy đầu     : 1 LLM planner + 3 LLM worker (mỗi worker còn kèm vòng ReAct + tool call)
Replan lần 1     : 1 LLM planner + N LLM worker  (chạy LẠI cả research và content dù đã xong)
Replan lần 2     : 1 LLM planner + N LLM worker
Replan lần 3     : 1 LLM planner + N LLM worker
--------------------------------------------------
Tổng             : ~4x chi phí của một lần chạy sạch, và vẫn có thể kết thúc bằng "max_rounds"
```

Đây chính xác là cái khách mô tả. Tệ hơn: mỗi lần replan chạy lại `research-agent` là chạy lại
tool search/crawl thật, không chỉ tốn token mà còn tốn quota nhà cung cấp.

### 1.4 UI đã có mầm mống tính năng nhưng dùng không được trong thực tế

`OrchestrationPanel.tsx:494-513` đã có nút **"Sửa kết quả task"**:

```tsx
{session.status === "paused" && (selectedTask.status === "completed" || selectedTask.status === "failed") ? (
  <button disabled={updatePlan.isPending} onClick={() => {
      const editedText = window.prompt("Chỉnh sửa kết quả để chuyển cho bước tiếp theo:", text);
      ...
      const nextPlan = replaceTaskOutput(planDraft, selectedTask.id, output);
      updatePlan.mutate({ sessionId: session.sessionId, planJson: nextPlan, etag: session.etag });
  }}>Sửa kết quả task</button>
) : null}
```

Bốn lý do nó không cứu được chi phí:

1. **Chỉ hiện khi `status === "paused"`.** Mà luồng fail không bao giờ dừng ở `paused` — nó tự replan
   (§1.2) rồi kết thúc ở `failed`. Người dùng không kịp bấm pause trong lúc replan đang chạy.
2. **`window.prompt` một dòng.** Output của agent thường vài nghìn ký tự kèm khối `[tool_results]`
   JSON ở cuối (`splitToolResults`, `userText.ts:170`). Sửa trong một ô prompt là bất khả thi.
3. **Sửa xong không có gì đẩy tiếp.** `replaceTaskOutput` (`OrchestrationPanel.tsx:60`) chỉ set
   `status: "completed"` cho task đó. Nếu task phía sau đã `completed` rồi thì nó không chạy lại —
   output mới không đi đâu cả. Người dùng sẽ thấy "sửa mà không có tác dụng".
4. **FE tự vá JSON của plan rồi PUT nguyên cục.** Không audit, dễ hỏng `input`/`dependsOn`, và phải
   qua `OrchestrationPlanValidator` vốn thiết kế cho việc sửa plan lúc draft.

### 1.5 Ba cái bẫy kỹ thuật phải xử lý cùng lúc, nếu không tính năng sẽ "chạy lúc được lúc không"

**(a) Runner đang chạy giữ bản `plan` trong RAM — sửa DB sẽ bị ghi đè.**
Trong `ExecutePlanAsync`, biến `plan` là local. Mỗi task xong lại `PersistPlanAsync` ghi đè cả
document. Nếu người dùng sửa DB trong lúc runner còn sống, lần ghi kế tiếp xóa sạch bản sửa.
`Control` đã có sẵn đúng lá chắn này cho `resume`:

```csharp
// OrchestratorGrpcService.cs:257-259
case "resume":
    if (RunningSessions.ContainsKey(session.Id))
        throw new RpcException(new Status(StatusCode.FailedPrecondition, "pause_in_progress"));
```

API can thiệp mới **bắt buộc** dùng lại lá chắn này.

**(b) `skipped` là trạng thái chết.** `ReadyTasks` yêu cầu mọi dependency phải `completed`:

```csharp
// AutonomousOrchestrator.cs:381-394
private static List<OrchestrationPlanTask> ReadyTasks(OrchestrationPlanDocument plan) =>
    ... .Where(t => IsPending(t.Status) && t.DependsOn.All(d => done.Contains(d))) ...
private static bool IsPending(string? status) => string.IsNullOrWhiteSpace(status) || status == "pending";
```

`done` chỉ gồm task `completed`. FE thì đã render nhãn "Bỏ qua" cho `skipped`
(`orchestrationStatus.ts:54-55`). Nghĩa là nếu cho phép skip mà không sửa engine, mọi task phía sau
kẹt vĩnh viễn → vòng lặp rơi vào `dependency_blocked` (`:180`).

**(c) Preflight chi phí tính theo TOÀN BỘ plan mỗi lần resume.**

```csharp
// AutonomousOrchestrator.cs:121
var preflight = await _costGuard.CanStartAsync(request.TenantId, plan.Tasks.Count * _options.PerTaskEstimateUsd, ...);
```

Resume một plan 40 task đã xong 38 vẫn bị tính tiền cho 40 → chặn nhầm bằng `cost_cap_preflight`.

**(d) `PersistPlanAsync` tự chặn khi phiên đã paused** (`AutonomousRunSink.cs:61`) — cái này ĐÚNG và
phải giữ, nó là lớp bảo vệ thứ hai cho bản sửa của người dùng:

```csharp
if (session is null || session.Status is AgentSessionStatuses.Paused or AgentSessionStatuses.Cancelled) return;
```

### 1.6 Những thứ đã có sẵn, chỉ cần nối vào (không phải làm mới)

| Có sẵn | Ở đâu | Dùng cho việc gì |
|---|---|---|
| Máy trạng thái pause/resume | `AgentSession.RequestPause/AcknowledgePause/Resume`, statuses `pause_requested`/`paused` | Trạng thái "chờ người xử lý" |
| `UpdatePlan` cho phép sửa khi `paused` | `AgentSession.UpdatePlan` (guard: draft/pending/paused) | Ghi plan đã sửa |
| Chạy tiếp plan cũ, không sinh plan mới | `OrchestratorGrpcService` resume → `_autonomous.RunExistingPlanAsync(...)` | Tiếp tục sau khi sửa |
| Optimistic concurrency | `EnsureEtagMatches` (`:391`) + `RowVersion` | Chống 2 người sửa đè nhau |
| Trace theo task | `IAutonomousRunSink.TraceAsync` | Audit "ai sửa cái gì" |
| Chọn node trên DAG | `TaskDagCanvas.tsx` `onSelect(node.task.id)` + `selectedTaskId` | Bấm vào task |
| Tách text và tool_results | `splitToolResults` (`userText.ts:170`) | Chia 2 khung soạn thảo |
| Chi phí thực của phiên | `OrchestrationV2RunDetail.actualCostUsd` từ `LlmCostLedger` | Hiện tiền tiết kiệm/đã tiêu |

Kết luận: đây là bài **nối dây + đảo chính sách**, không phải viết engine mới.

---

## 2. Nguyên tắc thiết kế

1. **Task fail = điểm dừng có người gác, không phải cái cớ để đốt LLM.** Mặc định đổi từ
   "tự động replan" sang "dừng, chờ người can thiệp".
2. **Replan trở thành hành động do người bấm**, có hiện chi phí ước tính trước khi bấm.
3. **Can thiệp là thao tác cấp task, do server thực hiện**, không phải FE tự vá JSON.
4. **Sửa output phải thực sự chảy xuống dưới** — nếu task sau đã chạy rồi thì phải reset chúng.
5. **Không phá tương thích**: giữ nguyên status hiện có, giữ `UpdatePlan`, thêm đường mới song song.

---

## 3. Thay đổi Backend

### 3.1 Chính sách khi task fail (`Clawbot.Agents.Core`)

`AutonomousRunContracts.cs` — thêm chính sách + kết quả mới:

```csharp
public static class OrchestratorFailurePolicies
{
    public const string Pause  = "pause";   // mặc định mới: dừng chờ người
    public const string Replan = "replan";  // hành vi cũ
    public const string Fail   = "fail";    // fail ngay, không replan
}

public sealed class AutonomousOrchestratorOptions
{
    public int MaxRounds { get; init; } = 1;                 // 3 -> 1 (xem §3.2)
    public string FailurePolicy { get; init; } = OrchestratorFailurePolicies.Pause;
    ...
}

// AutonomousRunResult
public static AutonomousRunResult AwaitingIntervention(int rounds) => new("paused", "awaiting_intervention", rounds);
```

`AutonomousRunRequest` thêm `string? FailurePolicy = null` để lấy chính sách theo tenant (§3.5), null
thì dùng options.

### 3.2 `AutonomousOrchestrator.ExecutePlanAsync` — 4 sửa đổi

**(1) Nhánh fail (`:176-205`)** — chèn trước khối replan:

```csharp
var policy = request.FailurePolicy ?? _options.FailurePolicy;
if (policy is OrchestratorFailurePolicies.Pause)
{
    await _sink.PersistPlanAsync(request.TenantId, request.SessionId, plan, ct: ct).ConfigureAwait(false);
    await _sink.PauseForInterventionAsync(request.TenantId, request.SessionId,
        failed[0].Id, failed[0].Error ?? "task_failed", _clock.UtcNow, ct).ConfigureAwait(false);
    return AutonomousRunResult.AwaitingIntervention(replans);
}
if (policy is OrchestratorFailurePolicies.Fail) { /* FailAsync("task_failed") */ }
// còn lại: giữ nguyên khối replan hiện tại
```

Thứ tự `PersistPlanAsync` **trước** `PauseForInterventionAsync` là bắt buộc — vì
`PersistPlanAsync` tự bỏ qua khi session đã `paused` (§1.5d), đảo thứ tự sẽ mất output/error của
task vừa fail.

**(2) `MaxRounds` mặc định 3 → 1.** Kể cả khi tenant chọn `replan`, một lần là đủ; hiện tại 3 lần chỉ
nhân chi phí lên chứ tỉ lệ cứu được thấp (LLM nhận đúng một câu "avoid the failed approach", lần 2
và lần 3 không có thêm thông tin gì mới).

**(3) `skipped` được coi là đã thỏa dependency** (`ReadyTasks` `:381-394`):

```csharp
private static readonly string[] SatisfiedStatuses = ["completed", "skipped"];

private static List<OrchestrationPlanTask> ReadyTasks(OrchestrationPlanDocument plan)
{
    var done = plan.Tasks.Where(t => SatisfiedStatuses.Contains(t.Status, StringComparer.OrdinalIgnoreCase))
                         .Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
    ...
}

private static bool IsPending(string? status) =>
    string.IsNullOrWhiteSpace(status) || string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase);
```

Đồng thời `ToAgentTask` (`:396-429`) khi gom `upstream_results` phải **bỏ qua** task `skipped` (không
có output) thay vì đẩy chuỗi rỗng xuống.

**(4) Preflight chỉ tính task còn phải chạy** (`:121`):

```csharp
var remaining = plan.Tasks.Count(t => IsPending(t.Status));
var preflight = await _costGuard.CanStartAsync(request.TenantId,
    Math.Max(remaining, 1) * _options.PerTaskEstimateUsd, _clock.UtcNow, ct).ConfigureAwait(false);
```

### 3.3 `IAutonomousRunSink` + `AutonomousRunSink`

Thêm một method:

```csharp
Task PauseForInterventionAsync(Guid tenantId, Guid sessionId, string taskId, string reason,
                               DateTimeOffset at, CancellationToken ct = default);
```

Cài đặt trong `AutonomousRunSink`:

- load session, nếu đang `Running`/`PauseRequested` → `RequestPause()` rồi `AcknowledgePause()` để về
  `Paused` (dùng lại đúng máy trạng thái sẵn có, không thêm status mới);
- ghi trace: `phase = "awaiting_intervention"`, `taskId`, message tiếng Việt
  *"Bước {taskId} lỗi: {reason}. Đã tạm dừng để bạn xử lý — chưa tốn thêm chi phí lập kế hoạch lại."*;
- publish Redis run-event như các trạng thái khác để SignalR đẩy realtime;
- gửi notification (dùng lại đường notification của terminal states) loại
  `orchestration_intervention`, link `/agents/runs/{sessionId}`.

**Không thêm cột `pause_reason`.** Trạng thái "đang chờ can thiệp" suy ra được:
`status == "paused" && tasks.any(status == "failed")`. Bớt được một migration và một điểm lệch dữ
liệu.

### 3.4 API can thiệp cấp task (mới)

`proto/orchestrator.proto`:

```proto
rpc InterveneTask (InterveneTaskRequest) returns (PlanResponse);

message InterveneTaskRequest {
  string tenant_id       = 1;
  string session_id      = 2;
  string task_id         = 3;
  string action          = 4;  // edit_output | retry | skip | replan
  string output          = 5;  // dùng cho edit_output
  string input_json      = 6;  // tùy chọn: ghi đè input của task
  bool   rerun_downstream= 7;  // reset các task phụ thuộc về pending
  string expected_etag   = 8;
  string actor_user_id   = 9;  // để ghi audit
}
```

`OrchestratorGrpcService.InterveneTask` — thứ tự kiểm tra (fail sớm, thông báo rõ):

1. `EnsureEtagMatches(session, request.ExpectedEtag)` → 409 khi lệch.
2. `if (RunningSessions.ContainsKey(session.Id)) throw FailedPrecondition("run_in_progress")` —
   **bắt buộc**, xem §1.5a.
3. `session.Status != Paused` → `FailedPrecondition("session_not_paused")`.
4. Parse `session.PlanJson` → `OrchestrationPlanDocument`; task không tồn tại → `NotFound`.
5. Áp dụng action trên document (immutable, dùng `WithTaskStatus` / `with`):

| action | Kết quả trên task |
|---|---|
| `edit_output` | `status = "completed"`, `output = <đã redact>`, `error = null` |
| `retry` | `status = "pending"`, `output = null`, `error = null` (giữ nguyên input trừ khi có `input_json`) |
| `skip` | `status = "skipped"`, `error = null` |
| `replan` | giữ nguyên, chỉ đánh dấu để `Control resume` chạy nhánh replan một vòng |

6. Nếu `rerun_downstream` → duyệt đồ thị phụ thuộc theo chiều xuôi, mọi task phụ thuộc (trực tiếp và
   gián tiếp) đang `completed`/`failed`/`skipped` → về `pending`, `output/error = null`.
7. **Redact PII** output do người nhập qua đúng bộ redactor đang dùng cho plan trước khi ghi
   (`OrchestrationPlanRedactor`) — người dùng có thể dán dữ liệu khách vào đây.
8. Chặn kích thước: `output` tối đa 8192 ký tự (dùng lại hằng `MaxTaskInputChars` của
   `OrchestrationPlanValidator`, đặt thành `MaxTaskOutputChars` cùng giá trị) → `InvalidArgument`.
9. `OrchestrationPlanValidator.Validate` trên document kết quả (bắt cycle nếu `input_json` sai).
10. `session.UpdatePlan(OrchestrationPlanJson.Serialize(plan))` + `SaveChanges` (RowVersion đổi → etag mới).
11. Ghi trace audit: `phase = "task_edited" | "task_retry" | "task_skipped"`, message có `actor_user_id`.

REST wrapper trong `OrchestrationV2Endpoints.cs`:

```
POST /api/orchestration/v2/runs/{sessionId}/tasks/{taskId}/intervene
body: { action, output?, inputJson?, rerunDownstream, etag }
perm: orchestration:manage        // đổi hành vi thực thi, không phải chỉ sửa nháp
```

Chọn `orchestration:manage` (cùng quyền với pause/cancel) chứ không phải `orchestration:run`, vì thao
tác này thay đổi kết quả thực thi và có thể kích hoạt chạy lại nhánh dưới.

### 3.5 Chính sách theo tenant (migration `0101`)

Theo đúng khuôn mẫu `Tenant.RequireOrchestrationApproval` đã có:

- `Tenant.OrchestratorFailurePolicy` (string, mặc định `"pause"`) + `SetOrchestratorFailurePolicy(...)`;
- map trong `DomainModelConfigurations`;
- resolver theo mẫu `EfOrchestrationApprovalResolver`, đọc ra rồi nhét vào `AutonomousRunRequest`;
- GET/PUT trong `AdminEndpoints` cạnh toggle duyệt hiện có.

`deploy/migrations/0101_tenants_orchestrator_failure_policy.sql` — một `SqlCommand`, **không có `GO`**:

```sql
IF COL_LENGTH('dbo.tenants', 'orchestrator_failure_policy') IS NULL
    ALTER TABLE dbo.tenants
        ADD orchestrator_failure_policy NVARCHAR(20) NOT NULL
            CONSTRAINT DF_tenants_orchestrator_failure_policy DEFAULT N'pause';
```

### 3.6 (Tùy chọn, Phase 3) Replan giữ lại việc đã xong

Ngay cả khi người dùng chủ động chọn replan, không nên vứt bỏ task đã hoàn thành. Sửa
`BuildReplanGoal` để truyền cả danh sách task **đã completed kèm id và tóm tắt output**, kèm chỉ thị
"giữ nguyên các id này với status completed, chỉ thay thế nhánh bị lỗi". Sau khi có plan mới, **merge
phía server**: task nào có id trùng task đã `completed` trong plan cũ thì carry-over `status/output`
thay vì tin LLM. Không merge được thì mới rơi về hành vi cũ.

Đây là phần đắt nhất và rủi ro nhất (phụ thuộc LLM tuân thủ id), nên tách riêng, làm sau khi Phase
1-2 đã chạy ổn. Phase 1-2 đã đủ giải quyết yêu cầu của khách.

---

## 4. Thay đổi Frontend

### 4.1 `orchestrationV2.ts` — client mới

```ts
export type OrchestrationV2TaskAction = "edit_output" | "retry" | "skip" | "replan";

export async function interveneOrchestrationV2Task(
  sessionId: string, taskId: string,
  payload: { action: OrchestrationV2TaskAction; output?: string; inputJson?: string;
             rerunDownstream: boolean; etag: string },
): Promise<OrchestrationV2Plan> { ... }
```

Bổ sung `"pause_requested"` và `"skipped"` vào union type nếu chưa có, để không rơi vào nhánh
`string` mập mờ.

### 4.2 `TaskInterventionDialog.tsx` (mới, thay `window.prompt`)

- Khung trên: textarea phần văn bản (từ `splitToolResults(task.output).text`), tối thiểu 12 dòng.
- Khung dưới: editor JSON cho `[tool_results]`, validate `JSON.parse` khi blur, báo lỗi tại chỗ; giữ
  nguyên marker khi ghép lại để `toHumanTaskSummary`/`PromoteUpstreamIds` phía BE vẫn đọc được
  (`content_id`, `schedule_id`, `post_url`, `lead_id`, `conversation_id`).
- Checkbox **"Chạy lại các bước phía sau"**: tự tick và khóa ở trạng thái bật khi có task phụ thuộc
  đã `completed`, kèm câu giải thích *"Bước sau đã chạy với kết quả cũ, cần chạy lại để nhận kết quả
  bạn vừa sửa."* — đây là chỗ chống hiểu nhầm ở §1.4-mục-3.
- Hàng nút: `Lưu & chạy tiếp` / `Lưu, giữ tạm dừng` / `Chạy lại bước này` / `Bỏ qua bước này`.
- `Lưu & chạy tiếp` = `intervene(...)` rồi `control(resume)` nối tiếp, dùng etag trả về từ lời gọi
  đầu (etag đổi sau khi ghi — nếu dùng etag cũ sẽ 409).
- Đếm ký tự, chặn ở 8192 khớp giới hạn BE.

### 4.3 `OrchestrationPanel.tsx`

- Xóa `replaceTaskOutput` (`:60-74`) và nút `window.prompt` (`:494-513`), thay bằng dialog trên.
- Banner khi `status === "paused" && tasks.some(failed)`:
  *"Bước «{tên}» lỗi. Phiên đã tạm dừng để bạn kiểm soát — chưa phát sinh chi phí lập kế hoạch lại.
  Bạn có thể sửa kết quả, chạy lại bước này, bỏ qua, hoặc để AI lập lại kế hoạch (tốn thêm chi phí)."*
  Nút "Để AI lập lại kế hoạch" hiện kèm ước tính `số task còn lại × PerTaskEstimateUsd` để người dùng
  thấy tiền trước khi bấm.
- Trạng thái `pause_requested`: hiện chip *"Đang dừng an toàn — chờ bước hiện tại kết thúc"* và khóa
  các nút can thiệp. Hiện tại `:599-617` không render gì cho trạng thái này nên UI trông như treo.
- Bấm node đã `completed` khi phiên đang `running`: hiện nút *"Tạm dừng để sửa bước này"* → gọi
  `control(pause)` và giữ `selectedTaskId`, khi sang `paused` thì tự mở dialog. Đây là đúng câu chữ
  yêu cầu của khách.

### 4.4 `TaskDagCanvas.tsx` / `TaskResultDetails.tsx` / `AgentRunDetailPage.tsx`

- `TaskDagCanvas`: thêm màu/nhãn cho `skipped` (xám gạch ngang) và viền nhấn cho task đang được chọn
  để sửa; giữ nguyên `onSelect`.
- `TaskResultDetails`: thêm badge *"Kết quả đã được chỉnh sửa thủ công"* khi trace của task có phase
  `task_edited`.
- `AgentRunDetailPage`: hiện chỉ đọc — nối cùng dialog và cùng banner để người dùng vào từ trang chi
  tiết run cũng xử lý được, không phải quay về `/agents`.

---

## 5. Thứ tự triển khai

| Phase | Nội dung | Kết quả đo được |
|---|---|---|
| 1 | §3.1 §3.2 §3.3 — fail thì pause, sửa `skipped`, sửa preflight, `MaxRounds` 3→1 | Chấm dứt replan tự động. Một task fail không còn nhân chi phí lên 4 lần |
| 2 | §3.4 §4.1 §4.2 §4.3 — API can thiệp + dialog sửa output | Đúng yêu cầu khách: bấm task → sửa → đẩy xuống bước sau |
| 3 | §3.5 §4.4 — toggle theo tenant, DAG/badge/trang chi tiết | Vận hành và audit |
| 4 | §3.6 — replan giữ lại việc đã xong | Giảm nốt chi phí ở nhánh replan tự nguyện |

Phase 1 độc lập, deploy được ngay và tự nó đã cắt phần lớn chi phí. Phase 2 mới là phần "sửa output".

---

## 6. Kiểm thử

Lưu ý: bộ test .NET đã bị gỡ khỏi repo trước đây (CI hiện chỉ chạy build + lint + E2E). Nên **khôi
phục scaffold xUnit cho riêng `Clawbot.Agents.Core`** để phủ phần engine — đây là logic vòng lặp,
không có test thì rất dễ vỡ thầm.

Backend (unit, `Clawbot.Agents.Core`):

- task fail + policy `pause` → không gọi `_planner.ReplanAsync` lần nào, `_sink.PauseForInterventionAsync` được gọi đúng 1 lần, kết quả `paused/awaiting_intervention`;
- task fail + policy `replan` → hành vi cũ, đúng 1 vòng với `MaxRounds = 1`;
- dependency ở trạng thái `skipped` → task sau vẫn `ready`, không rơi vào `dependency_blocked`;
- preflight tính theo số task `pending`, không theo tổng;
- sửa output task A rồi reset B về `pending` → `upstream_results` mà B nhận chứa **văn bản đã sửa**.

Backend (gRPC/API):

- `InterveneTask` trả `FailedPrecondition("run_in_progress")` khi session còn trong `RunningSessions`;
- trả 409 khi etag lệch; `session_not_paused` khi đang chạy;
- output > 8192 ký tự → `InvalidArgument`;
- output chứa số điện thoại/email → bản lưu trong `PlanJson` đã được redact;
- mỗi lần can thiệp sinh đúng 1 trace audit có `actor_user_id`.

E2E (Playwright, mock auth theo `playwright-mock-auth-session`):

- chạy plan → giả lập bước 2 fail → phiên dừng ở "Tạm dừng, chờ xử lý" (không có trace "re-planned");
- mở dialog, sửa text, tick chạy lại bước sau, bấm "Lưu & chạy tiếp" → bước 3 chạy và output của nó
  phản ánh nội dung đã sửa;
- kiểm tra `actualCostUsd` sau kịch bản này thấp hơn kịch bản replan cũ.

Thủ công: một run thật 3 bước trên tenant demo, so `actualCostUsd` trước/sau.

---

## 7. Rủi ro và cách chặn

| Rủi ro | Mức | Cách chặn |
|---|---|---|
| Runner còn sống ghi đè bản sửa | Cao | Chặn bằng `RunningSessions` (§3.4 bước 2) + `PersistPlanAsync` tự bỏ qua khi paused |
| Người dùng sửa xong nhưng bước sau đã chạy → tưởng tính năng hỏng | Cao | `rerun_downstream` tự tick và khóa bật khi có dependent đã completed (§4.2) |
| `skipped` làm kẹt nhánh dưới | Cao | Sửa `ReadyTasks` cùng lúc với việc mở nút "Bỏ qua" — không được tách 2 phase |
| Plan treo mãi ở `paused` nếu không ai xử lý | Trung bình | Notification khi vào trạng thái chờ; đưa vào tiêu chí reaper/cảnh báo phiên `paused` quá 24h (chỉ cảnh báo, không tự hủy) |
| Người dùng dán PII vào output | Trung bình | Redact trước khi ghi (§3.4 bước 7), đúng nguyên tắc `pii-redact-derived-content` |
| Đổi mặc định sang `pause` làm lịch tự động (`RunSchedule`) đứng chờ người | Trung bình | Lịch chạy nền nên giữ chính sách theo tenant; cân nhắc cho `source = "schedule"` dùng `replan` 1 vòng rồi mới fail — chốt ở §9.3 |
| `MaxRounds` 3→1 làm giảm tỉ lệ tự cứu | Thấp | Có chủ đích: thay bằng người can thiệp, rẻ hơn và chính xác hơn |
| Hai người cùng sửa một run | Thấp | Đã có `EnsureEtagMatches` + RowVersion |

---

## 8. Danh sách file đụng tới

Backend:

```
src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousRunContracts.cs      (policy, AwaitingIntervention, MaxRounds)
src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs      (:121 preflight, :176-205 nhánh fail, :381-394 ready/skipped, :396-429 upstream)
src/agents/Clawbot.Agents.Core/Orchestrator/IAutonomousRunSink.cs          (+PauseForInterventionAsync)
src/agents/Clawbot.Agents.Core/Orchestrator/OrchestrationPlanValidator.cs  (+MaxTaskOutputChars)
src/agents/Clawbot.AgentService/Services/AutonomousRunSink.cs              (cài đặt + trace + notify + redis event)
src/agents/Clawbot.AgentService/Services/OrchestratorGrpcService.cs        (+InterveneTask)
src/agents/Clawbot.AgentService/Services/AutonomousPlanner.cs              (Phase 4: giữ task completed)
src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs                  (+POST .../tasks/{taskId}/intervene)
src/api/Clawbot.Api/Endpoints/AdminEndpoints.cs                            (GET/PUT policy theo tenant)
src/shared/Clawbot.Domain/Tenants/Tenant.cs                                (+OrchestratorFailurePolicy)
proto/orchestrator.proto                                                   (+InterveneTask)
deploy/migrations/0101_tenants_orchestrator_failure_policy.sql             (mới, không GO)
```

Frontend:

```
src/frontend/clawbot-web/src/shared/api/orchestrationV2.ts                       (+intervene, +status skipped/pause_requested)
src/frontend/clawbot-web/src/features/agents/TaskInterventionDialog.tsx          (mới)
src/frontend/clawbot-web/src/features/agents/OrchestrationPanel.tsx              (bỏ prompt/replaceTaskOutput, banner, pause_requested)
src/frontend/clawbot-web/src/features/agents/TaskDagCanvas.tsx                   (skipped + node đang sửa)
src/frontend/clawbot-web/src/features/agents/TaskResultDetails.tsx               (badge đã sửa tay)
src/frontend/clawbot-web/src/features/agents/AgentRunDetailPage.tsx              (nối dialog + banner)
src/frontend/clawbot-web/src/features/agents/orchestrationStatus.ts              (nhãn awaiting_intervention/pause_requested)
```

---

## 9. Điểm đã chốt

1. **Không thêm status mới.** Dùng `paused` + suy ra "chờ can thiệp" từ `tasks.any(failed)`. Bớt một
   migration, bớt một điểm lệch dữ liệu, FE/máy trạng thái hiện có không phải sửa.
2. **Quyền là `orchestration:manage`**, không phải `orchestration:run` — thao tác này đổi kết quả
   thực thi.
3. **Lịch tự động (`source = "schedule"`)**: vẫn theo chính sách của tenant. Nếu tenant để `pause`,
   run của lịch cũng dừng chờ người — có notification. Đây là lựa chọn có ý thức: dừng chờ rẻ hơn
   nhiều so với replan mù lúc 3 giờ sáng. Tenant nào không muốn thì đặt `replan`.
4. **`MaxRounds` mặc định 1** kể cả ở chính sách `replan`.
5. **Phase 3 (§3.6) là tùy chọn** — Phase 1-2 đã thỏa yêu cầu khách; Phase 3 chỉ tối ưu thêm cho
   nhánh replan tự nguyện.
6. **Không dọn `MaxRounds`/`ReplanAsync`** — giữ nguyên nhánh replan để tenant chọn được, không xóa.
