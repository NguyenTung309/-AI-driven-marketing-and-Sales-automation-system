# Kế hoạch sửa lỗi Nội dung: lên lịch, agent review, và dọn bài của plan hỏng

Ngày: 2026-08-09
Nhánh: thang/ai-autoreply-kb-improvements
Trạng thái: đã triển khai trong working tree, đang kiểm chứng cuối; không migration, repair script, deploy, commit hay push nào đã được thực thi. Mọi điểm mở đã được chốt — xem §8 và cập nhật thực thi §9.

Yêu cầu gốc của khách (3 nhóm):

1. Ở `Content > Quản lý bài viết & nội dung`: hai lựa chọn "Chọn thời điểm riêng" và "Chọn khung giờ
   vàng" đều hỏng — chọn ngày giờ nào cũng báo "Thông tin gửi lên chưa hợp lệ", giữ nguyên mặc định
   rồi bấm "Xác nhận" cũng vậy. Và khi đến ngày giờ đã hẹn thì bài cũng không được đăng lên kênh.
2. Agent review đang lỗi, không review được bài, log báo 500 (Internal Server Error); do đó bài ABC
   đứng vĩnh viễn ở trạng thái "Chờ agent review".
   => Yêu cầu: khi Plan A fail, các bài ABC do Plan A sinh ra phải chuyển sang trạng thái "Từ chối".
3. Màn hình chi tiết bài đăng lỗi 500 và thao tác trên UI không dùng được.

---

## 1. Kết luận điều tra

Ba nhóm bug trên **không phải ba lỗi độc lập**. Chúng là một chuỗi nhân quả duy nhất, cộng thêm một
lỗi hạ tầng DB riêng biệt. Bằng chứng ở từng mắt xích bên dưới, tất cả đều đọc từ code trong repo,
không suy đoán.

### 1.1 Widget ngày/giờ KHÔNG hỏng — lỗi nằm ở trạng thái bài

Đã loại trừ từng khả năng thuộc về widget:

| Nghi vấn | Kết quả kiểm tra | Kết luận |
|---|---|---|
| Lệch múi giờ khi gửi lên | `scheduledAtIso()` — `ContentWorkspacePage.tsx:335-340` — dựng `new Date("YYYY-MM-DDTHH:mm:00")` theo giờ local rồi `.toISOString()` | Đúng, không lệch |
| Mặc định rơi vào quá khứ | `defaultScheduleDate()` — `ContentWorkspacePage.tsx:318-322` — mặc định là **ngày mai** + `"09:00"` (`:1587-1588`) | Luôn ở tương lai |
| Chế độ "giờ vàng" gửi sai | `mode === "golden"` trả `null`, BE tự `goldenHour.ResolveNext(...)` (`ContentEndpoints.cs:2038-2051`) | Đúng thiết kế |
| Input bị disable sai | `disabled={mode === "golden"}` (`ContentWorkspacePage.tsx:1515, 1525`) | Đúng thiết kế |

Đường đi thật của lỗi:

```
FE  POST /api/content/items/{id}/schedule
BE  ScheduleItemAsync            ContentEndpoints.cs:1338
      -> autoScheduler.CreateIntentAsync
           -> item.CanScheduleCurrentRevision() == false
           -> throw "content_current_revision_not_schedulable"   ContentAutoScheduler.cs:86
BE  catch -> 400 { errorCode: "content.item_not_schedulable" }   ContentEndpoints.cs:1348-1355
FE  build đang chạy chưa có bảng mã lỗi -> map cứng theo HTTP status:
    400 => "Thông tin gửi lên chưa hợp lệ. Vui lòng kiểm tra lại."   userText.ts:6
```

`CanScheduleCurrentRevision()` (`ContentItem.cs:440-445`) đòi **đủ 5 điều kiện**:

```csharp
DeletedAt is null
&& ActivePublishAttemptId is null
&& Status == "approved"          // <- phải bấm "Duyệt phát hành" trước
&& HasCurrentCompletedReview()   // <- agent review phải xong cho đúng revision hiện tại
&& ApprovedRevision == ContentRevision;
```

Bài đang kẹt "Chờ agent review" thì `HasCurrentCompletedReview()` false, kéo theo `Status` không bao
giờ lên được `"approved"`. Vậy **thông báo "Thông tin gửi lên chưa hợp lệ" là triệu chứng của bug 2,
không phải bug của ô ngày giờ.** Sửa ô ngày giờ sẽ không sửa được gì.

Cùng cơ chế đó cũng vô hiệu hóa toàn bộ nút bấm: `ToDto()` (`ContentEndpoints.cs:1878-1942`) tính
`reviewCompleteForCurrent` từ `AgentReviewedRevision == ContentRevision` và `AgentReviewStatus` thuộc
{passed, rejected, needs_human, failed}. Review kẹt ở `pending` => cờ này false => `CanApprove`,
`CanReject`, `CanSchedule` đều false => UI khóa hết nút. Đây chính là "thao tác trên UI không dùng
được" ở mục 3 của khách.

### 1.2 Vì sao agent review không bao giờ xong

Hàng đợi review là bảng bền `content_review_tasks` với lease token. Vòng đời:

```
ContentGenerateTool  -> tạo ContentItem + insert review task   ContentTools.cs:38-113
ContentReviewDispatchWorker (BackgroundService, PeriodicTimer)
  -> ReviewTenantWorker.RunTenantAsync
       -> LoadCandidatesAsync -> TryLeaseCandidateAsync -> coordinator.ProcessAsync
```

Khi executor ném exception (LLM lỗi, timeout, parse fail), `ReviewTenantWorker` bắt ở
`ReviewTenantWorker.cs:66-74` rồi gọi `HandleOperationalFailureAsync`:

- còn lượt: `task.ReleaseForRetry(...)` với backoff mũ 2 (`ComputeBackoff`, `:356-367`)
- hết lượt (`AttemptCount >= MaxAttempts`): `task.Fail(..., ReviewReasonAttemptLimitReached, ...)`

**Ở bản đang deploy, khi task chết hẳn thì `ContentItem` không bị đụng tới.** Hệ quả:

