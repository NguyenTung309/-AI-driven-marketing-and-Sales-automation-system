# Kế hoạch sửa code theo `docs/1708/Sửa luồng.docx`

Ngày lập: 2026-08-17. Nhánh hiện tại: `feat/admin-user-role-column` (nên tách nhánh mới từ `main`).
Nguồn yêu cầu: `docs/1708/Sửa luồng.docx` (3 hạng mục). Ảnh minh hoạ: rId6/rId7/rId8 (hạng mục 1),
rId9–rId14 (hạng mục 2), rId15 (hạng mục 2.3).

Tài liệu này là KẾ HOẠCH, chưa sửa code. Mỗi hạng mục có: hiện trạng (đã soi code, có file:line),
quyết định thiết kế, danh sách thay đổi, bẫy đã biết, test.

---

## Hạng mục 1 — "Dừng chờ người sửa" → "Dừng chờ người Duyệt"

### 1.0 Yêu cầu (docx §1, §1.1, §1.2)

- Bỏ ngữ nghĩa cũ của cấu hình "Dừng chờ người sửa" (chỉ dừng khi task LỖI), đổi thành
  "Dừng chờ người Duyệt": **cứ mỗi task chạy xong là dừng lại**, chờ user duyệt hoặc sửa output
  thì task kế tiếp mới chạy.
- Task **fail thì rePlan** (không dừng nữa).
- Thêm 2 nút vào ô khoanh đỏ ở card chi tiết task (ảnh rId7/rId8):
  1. **"Duyệt"** — cho task tiếp theo chạy luôn.
  2. **"Sửa nội dung"** — user sửa output của task VỪA HOÀN TẤT, output mới thành input cho task kế.
- Thêm nút **"Xem system prompt & quy tắc"** để người dùng xem prompt cấu hình, phạm vi công cụ
  và hướng dẫn của agent đang chịu trách nhiệm task đó.

### 1.1 Hiện trạng (đã xác minh)

| Thành phần | Vị trí | Trạng thái |
|---|---|---|
| Hằng số policy | `src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousRunContracts.cs` | `Pause` / `Replan` / `Fail`; `Normalize()` fallback `pause`; `ForSource()` ép `pause → replan` khi `source = schedule` |
| Vòng chạy wave | `src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs:176-312` | `foreach (var task in ready)` (208-245) chạy hết wave rồi mới xét `failed` (247); nhánh pause ở 262-302 |
| Hook tạm dừng | `IAutonomousRunSink.PauseForInterventionAsync(tenantId, sessionId, taskId, reason, expectedGeneration, at, ct)` | dùng lại được nguyên si |
| Can thiệp task | `src/agents/Clawbot.AgentService/Services/OrchestratorGrpcService.cs:379-453` | `InterveneTask` **không hề chặn theo trạng thái task** — `edit_output` trên task `completed` đã chạy được sẵn |
| Resume | cùng file, `Control:320-369` → `StartExecutionAsync` → `RunExistingPlanAsync` | task `completed` không chạy lại (ReadyTasks bỏ qua) |
| Nút hiện có | `src/frontend/clawbot-web/src/features/agents/OrchestrationPanel.tsx:540-612` | chỉ 1 nút "Sửa kết quả bước này", điều kiện `session.status === "paused"` |
| Dialog | `src/frontend/clawbot-web/src/features/agents/TaskInterventionDialog.tsx` | đã có `edit_output` / `retry` / `skip`, `rerunDownstream`, `resumeAfter` |
| Nhãn cấu hình | `src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx:77-90` | `FAILURE_POLICY_OPTIONS` label `"Dừng chờ người sửa"` |
| Lưu policy | `src/api/Clawbot.Api/Endpoints/AdminEndpoints.cs:102-114` + `src/shared/Clawbot.Domain/Tenants/Tenant.cs:91-97` | whitelist 3 giá trị `pause|replan|fail` |

Kết luận: **hạ tầng can thiệp đã đủ**, chỉ thiếu (a) điểm dừng sau mỗi task thành công,
(b) 2 nút FE, (c) đổi nhãn + đổi nhánh xử lý fail.

### 1.2 Quyết định thiết kế

**Giữ nguyên giá trị lưu trong DB là `pause`, chỉ đổi NGỮ NGHĨA + NHÃN.**

Lý do: docx nói "bỏ logic cũ, sửa thành ..." tức thay thế chứ không thêm lựa chọn thứ 4. Giữ chuỗi
`pause` thì không cần migration `tenants.orchestrator_failure_policy`, không phải sửa whitelist ở
`Tenant.cs`/`AdminEndpoints.cs`, không phải backfill dữ liệu tenant cũ. Diff nhỏ nhất, rủi ro thấp nhất.

Phương án thay thế (không chọn): thêm giá trị `approval` làm option thứ 4 + migration đổi mặc định.
Chỉ nên làm nếu sau này khách muốn giữ lại cả hành vi "chỉ dừng khi lỗi".

Hệ quả cần ghi rõ trong tooltip FE: policy này chỉ áp dụng cho run do người bấm; run theo lịch
(`source = schedule`) vẫn bị `ForSource()` ép sang `replan` vì không có ai ngồi duyệt lúc 3h sáng.