- `AgentReviewStatus` vẫn là `pending`, `AgentReviewedRevision` vẫn `null`
- task đã ở trạng thái terminal (`failed`), không worker nào nhận lại
- => bài kẹt "Chờ agent review" **vĩnh viễn**, đúng như khách mô tả

Lưu ý: `ContentReviewer` bắt gần như mọi exception và trả về verdict thay vì ném
(`ContentReviewer.cs:178-184` trả `needs_human` cho timeout / `reviewer_unavailable`;
`FailedOutcome` ở `:434-440` trả `reviewer_error`). Nên nhánh "task chết mà item còn pending" xảy ra
khi lỗi ở **ngoài** executor: worker chết giữa chừng, lease hết hạn liên tục, hoặc `BeginAgentReview`
bị chặn vì đã đủ `MaxAgentReviewAttempts = 5` (`ContentItem.cs:10, 122`).

### 1.3 Lỗi 500 khi bấm "Thử agent review lại" — nguyên nhân độc lập, ở tầng DB

Đây là lỗi 500 khách chụp được. Nguyên nhân là một unique index **không có filter**:

`deploy/migrations/0077_content_publishing_policy_constraints.sql:322-325`

```sql
CREATE UNIQUE INDEX UX_content_review_tasks_item_revision
    ON dbo.content_review_tasks (tenant_id, content_item_id, content_revision);
```

Index này ràng buộc **mọi** row, kể cả row đã terminal. Trong khi đó `RetryAgentReviewAsync`
(`ContentEndpoints.cs:1539-1590`) chỉ tìm task đang sống:

```csharp
var activeTask = await db.ContentReviewTasks
    .Where(t => t.ContentItemId == item.Id
        && t.ContentRevision == item.ContentRevision
        && (t.Status == ContentReviewTask.StatusPending
            || t.Status == ContentReviewTask.StatusLeased))   // <- bỏ qua failed/completed/canceled_stale
    ...
if (activeTask is null)
    db.ContentReviewTasks.Add(ContentAssetLifecycle.CreateQuietPeriodReviewTask(...));
await db.SaveChangesAsync(ct);   // <- KHÔNG bắt DbUpdateException
```

Chuỗi thực tế: task cũ đã `failed` (mục 1.2) => `activeTask == null` => `Add` một row mới với đúng
`(tenant_id, content_item_id, content_revision)` cũ => **Msg 2601 duplicate key** =>
`DbUpdateException` không được bắt => `app.UseExceptionHandler()` (`Program.cs:300`) trả **500**.

Và vì `ReopenAgentReview` (nếu có) **không** tăng `ContentRevision`, khóa vẫn y hệt — không thể né
bằng cách sửa code C# đơn thuần, phải sửa index.

### 1.4 Vì sao đến giờ bài không được đăng

Không phải job chết. `ContentPublishJob` được đăng ký đầy đủ:

- `HangfireModule.cs:196-200`: recurring `content-publish-due`, cron `*/5 * * * *`, queue `content`
- `HangfireModule.cs:14`: queue `content` nằm trong danh sách worker

Bài không đăng vì **chưa bao giờ vào được trạng thái `scheduled`** (mục 1.1 — lên lịch fail ở 400).
Và ngay cả khi lên lịch được, `ResolvePublishHoldReason` (`ContentPublishJob.cs:474-496`) vẫn chặn:

```csharp
if (!item.CanPublishCurrentRevision())  return "current_revision_not_publishable";
if (schedule.ApprovalMode is null || schedule.PublishingPolicyVersionApplied is null)
    return "approval_context_missing";
```

=> schedule bị `MarkHeld(...)`, không gọi provider. Đây là hành vi fail-closed đúng thiết kế, không
phải bug. Sửa mắt xích review là điều kiện để dòng chảy bình thường thông lại.

Đường publish vẫn cần một lớp sửa **tính đúng đắn cạnh tranh**: session lock cùng validation generation
phải bao claim publish, và replan/fail/cancel không được terminalize generation có provider request đang
in-flight. Human takeover hoặc soft delete cũng không được che giấu marker active attempt. Những guard
này giữ nguyên policy duyệt trước khi đăng; chúng không tạo đường vòng cưỡng bức đăng.

Một điểm cần kiểm tra khi deploy (không phải sửa code): `IsPublicationPausedAsync`
(`ContentWorkflowRuntimeGate.cs:105-108`). Nếu bảng `content_workflow_runtime_gate` tồn tại nhưng
**đọc lỗi**, gate fail-closed và **dừng toàn bộ publish** (`:89-99`). Phải xác nhận
`publication_paused = 0` trên DB khách.

### 1.5 Vì sao yêu cầu "Plan A fail thì ABC chuyển Từ chối" chưa làm được hôm nay

Không có đường liên kết nào từ bài đăng ngược về phiên orchestration:

- `ContentItem` chỉ có `CreatedByAgentId`, `BriefId`, `CreatedBy` — **không có** session id / plan id
- `ToolContext` (`IAgentTool.cs:18-25`) mang `TenantId, TaskId, AgentDefinitionId, AgentCode,
  RequireHighRiskApproval, DryRun, CanPublishContent` — **không có** `SessionId`
- `ContentGenerateTool` (`ContentTools.cs:38-113`) vì vậy không có gì để đóng dấu

Tin tốt: `GenericLlmAgentWorker` **đã** có sẵn `WorkerRunContext(TenantId, SessionId)`
(`GenericLlmAgentWorker.cs:11, 27`) ngay tại chỗ dựng `ToolContext` (`:85`). Nên việc nối provenance
là một thay đổi nhỏ, không phải kiến trúc lại.

---

## 2. Phần ĐÃ viết trong working tree nhưng CHƯA commit/deploy

Đây là điểm quan trọng về mặt kế hoạch: một phần đáng kể bản vá **đã tồn tại** trong cây làm việc.
Bản build khách đang chạy không có chúng. `git diff --stat`:

```
 src/agents/.../ContentReviewCoordinator.cs        |  14 +
 src/agents/.../ReviewTenantWorker.cs              |  51 +-
 src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs | 281 ++++++++++++---
 src/frontend/.../ContentWorkspacePage.tsx         |  82 +-
 src/frontend/.../shared/utils/userText.ts         |  51 +
 src/shared/Clawbot.Domain/Content/ContentItem.cs  |  78 ++
```

Nội dung đã có:

| Đã viết | Ở đâu | Chữa triệu chứng nào |
|---|---|---|
| `ContentItem.MarkAgentReviewExhausted(...)` — đẩy item về `needs_human` khi cạn lượt | `ContentItem.cs:241` | 1.2 — hết kẹt "Chờ agent review" vĩnh viễn |
| `ContentItem.ReopenAgentReview(...)` — mở lại một chu kỳ review cho revision hiện tại | `ContentItem.cs` (khối mới) | 1.3 — thay vì trả 429 ngõ cụt |
| `ContentItem.ResolveWorkflowState()` — định nghĩa trạng thái quy trình dùng chung API/AgentService | `ContentItem.cs:213` | hiển thị trạng thái nhất quán |
| Worker gọi `MarkAgentReviewExhausted` khi terminalize | `ReviewTenantWorker.cs:263-294, 352-353` | 1.2 |
| Coordinator gọi `MarkAgentReviewExhausted` trong cùng `SaveChanges` | `ContentReviewCoordinator.cs:480-494` | 1.2 |
| Nhánh `mustReopen` trong retry endpoint | `ContentEndpoints.cs:1550, 1565-1579` | 1.3 (một nửa) |
| `ScheduleBlockedReason(item)` + `ScheduleBlockedReason` trong DTO | `ContentEndpoints.cs` `ToDto` | 1.1 — UI nói rõ vì sao bị chặn |
| Bảng `CONTENT_ERROR_MESSAGES` 13 mã lỗi tiếng Việt | `userText.ts:16-42, 76-77` | 1.1 — hết "Thông tin gửi lên chưa hợp lệ" |
| Alert hiển thị lý do chặn lịch trên UI | `ContentWorkspacePage.tsx:1046-1048` | 1.1 |
| `GetItemAssetAsync` + `ItemAssetUrl` (ảnh qua API thay vì URL MinIO ký hạn) | `ContentEndpoints.cs` | 500 ở màn chi tiết (ảnh) |
| `ChainMetricsAsync` đổi `[FromQuery] int days` -> `int? days` | `ContentEndpoints.cs` | 500 do binding thiếu tham số |

Cùng lớp lỗi "500 do thiếu query parameter bắt buộc" đã quan sát được trong log thật:

`src/api/Clawbot.Api/logs/api-20260808.log:6496-6522`

```
[ERR] HTTP DELETE /api/kb/modules/.../versions/... responded 500
Microsoft.AspNetCore.Http.BadHttpRequestException:
  Required parameter "bool includeRollbackTarget" was not provided from query string.
```

Vì `Program.cs:44` bật `AddProblemDetails()` và `:300` bật `UseExceptionHandler()`, mọi
`BadHttpRequestException` đều thành **500 chứ không phải 400**. Đây là bẫy hệ thống, xem FIX-6.

**=> Một phần lớn công việc khắc phục chỉ là commit + build + deploy những gì đã viết.** Kế hoạch
bên dưới tách rõ phần "ship cái đã có" (Giai đoạn 0) và phần "còn phải viết" (FIX-1..FIX-7).

---

## 3. Các sửa còn thiếu

### FIX-0 (CHẶN) — Script vá dữ liệu đang kẹt trên DB khách

Đây là hạng mục **dễ bị bỏ sót nhất**, và nếu bỏ sót thì khách deploy xong vẫn thấy y nguyên bug.

Lý do: các bản vá code chỉ chặn **bài mới** rơi vào bẫy. Bài **đang kẹt sẵn** có task terminal nên
`ReviewTenantWorker.LoadCandidatesAsync` (chỉ tìm `pending`/`leased`) không bao giờ nhặt lại.

Đã thêm `deploy/repair_stuck_content_review.sql`. Chạy script sau các migration hiện có và trước khi
khởi động worker phiên bản mới. Script chỉ sửa item khi tất cả điều kiện đều đúng:

1. Item thuộc trạng thái `draft`, chưa bị xóa và không có publish attempt đang claim.
2. Item đang `agent_review_status = 'pending'`.
3. Task cùng `tenant_id`, `content_item_id` và `content_revision` đã `failed` hoặc `canceled_stale`.

Update tương đương `MarkAgentReviewExhausted`: item thành `needs_human`, có review reason
`content_review_attempt_limit_reached`, image `failed`, và `human_approval_requirement_reason =
agent_non_pass` trừ `migration_cutover`. Script in số row sửa được; chạy lại lần hai ra 0.

Không dọn duplicate review task và không đổi unique index: `UX_content_review_tasks_item_revision`
là invariant cố ý, được giữ bởi FIX-1. Đây là script vận hành một lần, **không** đưa vào
`deploy/migrations/` và không bao giờ được tự chạy lúc API khởi động.

### FIX-1 (CHẶN) — Giữ một durable review task cho mỗi revision

Đã loại phương án DROP unique index và tạo filtered index. `UX_content_review_tasks_item_revision`
phản ánh đúng invariant cần giữ: **mỗi content revision chỉ có một review task bền**. Mở lại task
terminal ngay trên row hiện có vừa bảo toàn lịch sử vận hành, vừa không tạo cửa sổ có nhiều task sống
cho một revision.

Không thêm migration cho index này. `ContentReviewTask` có transition `ReopenForManualRetry(at)` chỉ
nhận task terminal (`completed`, `failed`, `canceled_stale`) và atomically reset về `pending`: lease,
completion, error, attempt count và refine count đều được xóa/reset; `NextAttemptAt = at`.

### FIX-2 (CHẶN) — Retry endpoint mở lại task cũ, idempotent khi task đang sống

`RetryAgentReviewAsync` tải task theo `(tenant_id, content_item_id, content_revision)` **bất kể trạng
thái**, rồi xử lý theo bảng sau:

| Task hiện tại | Kết quả API |
|---|---|
| `pending`, đã tới giờ | 200 idempotent, không thêm row |
| `pending`, còn cooldown | 429 `content.review_retry_cooldown` |
| `leased` | 409 `content.review_in_progress`, không cướp lease |
| terminal | `ContentItem.ReopenAgentReview` + `ContentReviewTask.ReopenForManualRetry` trong một `SaveChanges` |
| không có (dữ liệu legacy hỏng) | tạo row duy nhất; 2601/2627 do hai request đua được coi là 200 idempotent sau reload |

Endpoint chỉ cho item `Status == "draft"` quay lại review. Không cho một người chỉ có `content:write`
ngầm bỏ approval/lịch hiện có trên bài `approved` hoặc `scheduled`; người dùng phải sửa thành revision
mới theo flow hiện hành. `DbUpdateConcurrencyException` trả 409 có mã `content.review_retry_conflict`,
không bao giờ thành 500.

### FIX-3 (CAO) — Provenance: nối bài đăng về phiên orchestration

Điều kiện tiên quyết cho FIX-4. Provenance phải xác định chính xác **phiên + generation của plan** và
phải ghi nhận lúc người dùng tiếp quản draft:

- `0102_agent_sessions_tenant_key.sql` tạo candidate key `(tenant_id, id)` để FK provenance không thể
  trỏ nhầm tenant.
- `0103_content_items_orchestration_ownership.sql` thêm session provenance cùng generation/ownership;
  `0104` tạo index cleanup riêng cho các cột mới.
- `0103_content_items_orchestration_ownership.sql` thêm nullable
  `orchestration_plan_generation`, `orchestration_ownership_claimed_at`,
  `orchestration_ownership_claimed_by`; backfill generation `0` cho session cũ; check provenance;
  FK `(tenant_id, orchestration_session_id)` -> `agent_sessions` với `NO ACTION`.
- `0104_content_items_orchestration_ownership_index.sql` thay index cũ bằng
  `IX_content_items_orchestration_cleanup` trên tenant/session/generation/status/claim time.

`AgentSession.ReplanCount` là generation bền: plan đầu là `0`, mỗi replan được chấp nhận tăng đúng một
lần trong `ApplyReplan`. `ContentItem.Create` nhận session và generation cùng lúc hoặc không nhận cái
nào; `ContentItem` giữ `OrchestrationOwnershipClaimedAt/By` để provenance không bị xóa khi người dùng
sửa body, assets, chọn hook hoặc review lại.

Nối dây generation theo chuỗi `AutonomousOrchestrator` -> `WorkerRunContext` -> `ToolContext` ->
`ContentGenerateTool` -> `ContentItem.Create`. Không đụng `agent_traces`, không parse
`[tool_results]` — cách đó mong manh và không chịu nổi truncate của run summary.

### FIX-4 (CAO) — Plan fail thì bài của plan đó chuyển "Từ chối"

Đây là yêu cầu trực tiếp của khách. Hai điểm móc trong `AutonomousOrchestrator`:

| Điểm móc | Dòng | Ý nghĩa |
|---|---|---|
| **Ngay sau** `ReplanAsync` + `PersistPlanAsync` thành công | `AutonomousOrchestrator.cs:196-198` | Plan A đã bị Plan B thay xong — bài của Plan A chính thức mồ côi |
| Mọi nhánh `_sink.FailAsync(...)` | `:124, 180, 186, 203` | Phiên chết hẳn (`cost_cap`, `dependency_blocked`, `max_rounds`, `replan_failed`) |

**Chốt thứ tự: hủy SAU khi replan thành công, không phải trước.** Nếu `ReplanAsync` ném exception
(`:200-205`) thì luồng rơi thẳng vào `FailAsync("replan_failed")` — điểm móc thứ hai vẫn dọn. Đặt sau
replan tránh được kịch bản tệ nhất: hủy sạch bài rồi replan lỗi, phiên chết mà không còn gì cả.

**Hợp đồng trên `IAutonomousRunSink`** tách hai hành vi có generation guard:

- `PersistReplanAndRejectSupersededContentAsync(tenant, session, expectedGeneration, planB, at)`:
  redact/serialize Plan B trước, rồi trong **một EF transaction** kiểm session có đúng generation,
  lưu Plan B, tăng `ReplanCount`, từ chối draft chưa có human takeover của generation cũ, hủy review
  task live của revision hiện tại và ghi audit per-item. Bất kỳ lỗi/concurrency nào rollback cả plan mới
  lẫn cleanup; Plan B không được durable trước cleanup.
- `RejectOrphanedContentAsync(..., expectedGeneration, ...)`: dùng khi session lỗi terminal; chỉ dọn
  generation hiện tại. Mismatch là stale runner `superseded`, không được fail hay complete session mới.

Mọi query background dùng `IgnoreQueryFilters()` **kèm predicate tenant rõ ràng**. Cleanup không nuốt
exception hay trả `0` giả; trường hợp không có row mới trả `0`. Sau commit, orchestrator ghi trace
`content_rejected_orphan` khi có draft bị từ chối.

**Phạm vi hủy** phải thỏa đồng thời: đúng tenant, đúng session, đúng superseded generation,
`Status` thuộc `draft`, `approved` hoặc `scheduled`, và `OrchestrationOwnershipClaimedAt == null`.
`needs_human` vẫn là draft nên bị hủy **trừ khi** người dùng đã tiếp quản. Sửa body/assets, chọn hook
hoặc retry review đánh dấu takeover trong cùng unit of work; agent refine không bao giờ xóa takeover.
`published` và `rejected` giữ nguyên. Schedule pending của item bị hủy cùng transaction; item scheduled
thành rejected. Mọi active publish attempt — bất kể ownership, soft delete hay mutable status — chặn cả
cleanup lẫn terminal session cho tới khi provider request có outcome.

`ContentItem.RejectForOrchestrationFailure(sessionId, generation, at)` guard lại session, generation,
human ownership, actionable state và active publish attempt; nó lưu reason cố định
`orchestration_plan_failed`. `ContentReviewTask.CancelForOrchestrationFailure(at)` hủy task live với
cùng reason machine-readable, không dùng reason `stale_content_revision` sai ngữ nghĩa. Audit per-item
được persist trong transaction cleanup.

**Về mặt UX:** trạng thái `rejected` đã có sẵn nhãn "Từ chối" trong `statusLabel`
(`ContentWorkspacePage.tsx:167-186`) nên FE không cần đổi. Cần kiểm tra bộ lọc trạng thái hàng đợi có
mục tương ứng để khách xem lại được bài bị hủy.

**Hủy là không hoàn tác được — phải nói rõ với khách.** `ReopenAgentReview` ném
`content_final_rejection_requires_new_revision` khi `Status == "rejected"`. Đường phục hồi duy nhất là
"Repurpose" (`POST /api/content/items/{id}/repurpose`) để sinh revision mới từ nội dung cũ — nội dung
**không mất**, chỉ là phải tạo bản mới. Nên viết câu này vào thông báo/trace khi tự hủy.

### FIX-5 (TRUNG BÌNH) — Vòng lặp thử lại của reviewer đang tự bịt lối

`ContentReviewer` bắt gần hết exception và trả `needs_human` / `reviewer_error` thay vì ném
(`:178-184`, `:434-440`). Nghĩa là mỗi lần lỗi hạ tầng LLM đều **đốt một lượt** trong
`MaxAgentReviewAttempts = 5` và khi hết thì bài rơi thẳng vào hàng chờ người — kể cả khi nguyên nhân
chỉ là LLM tạm thời không sẵn sàng.

Log agent service (`src/agents/Clawbot.AgentService/logs/agent-20260808.log`) lặp lại:

```
[WRN] LLM config fallback agent reviewer-agent tenant "c28c58f3-...":
      bound config "ae1aa695-..." inactive/missing, using active config "d95289b1-..."
```

Không chí mạng (có fallback) nhưng cho thấy binding LLM của `reviewer-agent` đang trỏ vào config đã
tắt. Hai việc:

1. **Vận hành:** sửa binding `reviewer-agent` sang config đang active trên DB khách, để hết cảnh báo
   và tránh phụ thuộc nhánh fallback.
2. **Code:** phân biệt lỗi hạ tầng (`reviewer_unavailable`, `review_timeout`) với verdict thật.
   Lỗi hạ tầng nên `ReleaseForRetry` với backoff mà **không** tăng `AgentReviewAttemptCount`, chỉ
   tăng `AttemptCount` của task. Verdict thật (`passed`/`rejected`/`needs_human` do nội dung) mới
   tính lượt.

Ưu tiên sau FIX-1..FIX-4: đây là tối ưu độ bền, không phải nguyên nhân khách đang gặp.

### FIX-6 (TRUNG BÌNH) — Chặn cả lớp lỗi "500 do thiếu query parameter"

`BadHttpRequestException` hiện thành 500 vì `AddProblemDetails()` + `UseExceptionHandler()`
(`Program.cs:44, 300`). Đây là lý do những 500 khó hiểu ở nhiều màn hình, không riêng Nội dung.

Thêm handler trong pipeline exception, đặt **trước** handler mặc định:

```csharp
// Tham số query thiếu/sai kiểu là lỗi của client -> 400 kèm mã máy đọc được,
// không phải 500 "hệ thống đang gặp sự cố".
if (exception is BadHttpRequestException badRequest)
    return Results.Json(new { code = "request.invalid_parameter", errorCode = "request.invalid_parameter",
                              message = badRequest.Message, requestId = http.TraceIdentifier },
                        statusCode: StatusCodes.Status400BadRequest);
```

Song song, rà toàn bộ `[FromQuery]` kiểu **không nullable, không default** trong `src/api` và đổi
thành nullable + default. Đã sửa `ChainMetricsAsync`; ít nhất KB versions endpoint
(`includeRollbackTarget`) còn dính.

### FIX-7 (THẤP) — Nút "Xóa" trong bảng thao tác

Hiện tại nút "Xóa" ở `ContentWorkspacePage.tsx:1124-1127` dùng `variant="ghost"` nên chìm hẳn so với
các nút còn lại, dù `DeleteItemAsync` (`ContentEndpoints.cs:643-660`) là thao tác **duy nhất không bị
khóa** khi bài kẹt review — tức là lối thoát duy nhất mà người dùng lại khó thấy nhất.

Đổi sang variant cảnh báo (giữ `disabled={acting || publishedLocked}`), giữ icon material
`delete`. Theo quy ước dự án: **không dùng emoji**, chỉ material icon trung tính.

Sau FIX-1..FIX-4 thì nút này không còn là lối thoát duy nhất nữa, nên để mức thấp.

---

## 4. File SQL cần thêm

| File | Loại | Nội dung | Ghi chú |
|---|---|---|---|
| `deploy/repair_stuck_content_review.sql` | vá dữ liệu | chuyển pending draft có task terminal sang `needs_human` | FIX-0; chạy tay sau migration, trước worker mới |
| `deploy/migrations/0101_content_review_task_cycle.sql` | schema | thêm `review_cycle` cho audit retry | uniqueness task row vẫn giữ nguyên |
| `deploy/migrations/0102_agent_sessions_tenant_key.sql` | schema | unique tenant/session key cho foreign-key provenance | FIX-3a |
| `deploy/migrations/0103_content_items_orchestration_ownership.sql` | schema | session/generation provenance và human ownership | FIX-3a |
| `deploy/migrations/0104_content_items_orchestration_ownership_index.sql` | schema | index truy vấn cleanup generation | **Phải là file riêng** |
| `deploy/migrations/0105_agent_sessions_pending_terminal_intent.sql` | schema | lưu durable intent `cancelling`/`failing` khi provider attempt chưa có outcome | không terminalize nhầm transmission đang bay |
| `deploy/migrations/0106_agent_sessions_pending_terminal_intent_index.sql` | schema | filtered index cho terminal intent chờ finalizer | **Phải là file riêng** |
| `deploy/migrations/0107_agent_schedules_initiator_user.sql` | schema | lưu người khởi tạo lịch để authorize mỗi recurrence | lịch legacy thiếu actor fail-closed |
| `deploy/migrations/0108_agent_schedules_initiator_user_index.sql` | schema | index cho initiator lịch | **Phải là file riêng** |
| `deploy/migrations/0109_agent_schedule_runs_initiator_user.sql` | schema | lưu actor effective cho từng schedule run | audit/authorization không phụ thuộc lịch bị sửa sau đó |