### 1.3 Backend

**(a) `AutonomousRunContracts.cs`**
- Đổi comment của `Pause`: "dừng sau MỖI task hoàn tất để người duyệt/sửa; task lỗi → replan".
- Giữ `ForSource()` như cũ (unattended → replan).

**(b) `AutonomousOrchestrator.cs` — điểm dừng sau mỗi task thành công**

Chèn ngay sau khối `PersistPlanAsync` + kiểm tra stop trong vòng `foreach (var task in ready)`
(sau dòng 239, trước khi sang task kế tiếp):

```csharp
// Policy "dừng chờ người duyệt": mỗi task xong là chốt lại để người duyệt/sửa output trước khi
// bước sau tiêu thụ nó. Chỉ dừng khi CÒN việc phía sau — task cuối không cần chờ duyệt vô ích.
if (failurePolicy is OrchestratorFailurePolicies.Pause
    && IsCompleted(plan.Tasks.First(t => t.Id == task.Id).Status)
    && plan.Tasks.Any(t => IsPending(t.Status)))
{
    // PersistPlanAsync đã chạy ở trên → output của task vừa xong không bị mất khi chuyển paused.
    await _sink.PauseForInterventionAsync(..., task.Id, "task_completed_awaiting_approval", planGeneration, _clock.UtcNow, ct);
    return AutonomousRunResult.AwaitingIntervention(replans);
}
```

- Bọc `try/catch` `OrchestrationPlanGenerationMismatchException` → `Failed("superseded")` và
  `OrchestrationSessionNotRunningException` → `Failed("stopped")` **giống hệt** khối 281-299 (tách
  thành hàm dùng chung `PauseAndAwaitAsync(request, plan, taskId, reason, planGeneration, replans, ct)`
  để không lặp code — cả nhánh cũ lẫn nhánh mới gọi chung).
- Vì `return` ngay ở task đầu tiên hoàn tất, wave có `MaxConcurrency > 1` cũng tự động thành **tuần tự**
  đúng như docx yêu cầu; không cần đụng `MaxConcurrency`.

**(c) `AutonomousOrchestrator.cs` — task lỗi thì replan**

Khối 262-302 (`failurePolicy is Pause` → pause) phải **bỏ**: dưới ngữ nghĩa mới, `pause` khi có task
lỗi phải rơi xuống nhánh replan (dòng 314 trở đi, có `MaxRounds` guard + trace `"re-planned"`).
Chỉ cần xoá điều kiện `is Pause` ở 262 và để `Fail` giữ nguyên; `pause` và `replan` cùng đi tiếp
xuống nhánh replan.

Lưu ý chi phí: replan sinh plan mới hoàn toàn nên task đã xong sẽ chạy lại — đây là điều docx yêu cầu,
nhưng phải cảnh báo trên UI (xem 1.4) vì trước đây chính lý do này khiến pause thành mặc định.
`MaxRounds` (mặc định 1) vẫn chặn vòng lặp vô hạn — xem memory `max-rounds-real-cause-wave-vs-replan`
và `replan-loop-bounded-by-sink-generation`.

**(d) Không cần đổi gRPC/proto.** `InterveneTask` đã nhận task `completed`; `Control("resume")` đã đủ
cho nút "Duyệt".

### 1.4 Frontend

**(a) `AgentDashboardPage.tsx:77-90`**
- `FAILURE_POLICY_OPTIONS`: label `"Dừng chờ người sửa"` → `"Dừng chờ người Duyệt"`, value vẫn `pause`.
- `FAILURE_POLICY_NOTICE` cho `pause`: viết lại thành "Mỗi bước chạy xong sẽ dừng để bạn duyệt hoặc sửa
  kết quả trước khi chạy bước kế. Bước lỗi sẽ tự lập kế hoạch lại (tốn thêm chi phí AI)."

**(b) `OrchestrationPanel.tsx:540-612` — 2 nút mới trong card chi tiết task** (đúng ô khoanh đỏ rId7/rId8)

Điều kiện hiển thị: `session.status === "paused"` **và** task này là task đang chờ duyệt
(`task.status === "completed"` và là task mà phiên dừng lại ở đó).

- **"Duyệt"** (primary): gọi thẳng `controlOrchestrationV2Run(sessionId, "resume", plan.etag)` —
  không gọi `interveneOrchestrationV2Task`, nên **0 đồng chi phí AI**.