Ràng buộc chung (memory `clawbot-migration-no-go`, `run-all-skips-migration-replay`):

- Mỗi file migration chạy trong **một** `SqlCommand` — **không có `GO`**
- Index trên cột vừa `ALTER ADD` phải nằm ở file kế tiếp
- Đặt file vào `deploy/migrations/` là đủ: `run-all.bat` áp migration còn thiếu qua ledger
  `schema_migrations` (`apply-migrations.ps1`)
- File `repair_*.sql` **không** vào `deploy/migrations/` — nó là vá dữ liệu một lần, chạy tay

---

## 5. Thứ tự triển khai

**Giai đoạn 0 — ship cái đã có (không viết thêm code)**
1. Review lại toàn bộ diff chưa commit ở mục 2
2. `dotnet build` (nhớ: NuGetAudit + CA analyzer là **error**, Gateway net10 / còn lại net8, SDK ghim
   bởi `global.json` — memory `clawbot-build-gates`)
3. `pnpm lint` + `pnpm tsc --noEmit` cho `clawbot-web`
4. Commit theo conventional commit **tiếng Anh** (memory `commit-messages-in-english`)

Sau giai đoạn này: hết kẹt "Chờ agent review" vĩnh viễn, hết "Thông tin gửi lên chưa hợp lệ" vô nghĩa
(đổi thành thông báo nói rõ lý do). Lỗi 500 retry **vẫn còn**.

**Giai đoạn 1 — chặn lỗi 500 và gỡ dữ liệu kẹt (FIX-0, FIX-1, FIX-2)**

5. Ship retry mở lại task bền tại chỗ và terminalization của worker
6. Áp các migration còn thiếu, rồi chạy `deploy/repair_stuck_content_review.sql` trên DB khách (backup trước)
7. Restart API/AgentService để worker phiên bản mới nhận việc
8. Xác nhận trên DB khách: `SELECT publication_paused FROM dbo.content_workflow_runtime_gate WHERE id = 1`
   phải bằng `0` (mục 1.4)
9. Chạy lại repair script; kết quả phải là 0 dòng sửa (idempotent)

Sau giai đoạn này khách hết cả ba triệu chứng đang chặn: bài không kẹt nữa, retry không 500 nữa, lên
lịch và đăng chạy được. **Đây là mốc có thể báo khách.**

**Giai đoạn 2 — provenance + tự hủy bài mồ côi (FIX-3, FIX-4)**
10. `0101`–`0104`, domain + FK/index mapping
11. `ReplanCount` generation -> `WorkerRunContext` -> `ToolContext` -> all content mutation tools
12. SQL Server `UPDLOCK, HOLDLOCK` fence: generation/current-running validation + content mutation in one short transaction
13. Atomic replan persistence/cleanup and `FailAndRejectOrphanedContentAsync`; coordinator fences stale unclaimed items

**Giai đoạn 3 — độ bền và dọn dẹp (FIX-5, FIX-6, FIX-7)**
13. Sửa binding LLM `reviewer-agent` (vận hành) + tách lỗi hạ tầng khỏi verdict
14. Handler `BadHttpRequestException` -> 400 + rà `[FromQuery]`
15. Nút "Xóa"

---

## 6. Kiểm thử

Dự án có các test project .NET hiện hành cho Agent, Infrastructure và API; test mới cho invariants domain
nằm trong `Clawbot.Agents.Tests`. Ngoài unit/integration tests, vẫn cần kiểm chứng thủ công + E2E Playwright
cho provider boundary và hành vi giao diện.

**Kịch bản 0 — vá dữ liệu kẹt (FIX-0)** — chạy trên bản sao DB khách trước khi làm thật
1. Đếm trước: `SELECT COUNT(*) FROM content_items WHERE deleted_at IS NULL AND status='draft' AND agent_review_status='pending'`
2. Chạy bước 1 + bước 2 của repair script
3. Đếm lại — số bài kẹt phải giảm; số còn lại phải khớp đúng danh sách bước 3 trả về (bài chưa từng
   có review task, thuộc diện khác)
4. Chạy lại toàn bộ script lần hai -> **không** thay đổi thêm dòng nào (idempotent)
5. Kiểm tra mỗi `(tenant_id, content_item_id, content_revision)` có đúng một review task; không được
   có duplicate live hay lịch sử.

**Kịch bản A — vòng đời review (FIX-1, FIX-2)**
1. Tạo bài mới qua `content_agent`, để review fail đủ 5 lượt (tắt LLM config của reviewer)
2. Xác nhận bài chuyển `needs_human`, **không** còn kẹt "Chờ agent review"
3. Bấm "Thử agent review lại" -> **200**, không 500; cùng `content_review_tasks.id` mở lại thành
   `pending`, `review_cycle` tăng một, và có manual-retry audit event mới
4. Hoàn tất review lại -> completed audit event của cycle mới có event key khác cycle trước; không lỗi unique
5. Retry idempotent cho task pending đến hạn cũng persist human takeover trước khi trả 200; cleanup orchestration
   sau đó không được hủy draft này
6. Bấm lại trong cooldown -> 429; trong lúc worker lease -> 409; không bao giờ 500
7. Hai request retry đồng thời vẫn để một task row và một cycle mới nhất nhất quán.