- **"Sửa nội dung"** (outline): mở `TaskInterventionDialog` với `action = "edit_output"` khoá sẵn
  (ẩn/disable 2 lựa chọn `retry`/`skip` khi task đang ở trạng thái chờ duyệt), `rerunDownstream` mặc
  định **bật** (bước sau chưa chạy nên reset là no-op, nhưng bật sẵn giữ đúng ngữ nghĩa "output mới
  thành input bước kế"), `resumeAfter = true`.
- Giữ nguyên nút "Sửa kết quả bước này" cho trường hợp phiên dừng vì task LỖI ở các policy khác,
  hoặc gộp: khi `task.status === "failed"` thì hiện nút cũ, khi `completed` thì hiện 2 nút mới.
- Cả 2 nút gate bằng `canManage` (`orchestration:manage`) như nút hiện có.
- **"Xem system prompt & quy tắc"** (read-only): dùng endpoint agent catalog có sẵn, chỉ hiện với
  `orchestration:view`; hiển thị `personaPrompt`, allow-list công cụ và hướng dẫn task. Không nhúng HTML
  prompt vào DOM. Đây là quyền xem đã được endpoint bảo vệ sẵn, không cấp quyền sửa (`orchestration:manage`).

**(c) Xác định "task đang chờ duyệt"**

`OrchestrationV2SessionDto` cần biết phiên dừng ở task nào. Kiểm tra trước khi code: nếu DTO đã có
`pausedTaskId`/`interventionTaskId` thì dùng luôn; nếu chưa, suy ra ở FE = task `completed` có
`completedAt` lớn nhất khi `session.status === "paused"` và không có task nào `failed`.
Ưu tiên đọc từ trace `phase = "paused"` nếu panel đã tải trace.

### 1.5 Bẫy đã biết

1. `RunningSessions.ContainsKey` chặn cả `resume` lẫn `intervene` → nếu runner chưa nhả session,
   nút sẽ trả `run_in_progress`. Đã có sẵn, chỉ cần FE hiện lỗi tiếng Việt tử tế.
2. `PersistPlanAsync` tự bỏ qua khi phiên không còn `Running` → **bắt buộc** persist trước khi
   `PauseForInterventionAsync`, đúng thứ tự khối 265-290 hiện tại.
3. `expectedGeneration` sai → `superseded`. Luôn truyền `planGeneration` đang giữ trong vòng lặp.
4. Phiên dừng sau task cuối cùng sẽ kẹt ở `paused` nếu không có guard `Any(IsPending)` — đã xử lý ở 1.3(b).
5. FE `etag` cũ → 409. Sau `resume`/`intervene` phải invalidate query phiên (panel đã làm ở mutation
   `intervene` hiện tại, copy y hệt cho nút "Duyệt").

### 1.6 Test

- `tests/Clawbot.Agents.Tests/Orchestrator/OrchestratorFailurePolicyTests.cs`:
  - `Pause_PausesAfterEachCompletedTask` — plan 3 task tuần tự, sink giả; assert `AwaitingIntervention`
    sau task 1, `PauseForInterventionAsync` nhận đúng `taskId` task 1, task 2 CHƯA chạy.
  - `Pause_DoesNotPauseAfterFinalTask` — plan 1 task → `Completed`.
  - `Pause_FailedTask_TriggersReplan` — task lỗi → có trace `re-planned`, không `AwaitingIntervention`.
  - `Pause_UnattendedSource_StillReplans` — `ForSource("pause","schedule")` → replan.
- Test gRPC: `InterveneTask(action=edit_output)` trên task `completed` + `rerunDownstream=true` →
  plan mới có output đã sửa, task sau về `pending`.
- E2E/thủ công: chạy 1 plan 3 bước với policy mới, bấm "Duyệt" 2 lần, xác nhận không phát sinh chi phí
  LLM ở lần bấm (soi `llm_usages`).

---

## Hạng mục 2 — 3 tab nội dung (docx §2)

### 2.0 Yêu cầu

1. Bài **đã đăng** vẫn hiện ở tab "Lịch xuất bản" với status **"Quá hạn"** → phải biến mất khỏi
   "Lịch xuất bản" và nằm ở "Hiệu quả bài đăng".
2. Tab "Hàng đợi duyệt bài": hoặc ẩn bài đã đăng, **hoặc** giữ lại nhưng status phải = "Đã đăng"
   (khách để mình chọn phương án an toàn/nhanh).
3. Tab "Hiệu quả bài đăng" phải xem được **lượt thích và bình luận** của các bài đã đăng.

### 2.1 Hiện trạng (đã xác minh)

- `ContentEndpoints.CalendarAsync` (`src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs:1421-1450`):
  `Where(s => s.ScheduledAt >= fromValue && s.ScheduledAt < toValue)` — **không lọc status**, trả về
  cả `posted`, `canceled`, `failed`.
- Nhãn "Quá hạn" ở `ContentWorkspacePage.tsx:179-182` chỉ sinh ra khi
  `schedule.status === "pending"` và `ScheduledAt` đã qua. Kết luận quan trọng: bài khách thấy
  "Quá hạn" là **một dòng `content_schedule` còn `pending`** — không phải dòng đã `posted`.
  Tức là ngoài lỗi thiếu filter, còn tồn tại **schedule mồ côi** của bài đã đăng.
- `ContentWorkspacePage.tsx:324-329` — dải lịch cố tình lùi 7 ngày để bài quá hạn/lỗi còn thấy được;
  vì thế mọi rác `pending` cũ đều lọt vào khung nhìn.
- Tab hàng đợi: `QueueAsync` (`ContentEndpoints.cs:279-333`) trả **mọi** item chưa xoá. Nhưng FE đã
  gắn nhãn `"Đã đăng"` cho `published` (`ContentWorkspacePage.tsx:123, 172`) và đã khoá toàn bộ
  hành động: `publishedLocked` (dòng 1000) chặn Lưu/Xoá/HookSwitcher (1102, 1124, 1154), còn
  Duyệt/Từ chối/Chạy lại review/Đổi lịch bị chặn từ server qua `ToDto` (`ContentEndpoints.cs:2044-2059`:
  `CanApprove` yêu cầu `Status == "draft"`, `CanReject` `draft|approved`, `CanRetryReview` loại
  `published`, `CanSchedule` ← `CanScheduleCurrentRevision()` yêu cầu `approved|scheduled`).
- Tab hiệu quả: `BuildPostPerformanceAsync` (`ContentEndpoints.cs:1019-1100+`) lọc
  `Status == posted && PostedAt.HasValue && PostedAt ∈ [from,to) && Platform ∈ {facebook, instagram}`.
  `Posts` đếm cả bài chưa sync engagement; chỉ `Likes/Comments` mới yêu cầu
  `LikeCount.HasValue && CommentCount.HasValue`. Nên tab rỗng ⇔ **không có dòng `posted` nào trong cửa sổ**.
- Cả 2 đường publish đều đóng sổ đúng (item + schedule cùng transaction):
  `ContentPublishJob.cs:335-348, 451-458` và `ContentEndpoints.cs:1831-1836` (reconcile). Không tìm
  thấy đường nào đánh dấu item `published` mà bỏ quên schedule.

### 2.2 Chẩn đoán bắt buộc trước khi sửa

> **Trạng thái 2026-08-17:** server đã chặn lịch `posted`/`canceled` và lịch thuộc content `published`.
> Chưa đọc dữ liệu production trong phiên này; giữ nguyên chẩn đoán 2.2-A/2.2-B và chỉ chạy migration 2.6
> khi truy vấn read-only xác nhận còn lịch mồ côi.

Hai giả thuyết loại trừ nhau, phải chạy SQL trên DB khách trước:

```sql
-- A. Bài item đã published nhưng còn schedule chưa đóng sổ (rác làm lịch hiện "Quá hạn")
SELECT s.id, s.content_item_id, s.status AS schedule_status, i.status AS item_status,
       s.scheduled_at, s.posted_at, s.platform, s.last_error
FROM content_schedule s
JOIN content_items i ON i.id = s.content_item_id
WHERE i.status = 'published' AND s.status NOT IN ('posted','canceled')
ORDER BY s.scheduled_at DESC;

-- B. Bài đăng thật ngoài Facebook nhưng hệ thống chưa từng biết (item chưa published)
SELECT status, COUNT(*) FROM content_schedule GROUP BY status;
SELECT COUNT(*) FROM content_schedule WHERE status='posted' AND posted_at >= DATEADD(day,-30,SYSUTCDATETIME());
```

- Nếu A trả về dòng → đúng là schedule mồ côi: làm 2.3 + 2.6 (migration dọn).
- Nếu B cho thấy `posted = 0` → bài chưa bao giờ đăng qua ClawBot (khách đăng tay trên FB), khi đó
  tab "Hiệu quả bài đăng" rỗng là **đúng dữ liệu**, và phải trả lời khách bằng luồng reconcile
  (`POST /api/content/schedules/{id}/reconcile`) chứ không phải sửa query.

### 2.3 Lịch xuất bản — lọc ở server (fix chính)

`CalendarAsync` (`ContentEndpoints.cs:1436-1439`): thêm điều kiện

```csharp
var schedules = await db.ContentSchedules.AsNoTracking()
    .Where(s => s.ScheduledAt >= fromValue && s.ScheduledAt < toValue
        && s.Status != ContentSchedule.StatusPosted
        && s.Status != ContentSchedule.StatusCanceled)
    .OrderBy(s => s.ScheduledAt)
    .ToListAsync(ct)
```

Và lá chắn thứ hai cho rác dữ liệu: sau khi nạp `itemsById`, loại các schedule mà
`itemsById[s.ContentItemId].Status == "published"` trước khi gọi `BuildCalendarRows`. Lá chắn này
xử lý đúng ca "Quá hạn" khách gặp kể cả khi còn dòng `pending` mồ côi.

Kiểm tra kèm: `BuildCalendarRows` và FE `groupCalendar` (`ContentWorkspacePage.tsx:1255-1351`) không
có chỗ nào đếm tổng dựa trên schedule `posted` (nếu có, sửa cho khớp).

### 2.4 Hàng đợi duyệt bài — **chọn phương án B** (giữ lại, status "Đã đăng")

Lý do chọn (an toàn + nhanh, đúng tiêu chí khách đưa ra):

1. **Đã gần như xong sẵn**: nhãn "Đã đăng" và khoá hành động đã có đủ ở cả FE lẫn server (xem 2.1).
   Chi phí còn lại gần bằng 0.
2. **Ẩn hẳn sẽ làm hỏng 3 luồng đang chạy**: deep-link `?itemId=` (`linkedItem` đang được prepend
   cưỡng bức vào `displayedQueueItems`), link "Xem bài" từ tab lịch/hiệu quả trỏ về hàng đợi, và
   bộ lọc trạng thái "Đã đăng" (`ContentWorkspacePage.tsx:123`) sẽ vĩnh viễn rỗng — tự tạo bug mới.
3. Không đụng API `QueueAsync` ⇒ không ảnh hưởng tool `content.list` của agent và các test hiện có.

Việc cần làm (nhỏ):
- Mặc định bộ lọc trạng thái của tab hàng đợi **không** kéo `published` vào danh sách làm việc:
  khi user chưa chọn filter, gọi `QueueAsync` như cũ nhưng FE lọc bỏ `published` khỏi
  `displayedQueueItems` **trừ khi** (a) user chọn đúng filter "Đã đăng", hoặc (b) item đó là
  `linkedItem` của deep-link. Giữ nguyên hành vi khi user chọn filter.
- Rà lại card `QueueEditor` (`ContentWorkspacePage.tsx:938-1150`): khi `publishedLocked`, hiển thị
  banner "Bài đã đăng — chỉ xem" (dòng 1050 đã có, kiểm tra wording) và bảo đảm badge trạng thái
  lấy từ `workflowState`/`status` ra đúng chữ "Đã đăng".

### 2.5 Hiệu quả bài đăng — hiển thị lượt thích/bình luận

Điều kiện đủ để tab có dữ liệu: có `content_schedule.status = 'posted'` với `posted_at` trong cửa sổ
(mặc định 30 ngày, tối đa 90 — `NormalizePostPerformanceWindowDays`, `ContentEndpoints.cs:1015`).

Việc cần làm:
1. **Nới điều kiện nền tảng nếu cần**: query đang cứng `Platform ∈ {facebook, instagram}`
   (`ContentEndpoints.cs:1028`). Nếu khách đăng Zalo/kênh khác, bài sẽ biến mất khỏi cả lịch (2.3) lẫn
   hiệu quả ⇒ rơi vào lỗ đen. Xử lý: giữ nguyên aggregate FB/IG, nhưng thêm dòng "Không theo dõi được
   tương tác cho nền tảng X (n bài)" thay vì lọc im lặng.
2. **Bảo đảm engagement được sync**: `MetaEngagementSyncJob` chạy `*/15`, chỉ sync khi schedule có
   `PostUrl`/`ExternalPostId` (xem memory `facebook-publish-vs-engagement-split`,
   `fb-engagement-edges-vs-insights-deprecation`). Kiểm tra:
   ```sql
   SELECT id, platform, external_post_id, post_url, like_count, comment_count, engagement_synced_at
   FROM content_schedule WHERE status='posted' ORDER BY posted_at DESC;
   ```
   Nếu `external_post_id` NULL → job không có gì để hỏi Graph API; lỗi nằm ở lúc publish, không phải
   ở tab. Khi đó bổ sung: ghi `ExternalPostId` từ kết quả publish (đã có ở `MarkPosted`), và cho
   `ContentPostPerformanceFreshnessDto` hiện rõ "n bài chưa đồng bộ tương tác".
3. **UI**: `PostPerformancePanel.tsx` đã dựng đủ cột like/comment — chỉ cần bảo đảm state rỗng nói rõ
   lý do ("chưa có bài nào đăng qua hệ thống trong 30 ngày" vs "đã có bài nhưng chưa đồng bộ tương tác")
   thay vì một bảng trắng.

### 2.6 Migration dọn dữ liệu (chỉ khi 2.2-A trả về dòng)

`deploy/migrations/0124_cancel_orphan_schedules_of_published_items.sql`
> (số 0122 đã bị migration `system_prompt`, 0123 đã bị migration reaction breakdown chiếm)
— **một `SqlCommand`/file, tuyệt đối không có `GO`** (memory `clawbot-migration-no-go`):

```sql
UPDATE s
SET s.status = 'canceled',
    s.last_error = 'orphan_schedule_item_already_published',
    s.updated_at = SYSUTCDATETIME()
FROM content_schedule s
JOIN content_items i ON i.id = s.content_item_id
WHERE i.status = 'published'
  AND s.status IN ('pending','held');
```

Không đụng `posted`/`publishing`/`outcome_unknown` (đang có tiến trình hoặc là bản ghi thật).
Nhớ: `run-all.bat` không replay migration cũ (memory `run-all-skips-migration-replay`) — phải chạy
`apply-migrations.ps1` trên DB đích.

### 2.7 Test

- API: `CalendarAsync` — seed 1 schedule `posted` + 1 `canceled` + 1 `pending` mồ côi của item
  `published` + 1 `pending` hợp lệ ⇒ chỉ trả về dòng cuối.
- API: `BuildPostPerformanceAsync` — 2 bài `posted`, 1 bài có like/comment ⇒ `Posts = 2`,
  `SyncedPosts = 1`, freshness `unsynced = 1`.
- FE (vitest/playwright): hàng đợi mặc định không hiện item `published`; chọn filter "Đã đăng" thì hiện,
  và mọi nút hành động đều disabled.
- Regression: `tests/.../ContentScheduleTests` hiện có (reschedule của item đã scheduled) phải còn xanh.

---

## Hạng mục 3 — Siết system prompt cho 9 agent (docx §3)

> **Trạng thái 17/08/2026 — ĐÃ CODE.** Đã làm: `AgentPromptPacks` (nguồn sự thật duy nhất cho cả 3 đường),
> `GenericLlmAgentWorker` nối persona + `roleInstruction` thay vì thay thế, cột
> `agent_definitions.system_prompt` + `system_prompt_version` (migration `0122`), seeder repair có version
> (không ghi đè prompt tenant đã sửa), chuẩn hoá `sale-assist` ↔ `sale-assist-agent`, rubric reviewer mới
> (dùng chung cho `ContentReviewer`), và bối cảnh thương hiệu bơm vào cả 4 bước content chain.
> **Chưa làm (cần môi trường thật):** đo lại tỉ lệ approve/reject 20 bài qua reviewer (3.6) và 1 run
> orchestration đầu-cuối. Hai việc này cần LLM + dữ liệu tenant thật nên để lại cho bước nghiệm thu.
>
> Khác plan gốc ở 3 điểm, đều để giảm rủi ro:
> 1. Migration đánh số `0122` (không phải `0123`) vì `0121` là file cuối hiện có.
> 2. Seeder **chỉ tạo mới** cho tenant mặc định như trước; phần repair (tool grants + prompt pack) chạy cho
>    **mọi tenant đã có row**. Vòng lặp mọi tenant kiểu "tạo nếu thiếu" sẽ tự ý cấp agent cho tenant chưa
>    từng có.
> 3. Thêm bơm `BrandContext` vào `ContentChain` (Plan/Outline/Write/Package). Bài "Cổ Loa" khách báo do
>    chuỗi này viết, không phải do persona của `content-agent`; sửa mỗi persona thì bug vẫn còn.
>    Hợp đồng JSON vẫn nằm cuối prompt nên cổng kiểm từng bước không đổi.

### 3.0 Yêu cầu

- Prompt phải xoay quanh **knowledge domain của Học Bá** (docx §0 "BỐI CẢNH THƯƠNG HIỆU": HSK 1-6,
  các khoá tiếng Trung cho người đi làm, tiếng Trung công xưởng...). Ví dụ khách nêu: content-agent
  viết bài Cổ Loa mà không gắn được nghiệp vụ trung tâm.
- reviewer-agent hiện **reject ~80%** nội dung → phải sửa rubric để hết "reject oan".

> **ĐO THẬT 22/08/2026 trên stack local (11 bài draft, rubric MỚI):** rejected 6, needs_human 3,
> passed 1, reviewer_error 1 → **non-pass 82%**, gần y hệt con số khách báo. **Sửa rubric KHÔNG hạ
> được tỉ lệ.** Soi bài bị reject thì thấy reject là ĐÚNG, không phải oan — ví dụ một bài mở đầu
> "VIỆT NAM TOP 1 THẾ GIỚI VỀ SỐ THÍ SINH HSK 2025... tăng trưởng vượt cả IELTS", toàn claim không
> có gì đối chiếu. Lý do gốc: **KB chỉ có 3 module** (`hoc-phi`, `hop-dong-dao-tao`,
> `quy-trinh-test-dau-vao`) — không chứa số liệu thị trường/khóa học/ưu đãi mà bài marketing viện dẫn.
> Rubric mới chỉ cứu được ca "số liệu ĐÃ có trong KB mà vẫn bị đòi thêm bằng chứng"; nó không thể tạo
> ra bằng chứng khi KB trống về chủ đề đó.
>
> ⇒ Muốn hạ 80% phải làm 2 việc KHÁC, không phải sửa prompt reviewer:
> 1. **Nạp KB** đủ danh mục khóa học, học phí, lịch khai giảng, số liệu được phép trích.
> 2. **Chặn content-agent bịa số liệu ngay từ đầu** — đường ReAct `content-agent` không có cổng G2
>    đối chiếu citation như chuỗi 4 bước; cân nhắc bắt buộc đi qua ContentChain.
- Áp bộ prompt trong docx §0-§9 cho: chat-agent, sale-assist-agent, lead-agent, content-agent,
  research-agent, docs-agent, report-agent, reviewer-agent, orchestrator (+ ghi chú publisher-agent).

### 3.1 Hiện trạng — 3 đường prompt khác nhau

| Đường | Nguồn prompt | File |
|---|---|---|
| Chat trực tiếp / sandbox | `agents.config_json.systemPrompt` | seed ở `RbacSeeder.cs:402-424` (`MergeOrchestrationConfig`, chỉ ghi khi rỗng), dùng ở `ChatAgent.cs:332-343` qua `AgentPromptDefaults.Compose(persona)` |
| Sub-agent trong orchestration | `agent_definitions.PersonaPrompt` | seed ở `DevDataSeeder.cs:419-434`, đọc qua `AgentDefinitionCatalog.cs:20-67` (`Description ← PersonaPrompt`) |
| Mẫu prompt trong code | `AgentPromptDefaults.DefaultFor(code)` | `src/agents/Clawbot.Agents.Core/AgentPromptDefaults.cs:37-70` |

### 3.2 Ba lỗi chặn phải sửa trước khi nhồi prompt mới

**(1) `roleInstruction` THAY THẾ persona (nghiêm trọng nhất).**
`GenericLlmAgentWorker.cs:391-437`, ở cả `BuildSystemPrompt` lẫn `BuildReActSystemPrompt`:

```csharp
sb.AppendLine(string.IsNullOrWhiteSpace(roleInstruction)
    ? definition.Description.Trim()
    : roleInstruction.Trim());
```

⇒ Khi orchestrator sinh `roleInstruction` 1-3 câu ("Quét xu hướng thị trường VN"), **toàn bộ persona
của agent bị vứt bỏ**. Đây chính là lý do content-agent viết bài Cổ Loa không dính gì tới Học Bá.
Sửa: nối chứ không thay —

```csharp
sb.AppendLine(definition.Description.Trim());
if (!string.IsNullOrWhiteSpace(roleInstruction))
{
    sb.AppendLine();
    sb.AppendLine("# Nhiệm vụ cụ thể trong kế hoạch lần này");
    sb.AppendLine(roleInstruction.Trim());
}
```

**(2) `PersonaPrompt` đang bị dùng 2 vai.** `AgentDefinitionCatalog.ToPlannerEntry()` lấy chính
`Description` (= `PersonaPrompt`) làm mô tả năng lực cho planner. Nhồi prompt 40 dòng vào đây sẽ
phình prompt planner và làm planner chọn agent kém đi.
Sửa: tách 2 trường —
- giữ `PersonaPrompt` ngắn (1-3 câu, mô tả năng lực) cho planner;
- thêm cột mới `agent_definitions.system_prompt` (nvarchar(max), NULL) chứa prompt siết đầy đủ;
- `GenericLlmAgentWorker` dùng `SystemPrompt ?? Description`.
Migration `deploy/migrations/0123_agent_definitions_system_prompt.sql` (không `GO`):
`ALTER TABLE agent_definitions ADD system_prompt NVARCHAR(MAX) NULL;` — cột thêm bằng ALTER phải nhớ
quy tắc ở memory `clawbot-migration-no-go`, và nếu có test SQLite viết tay thì cập nhật DDL tương ứng
(memory `tenants-column-breaks-sqlite-test-schema`).

**(3) Seeder không bao giờ sửa persona của tenant đã tồn tại.**
`DevDataSeeder.SeedAgentDefinitionsAsync` (`DevDataSeeder.cs:437-470`) chỉ repair `AllowedToolsJson`,
`continue` với mọi row đã có. Prompt mới sẽ **không** tới được tenant khách.
Sửa: repair thêm `system_prompt` khi giá trị hiện tại rỗng **hoặc** bằng đúng chuỗi seed của phiên bản
trước (versioned: thêm hằng `PromptPackVersion` và cột/`metadata` lưu version đã seed) để không ghi đè
prompt user đã tự sửa.

**(4) Lệch mã agent.** `AgentPromptDefaults.DefaultFor` có case `"sale-assist"`
(`AgentPromptDefaults.cs:42`) nhưng `agent_definitions` dùng mã `"sale-assist-agent"`
(`DevDataSeeder.cs:422`); `publisher-agent` và `reporter-agent` không có case nào → rơi vào default
chung chung. Phải chuẩn hoá bảng ánh xạ mã trước khi seed prompt mới.

### 3.3 Kiến trúc đề xuất

Tạo `src/agents/Clawbot.Agents.Core/AgentPromptPacks.cs`:

- `public const string BrandContext` — nguyên văn docx §0 "BỐI CẢNH THƯƠNG HIỆU" + "GUARDRAILS CHUNG"
  + "XỬ LÝ NGOẠI LỆ CHUNG" (danh mục khoá học HSK 1-6, khoá cho người đi làm, công xưởng...).
- `public static string For(string code)` — trả prompt đầy đủ theo docx §1-§9 cho từng mã.
- `AgentPromptDefaults.Compose(custom)` giữ nguyên vai trò ghép `BaseGuardrail` (khoá) + custom;
  prompt pack đi vào phần custom. Thứ tự cuối cùng gửi LLM:
  `BaseGuardrail` → `BrandContext` → prompt riêng của agent → `roleInstruction` (nếu có).
- `AgentPromptDefaults.DefaultFor(code)` chuyển sang gọi `AgentPromptPacks.For(code)` để **cả 3 đường**
  (chat, sandbox, orchestrator) dùng chung một nguồn sự thật.

Ánh xạ docx → mã trong hệ thống:

| docx | mã `agents` (chat) | mã `agent_definitions` (orchestration) |
|---|---|---|
| §1 chat-agent | `chat-agent` | `chat-agent` |
| §2 sale-assist-agent | `sale-assist` | `sale-assist-agent` |
| §3 lead-agent | `lead-agent` | `lead-agent` |
| §4 content-agent | `content-agent` | `content-agent` |
| §5 research-agent | `research-agent` | `research-agent` |
| §6 docs-agent | `docs-agent` | `docs-agent` |
| §7 report-agent | `report-agent` | `report-agent` |
| §8 reviewer-agent | `reviewer-agent` | `reviewer-agent` |
| §9 orchestrator | `orchestrator` | (không có row) |
| — | — | `publisher-agent`, `reporter-agent` (docx không định nghĩa; giữ persona hiện tại, chỉ thêm BrandContext) |

### 3.4 Điểm cần cẩn thận theo từng agent

- **content-agent (§4)**: docx quy định hợp đồng đầu ra theo 4 bước chain (B1/B2 JSON thuần không
  fence, B3 chỉ thân bài không URL/hashtag, B4 chỉ đóng gói). Chain này đã tồn tại trong code
  (xem memory `content-prompt-chaining-plan`) — prompt mới phải khớp **đúng** contract từng bước,
  nếu không parser sẽ vỡ. Kiểm tra prompt từng bước trong `ContentChain*`/`ContentWriter` trước khi thay.
- **reviewer-agent (§8)**: đây là chỗ chữa lỗi reject 80%. Rubric mới thêm câu chốt:
  "KB evidence là bằng chứng đối chiếu — nếu số liệu trong nội dung ĐÃ KHỚP KB thì KHÔNG được trả
  `needs_human` vì lý do thiếu dữ liệu (cấm reject oan)". Prompt thật đang nằm ở
  `AgentPromptDefaults.cs:61-67` **và** `ContentReviewer.cs:68-86, 451-463`
  (`TrustedSystemInstructions`) — phải sửa **cả hai**, không thể sửa bằng UI/DB.
  Output contract giữ nguyên `{"verdict":"approve|reject|needs_human","reason":"..."}` không fence.
- **orchestrator (§9)**: yêu cầu "roleInstruction phải viết tiếng Việt 1-3 câu" — ghép vào prompt
  planner ở `AgentPromptDefaults`/orchestrator prompt builder. Docx cũng xác nhận không có
  `publisher-agent` độc lập; giữ nguyên hiện trạng (publish do worker durable làm).
- **chat-agent (§1)**: quy tắc xưng hô + cấm mở đầu "Dựa trên..." + cấm nhắc "tài liệu/kho tri thức/AI"
  — trùng một phần với `BaseGuardrail` hiện có, tránh nói 2 lần mâu thuẫn (rà lại
  `AgentPromptDefaults.cs:9-25`).

### 3.5 Đường cập nhật cho tenant đang chạy

1. Migration cột `system_prompt` (3.2-(2)).
2. Seeder repair có version (3.2-(3)) — chạy khi API khởi động.
3. UI `/api/prompts/configs` (`src/frontend/clawbot-web/src/shared/api/prompts.ts`) vẫn cho khách tự
   sửa; prompt seed chỉ ghi khi rỗng/còn nguyên bản cũ.
4. Với `agent_definitions` của tenant prod, nếu không muốn chờ seeder: script SQL cập nhật
   `system_prompt` theo mã (một `SqlCommand`, không `GO`).

### 3.6 Test

- Unit: `AgentPromptPacks.For(code)` trả đúng pack cho 10 mã, mã lạ → pack mặc định có `BrandContext`.
- Unit: `GenericLlmAgentWorker` build prompt với `roleInstruction` không rỗng ⇒ chuỗi kết quả chứa
  **cả** persona lẫn roleInstruction (test hồi quy cho lỗi 3.2-(1)).
- Unit: `AgentDefinitionCatalog.ToPlannerEntry()` vẫn trả mô tả NGẮN (assert độ dài < 400 ký tự) sau
  khi thêm `system_prompt`.
- Kiểm thử thủ công có số liệu: chạy lại 20 bài content qua reviewer trước/sau khi sửa rubric, ghi
  tỉ lệ approve/reject/needs_human vào PR để chứng minh đã hết reject oan.
- Kiểm thử content: 1 run orchestration đầy đủ (research → content → reviewer → publisher), xác nhận
  bài viết có nhắc đúng khoá học trong danh mục và không bịa khoá mới.

---

## Thứ tự thực hiện đề xuất

1. **Hạng mục 3 lỗi (1)** — sửa `roleInstruction` thay persona (1 file, tác động lớn nhất tới chất
   lượng output, độc lập với mọi thứ khác).
2. **Hạng mục 2** — chẩn đoán SQL → filter `CalendarAsync` → migration dọn rác → FE hàng đợi → tab hiệu quả.
3. **Hạng mục 1** — orchestrator pause-after-each + 2 nút FE.
4. **Hạng mục 3 phần còn lại** — prompt pack + cột `system_prompt` + seeder versioned (khối lượng
   soạn thảo lớn nhất, nên làm cuối và review riêng).

## Rủi ro / cần khách xác nhận

- Hạng mục 1: đổi task lỗi sang **replan** sẽ chạy lại các bước đã xong ⇒ **tốn thêm chi phí AI**.
  Đây là điều docx yêu cầu; cần nói rõ lại với khách và giữ `MaxRounds` chặn vòng lặp.
- Hạng mục 2: nếu chẩn đoán 2.2 cho ra kịch bản B (bài đăng tay ngoài FB), phần "Hiệu quả bài đăng"
  không thể tự có dữ liệu — phải hướng dẫn khách dùng luồng reconcile hoặc đăng qua hệ thống.
- Hạng mục 3: prompt mới siết chặt danh mục khoá học ⇒ nếu Học Bá mở khoá mới mà không cập nhật
  `BrandContext`, agent sẽ từ chối tư vấn khoá đó. Nên đưa danh mục vào KB thay vì hard-code nếu
  khách còn đổi danh mục thường xuyên (ghi nhận như bước tiếp theo, không làm trong lần này).