**Kịch bản B — lên lịch (FIX-1 gián tiếp)**
5. Bài đã review pass -> "Duyệt phát hành" -> "Đổi lịch" -> chọn thời điểm riêng -> **201**
6. Bài chưa review xong -> nút lịch **disabled** kèm Alert nói rõ lý do (không còn "Thông tin gửi lên
   chưa hợp lệ")
7. Đợi `content-publish-due` (cron `*/5`) -> schedule chuyển `posted`, `post_url` có giá trị

**Kịch bản C — plan fail (FIX-3, FIX-4)**
8. Chạy một goal khiến task sau `content_agent` fail (ví dụ agent thiếu grant tool)
9. Trước replan: bài ABC hiện ở "Chờ agent review"
10. Sau replan: Plan B persistence, generation increment và ABC của generation cũ chuyển **"Từ chối"**
    cùng một transaction; XYZ của Plan B xuất hiện bình thường và không thể bị cleanup A đụng tới
11. **Test hồi quy quan trọng:** bài `approved`/`scheduled` chưa human takeover là output Plan A và phải
    bị từ chối, đồng thời cancel schedule pending; chỉ bài đã `published`, có publish attempt active, hoặc
    người dùng sửa body/assets, chọn hook hay retry review mới phải **giữ nguyên**
12. Bài `needs_human` chưa takeover của generation cũ cũng chuyển "Từ chối"
13. Bài khác session hoặc khác generation không bị đụng tới; stale runner nhận `superseded` và không thể
    complete/fail generation mới
14. Trace có `content_rejected_orphan` khi có row bị hủy và audit per-item có generation + reason

**Kịch bản D — 500 do query parameter (FIX-6)**
15. Gửi một `[FromQuery]` primitive không parse được -> **400** với
    `errorCode = request.invalid_parameter`, không lộ raw binding message hoặc stack trace

---

## 7. Rủi ro và điểm cần quyết định

| Rủi ro | Mức | Xử lý |
|---|---|---|
| Retry audit key bị dùng lại ở cycle mới | Cao | `review_cycle` tăng khi reopen; started/completed/stale/manual-retry event keys và payload mang cycle |
| FIX-4 hủy nhầm bài người đã duyệt | Cao | Lọc cứng draft + session/generation/takeover + guard domain; kịch bản C-11 |
| FIX-4 hủy bài người đang sửa dở | Cao | Sửa body/assets, chọn hook và retry review persist human takeover cùng unit of work; cleanup loại các row này |
| Replan lẽ ra tái dùng được bài của plan trước | Trung bình | Hiện `ReplanAsync` sinh plan mới từ goal, `ToAgentTask` chỉ bơm `upstream_results` trong phạm vi plan hiện tại — bài của plan cũ vốn đã mồ côi. Nếu sau này thay đổi, phải xem lại FIX-4 |
| `ToolContext` đổi chữ ký làm hỏng call-site khác | Thấp | Chỉ có **một** chỗ dựng: `GenericLlmAgentWorker.cs:85`. Thêm tham số ở cuối kèm default |
| Bài cũ trước provenance | Trung bình | Session NULL không bị cleanup; session cũ được backfill generation 0 trước FK/check constraint |
| `MarkAgentReviewExhausted` + FIX-0 đẩy hàng loạt bài sang `needs_human` ngay sau deploy | Trung bình | Đúng thiết kế (fail-closed) nhưng phải **báo trước cho khách**: hàng chờ người sẽ đầy lên một đợt, đó là các bài trước đây vô hình |

---

## 8. Các quyết định đã chốt

Bốn điểm dưới đây trước đó còn mở. Chốt luôn để không chặn thi công; mỗi điểm kèm lý do và phương án
đã loại.

### QĐ-1: FIX-4 kích hoạt ở CẢ hai điểm — mỗi lần replan VÀ khi phiên chết hẳn

Phương án loại: chỉ hủy khi cả phiên kết thúc fail.

Lý do quyết định: nếu chỉ hủy lúc phiên chết, thì phiên nào **replan rồi thành công** sẽ để lại bài
của Plan A lơ lửng trong hàng chờ mãi mãi — tức là tái tạo đúng cái bug khách đang gặp, chỉ khác
nguyên nhân. Mà đó lại là kịch bản khách mô tả nguyên văn (Plan A fail, Plan B chạy tiếp, ABC kẹt).
Hủy ở cả hai điểm là **idempotent** (sau lần đầu, bài đã `rejected` nên lần sau lọc không thấy) nên
không có chi phí gì khi gọi thừa.

Đi kèm: Plan B persistence, tăng generation và cleanup Plan A commit cùng transaction; không tồn tại
khoảng crash giữa ba bước.

### QĐ-2: Bài `needs_human` do cạn lượt review CŨNG bị hủy nếu chưa có human takeover

Phương án loại: giữ lại bài `needs_human` cho người xem.

Lý do quyết định: `MarkAgentReviewExhausted` không đổi `Status`, nên bài `needs_human` vẫn là
`draft` — muốn giữ chúng lại thì phải thêm điều kiện review vào bộ lọc, và bộ lọc đó lập tức khó
kiểm chứng (phải suy luận qua ba cột `AgentReviewStatus`, `AgentReviewedRevision`, `Status`). Đổi lại
được gì? Được một hàng chờ đầy bài của một kế hoạch đã bị thay thế — đúng thứ nhiễu mà khách đang
phàn nàn.

Giữ rule draft cho nội dung chưa ai tiếp quản, nhưng lấy `OrchestrationOwnershipClaimedAt` làm rào chắn
bắt buộc cho công việc con người. Đây là trade-off đúng hơn việc tự động hủy nội dung người đang sửa.

### QĐ-3: Thứ tự triển khai — FIX-0 trước FIX-1, và Giai đoạn 1 là mốc báo khách

Phương án loại: gộp repair script vào migration `0101`.

Lý do quyết định: `0101` chạy qua ledger `schema_migrations` nên chỉ chạy **một lần**; nếu nhét vá dữ
liệu vào đó thì lần sau có bài kẹt mới sẽ không chạy lại được. Tách ra thành `repair_*.sql` cho phép
chạy lại bất cứ lúc nào — đúng quy ước 5 file `deploy/repair_*.sql` đang có.

`0101` chỉ bổ sung `review_cycle` với default/check constraint; nó không thay đổi unique invariant hiện có.
Repair vẫn chạy sau migration và trước worker mới để đưa các item lịch sử có task terminal nhưng review
status còn pending về `needs_human`, rồi có thể chạy lại idempotent khi cần.

### QĐ-4: Không đổi publish policy/provider/hold flow; bắt buộc thêm publication fencing

Phương án loại: thêm cơ chế cưỡng bức đăng khi schedule bị `held` hoặc để replan/fail/cancel chạy qua
một provider request đang in-flight.

Lý do quyết định: `ResolvePublishHoldReason` fail-closed vẫn là thứ ngăn bài chưa duyệt lọt ra Facebook.
Bài không đăng được ban đầu là hệ quả của review kẹt, không phải lý do để mở đường vòng publish. Tuy
nhiên, một session lock chỉ an toàn khi publisher claim cùng lock/generation validation và mọi replan,
fail, cancel bị chặn khi có `ActivePublishAttemptId`. Human takeover và soft delete cũng phải bị từ chối
trong lúc attempt active, để state mutable không che giấu provider call.

Việc cần làm ở đường publish là giữ fencing này cùng kiểm tra vận hành `publication_paused = 0`; không
đổi approval policy, credentials, provider request hay hold reason.

---

## 9. Cập nhật thực thi và kiểm chứng

### 9.1 Đã triển khai thêm sau kế hoạch gốc

1. **Terminal intent có thể khôi phục:** `AgentSession` lưu generation/thời điểm/lý do chờ kết thúc;
   `AutonomousRunSink` chỉ finalization khi không còn `ActivePublishAttemptId`, và worker định kỳ xử lý
   từng candidate độc lập. Các trạng thái trung gian `cancelling`/`failing` vẫn được FE polling.
2. **RPC identity fail-closed:** API mint JWT nội bộ ngắn hạn cho AgentService bằng signing key riêng,
   issuer/audience/client-id cố định; gRPC authorize claim đã xác thực và current RBAC thay vì tin
   tenant/user trong request. API key không được gọi orchestration vì không đủ human identity.
3. **Internal transport and key hardening:** mọi môi trường ngoài Development bắt buộc AgentService gRPC
   HTTPS; API chỉ cho HTTP loopback trong Development. API validate hostname, TLS Server Authentication EKU
   cùng chain root/private-intermediate mounted read-only. Signing key phải là Base64 ≥32 byte; public JWT
   key tối thiểu 32 UTF-8 bytes; API/startup plus go-live readiness so sánh raw key material để cấm reuse.
   Workflow production upload rồi chạy strict, non-report-only preflight trên host trước installer/candidate;
   parser chỉ đọc dotenv, không source/execute protected environment files. Preflight xác minh PFX có một
   private key dùng được, full PFX chain và AgentService image user `app` có thể đọc bind mount.
4. **Schedule authority theo thời điểm chạy:** schedule và schedule run persist initiator. Mỗi run resolve
   quyền hiện tại; thiếu, inactive, cross-tenant hoặc mất `orchestration:run` đều fail run/session trước
   khi orchestration hoặc trend scan có side effect.
5. **Reaper tuân thủ provider fence:** stale run/session không bulk-update `AgentSession`; đều đi qua
   `FailAndRejectOrphanedContentAsync` nên provider transmission đang active chuyển sang durable intent.
6. **DI và test seam:** `IAutonomousOrchestrator`, `IAgentScheduleLeaseProvider` và
   `IOrchestratorPermissionResolver` tách runner khỏi concrete orchestrator, SQL Server applock, và
   gRPC transport. Production registrations dùng cùng scoped instance của caller authorizer/resolver.

### 9.2 Regression coverage đã thêm

- `AgentSessionLifecycleTests`: terminal intent, pause/fail lifecycle và giới hạn lý do `NVARCHAR(1024)`.
- `AutonomousRunSinkTerminalIntentTests`: cancel/fail/replan có active publication, generation/ETag fence,
  và settlement sau provider outcome.
- `AgentScheduleRunnerInitiatorAuthorizationTests`: database-backed assertions chứng minh run/session được
  persist terminal khi initiator thiếu, inactive, revoked hoặc mismatch; chứng minh trend scan/orchestrator
  không bị gọi khi unauthorized, và permission được refresh/forward riêng cho từng recurrence.
- `AgentScheduleTests`, `AgentServiceTokenIssuerTests` và `AgentServiceTransportSecurityTests`:
  persistence initiator, JWT internal claim/issuer/audience, raw-key reuse guard, non-Development HTTPS guard,
  certificate-only CA PEM load được, certificate chỉ có Client Authentication bị từ chối, và private root →
  presented intermediate → server chain được custom-root handler chấp nhận.

### 9.3 Kết quả kiểm chứng local

- `dotnet build Clawbot.sln --no-restore -c Release`: pass, 0 warning/error.
- `dotnet test Clawbot.sln --no-build -c Release`: 240 passed; 1 SQL Server-only test skipped.
- `npm run lint`: 0 error; còn 3 warning `react-hooks/exhaustive-deps` đã tồn tại ở Agent run/Pixel office.
- `npm run build`: pass.
- `npm run test:e2e:post-performance`: 2 passed (7.6s) khi chạy bằng Node 20.17.0. Node 25.6.1 hiện tại treo trong Playwright 1.52 ESM loader trước test discovery; CI đã pin Node 20.x, nên local cần dùng Node 20.19+ hoặc Node 22 LTS thật trước khi chạy Playwright.

### 9.4 Điều kiện vận hành còn lại

1. Chạy `0101`–`0109` qua migration ledger; **không** chạy bằng tay từng câu SQL và **không** thêm `GO`.
2. Backup rồi chạy `deploy/repair_stuck_content_review.sql`; chạy lần hai phải báo 0 row.
3. Set `AgentServiceAuthentication__SigningKey` bằng Base64 secret riêng ≥32 byte (không dùng public JWT
   key) cho API và AgentService. Production phải mount AgentService PFX cùng private-CA PEM read-only qua
   `AGENT_SERVICE_TLS_*`; host deploy phải có `pwsh` để workflow chặn release khi preflight certificate
   strict thất bại. `run-all.bat --dry-run` giữ HTTP local-only.
4. Xác nhận `content_workflow_runtime_gate.publication_paused = 0`, active LLM binding của reviewer, và
   chỉ sau đó mới restart/deploy theo quy trình vận hành được phê duyệt.
