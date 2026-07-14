# Plan: Nền tảng "chạy ngầm — thông báo — click vào xem trạng thái"

> Yêu cầu gốc: hệ thống chủ yếu để AI agent làm việc, nên MỌI tác vụ agent phải (1) chạy ngầm, không giữ HTTP request; (2) bắn thông báo cho user khi xong/lỗi; (3) user click thông báo là vào được trang trạng thái của chính công việc đó.
>
> Nguồn: rà soát nền tảng 2026-07-13 (endpoints + Hangfire jobs + notification stack + FE topbar).

---

## 0. Phát hiện quan trọng nhất

**Hạ tầng đã có đủ mảnh, nhưng chỉ orchestration dùng đúng.** Không thiếu công nghệ, thiếu *một khuôn chung* và thiếu *một run record chung*.

Đã có sẵn (KHÔNG dựng lại):
- Hangfire (SqlServer storage, 5 queue: `default`, `retention`, `kpi`, `content`, `ads`) — `HangfireModule.cs:11-35`.
- `INotificationPublisher` + `NotificationRequest(TenantId, UserId, Type, Title, Severity, Body, Link)` — `INotificationPublisher.cs:4-12`. Entity `Notification` **đã có cột `Link`** (Notification.cs:16).
- 2 publisher: `DbNotificationPublisher` (API, persist + SignalR) và `RedisBridgeNotificationPublisher` (AgentService) → `RedisNotificationRelay` (BackgroundService trong API) đẩy lại xuống hub. Đường cross-host đã thông.
- FE: chuông + badge unread + realtime hook mount toàn cục + toast + trang `/notifications` (Topbar.tsx:44-53, NotificationsPage.tsx).
- Khuôn chuẩn duy nhất: `POST /orchestration/runs` trả `sessionId` ngay (OrchestrationV2Endpoints.cs:106-121) → chạy ngầm → `AutonomousRunSink.NotifyAsync` bắn notification `Link: /agents/runs/{id}` (AutonomousRunSink.cs:88-110) → FE có trang chi tiết `/agents/runs/:sessionId` (routes.tsx:127).

### Gap
| # | Gap | Anchor |
|---|-----|--------|
| G1 | 9 nhóm endpoint gọi agent **đồng bộ** trong request, không job, không notify, không trang trạng thái | bảng §1.1 |
| G2 | Không có run record chung cho việc user kích. Chỉ `AgentSessions` (orchestration-shaped: Goal/PlanJson/Replan) → không có URL để `Link` trỏ tới | AgentSession.cs:5-22 |
| G3 | Toast bỏ qua `notification.link` — chỉ hiện chữ, không click được | Topbar.tsx:69-78 |
| G4 | ~20 Hangfire job chạy ngầm **im lặng**, kể cả job agent tự đổi tiền quảng cáo | bảng §1.2 |
| G5 | `NotifyTrendScanAsync` chỉ đẩy SignalR, **không persist** notification → user offline lúc quét là mất | PublishingContentNotifier.cs:10-13 |
| G6 | Không có wrapper chuẩn "enqueue + notify start/done/fail" → mỗi job tự publish tay nên sót | — |
| G7 | Notification chỉ có trạng thái cuối, không có tiến độ ("đang chạy 3/7") | Notification.cs |
| G8 | Job fail = im lặng (trừ orchestration). Hangfire retry hết vẫn không ai báo | — |
| G9 | Admin trigger recurring job xong không có phản hồi gì | AdminJobsEndpoints.cs:123-130 |
| G10 | Đóng tab = mất thông báo (không có web push / email fallback) | — |

### 1.1 Endpoint gọi agent đồng bộ (G1)

| Luồng | Anchor | Ước lượng |
|---|---|---|
| Sinh nội dung / image prompt / repurpose | ContentEndpoints.cs:175, :404, :444 | 5-30s |
| Quét trend thủ công | ContentEndpoints.cs:630 | 30s-2ph |
| Sinh tài liệu / **generate-kit** (nhiều doc 1 request) | DocumentsEndpoints.cs:77, :111, :168 | 1-5ph |
| Đánh giá campaign / build lookalike | AdsEndpoints.cs:129, :160 | 5-60s |
| Lead create-with-skills / import.csv | LeadsEndpoints.cs:207, :27 | 5s-nhiều phút |
| KB: sinh test-case, chạy test, classify-upload, upload+embed | KbEndpoints.cs:37-53 | 30s-5ph |
| Report agent gọi trong GET analytics | AnalyticsEndpoints.cs:140 | 5-30s |
| Plan-suggestions (LLM) | OrchestrationV2Endpoints.cs:526 | 10-40s |
| Agent sandbox | AgentsEndpoints.cs:251 | tương tác — GIỮ sync |
| SaleAssist draft / summary | SaleAssistEndpoints.cs:41,66,112 | tương tác — GIỮ sync |

Bằng chứng mức độ: cả API chỉ có **3 chỗ** enqueue Hangfire (InboxEndpoints.cs:246, MetaBusinessIntegrationWebhookEndpoints.cs:157, WebhookEndpoints.cs:72).

### 1.2 Job chạy ngầm nhưng im lặng (G4)

- Có notify: ContentPublish, ContentReviewSla, KbCompression, KnowledgeDistillation, AdsLookalikeRefresh, DailyReport, DailySummary, IdleConversationAlert, CompetitorScan.
- **Không notify**: WeeklyTrendScan (G5), AgentMemoryDistillation, ContactMemoryExtraction, AdsRuleEvaluation, AdsCreativeRotation, AdsRemarketing, AdsDaypartPause/Resume, WeeklyAdsReport, ForecastPrecompute, DailyKpiRollup, LeadFollowUp, DripSequence, OutOfHoursAutoReply, AutoSummary, CommentAutoReply, MetaConnectionHealth, HealthCheck.
- Không cần notify (housekeeping thuần): RetentionPurge, RefreshTokenCleanup.

---

## 2. Thiết kế đích

### 2.1 Quyết định lõi

1. **Một bảng `background_jobs` chung**, KHÔNG tái dùng `AgentSessions`. Lý do: `AgentSession` mang ngữ nghĩa orchestration (Goal, PlanJson, ReplanCount, Traces) và màn `/agents/runs` đã phải lọc bỏ session nội bộ (`chat-reply`, sandbox) cho khỏi nhiễu — nhét thêm "generate content" vào đó là làm bẩn tiếp. Bảng mới nhỏ, 1 mục đích.

2. **Một khuôn duy nhất: `IJobLauncher` + `JobRunner` + `IJobHandler`.** Endpoint không tự enqueue Hangfire nữa, không tự publish notification nữa. Notify start/done/fail do `JobRunner` lo — **một chỗ, không sót** (trả lời G6, G8).

3. **Endpoint nhóm 1 đổi sang `202 Accepted { jobId, statusUrl }`.** Không giữ 2 chế độ sync/async song song (không `?async=true`) — mono-repo, FE + BE deploy cùng nhau, giữ 2 đường là 2x bug.
   Ngoại lệ giữ sync: SaleAssist draft/summary, agent sandbox, chat. Bản chất tương tác, sale cần trả lời trong 3s.

4. **Link đích ưu tiên trang nghiệp vụ, fallback dialog job.** Handler trả `ResultLink` (ví dụ `/content?tab=queue&itemId=...`); notification `Link = job.ResultLink ?? /agents?job={jobId}`. User click là thấy *kết quả*, không phải thấy cái log. Không có trang `/jobs` riêng — job center là **dialog ở `/agents`**, mở bằng query param `?job={id}` nên deep link vẫn chạy (chốt 2026-07-13).

5. **Thông báo kiểu Facebook: mọi thứ vào feed, gom nhóm, không spam push** (chốt 2026-07-13).
   - **Vào feed hết** (trừ housekeeping thuần: retention purge, token cleanup, kpi rollup). Không còn khái niệm "job im lặng".
   - **Gom nhóm bằng `group_key`**: sự kiện cùng nhóm trong cửa sổ thời gian thì **update row cũ** (`count++`, bump `last_occurred_at`, ghi đè `title`), KHÔNG tạo row mới. "AI đã điều chỉnh 5 quảng cáo" — 1 dòng, không phải 5 dòng. Đây là cái chặn spam, không phải cắt bớt sự kiện.
   - **Badge đếm nhóm chưa đọc**, không đếm sự kiện.
   - **Push (toast + web push) tách khỏi feed**: mặc định push cho job do người kích + mọi `severity=warning` (fail). Nhóm máy móc (daypart, drip, creative rotation, comment auto-reply) chỉ vào feed, không push. User chỉnh lại được ở §2.8.
   - Mọi loại đều **vào feed + push khi FAIL** (severity warning) — không cho phép tắt push fail.

6. **Retry: 3 lần, chỉ notify fail ở lần cuối.** `[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = Fail)]`; `JobRunner` đọc `RetryCount` từ `PerformContext` để biết đang ở lần cuối chưa. Không có chuyện user nhận 3 cái thông báo lỗi cho 1 việc.

7. **Queue riêng `ai` cho job LLM.** Job LLM chạy vài phút; `WorkerCount = cores/2` mà dùng chung queue `default` thì 1 lô doc-kit là nghẽn cả retention/kpi. Thêm `ai` vào `QueueNames` (HangfireModule.cs:11) và gắn `[Queue("ai")]` lên `JobRunner`.

8. **Tenant trong job: truyền tường minh, không dựa `ITenantAccessor`.** `ITenantAccessor` là HTTP-scoped (HttpTenantAccessor) — trong Hangfire không có. Handler nhận `TenantId` qua `JobContext` và query bằng `IgnoreQueryFilters().Where(x => x.TenantId == ctx.TenantId)` — đúng khuôn các job hiện có (ContentPublishJob, KbCompressionJob).

9. **`IJobLauncher` chỉ dùng được ở host API.** Hangfire server + client chỉ đăng ký ở API (`Program.cs:39 AddClawbotJobs`); AgentService không cấu hình storage nên **không enqueue được** (đúng như ghi chú trong CommentAutoReplyJob.cs:24). Agent muốn tạo job nền thì đi qua bus/gRPC về API. Không cố mở Hangfire client ở AgentService trong phase này.

### 2.2 Schema `background_jobs`

```sql
CREATE TABLE background_jobs (
    id                UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    tenant_id         UNIQUEIDENTIFIER NOT NULL,
    user_id           UNIQUEIDENTIFIER NULL,        -- người kích; NULL = job hệ thống
    type              NVARCHAR(64)  NOT NULL,       -- "content.generate", "docs.generate-kit", ...
    title             NVARCHAR(200) NOT NULL,       -- hiển thị: "Sinh bài đăng: Khuyến mãi tháng 7"
    status            NVARCHAR(20)  NOT NULL,       -- queued|running|succeeded|failed|cancelled
    progress          INT           NOT NULL DEFAULT 0,   -- 0..100
    progress_note     NVARCHAR(200) NULL,           -- "Đang sinh doc 3/7"
    payload_json      NVARCHAR(MAX) NULL,           -- input, ĐÃ redact PII
    result_link       NVARCHAR(400) NULL,           -- deep link trang nghiệp vụ
    result_summary    NVARCHAR(MAX) NULL,           -- ĐÃ redact PII
    error             NVARCHAR(1000) NULL,          -- message an toàn (không stack trace)
    hangfire_job_id   NVARCHAR(64)  NULL,
    idempotency_key   NVARCHAR(128) NULL,           -- chặn double-submit
    created_at        DATETIMEOFFSET NOT NULL,
    started_at        DATETIMEOFFSET NULL,
    finished_at       DATETIMEOFFSET NULL
);
CREATE INDEX ix_background_jobs_tenant_created ON background_jobs (tenant_id, created_at DESC);
CREATE INDEX ix_background_jobs_tenant_user ON background_jobs (tenant_id, user_id, created_at DESC);
CREATE UNIQUE INDEX ux_background_jobs_idem ON background_jobs (tenant_id, idempotency_key)
    WHERE idempotency_key IS NOT NULL;
```

State machine: `queued → running → (succeeded | failed | cancelled)`. Không quay ngược. Retry Hangfire giữ nguyên `running` (không đá về queued) — retry là chi tiết nội bộ, user không cần thấy.

Retention: `RetentionPurgeJob` xoá job `finished_at < now-90d` (thêm 1 dòng vào job có sẵn).

### 2.3 Khuôn code

```csharp
// SharedKernel — contract, handler nằm ở Infrastructure/Api tuỳ luồng
public sealed record JobContext(Guid JobId, Guid TenantId, Guid? UserId, string PayloadJson, IJobProgress Progress);
public sealed record JobResult(string? ResultLink, string? Summary);

public interface IJobHandler
{
    string Type { get; }                                   // khớp background_jobs.type
    Task<JobResult> RunAsync(JobContext ctx, CancellationToken ct);
}

public interface IJobProgress
{
    Task ReportAsync(int percent, string? note, CancellationToken ct);   // update row + push SignalR
}

public interface IJobLauncher   // chỉ resolve được ở host API
{
    Task<Guid> LaunchAsync(string type, string title, object payload,
        string? idempotencyKey = null, CancellationToken ct = default);
}
```

`JobRunner` (Infrastructure/Jobs) — chỗ DUY NHẤT publish notification cho job:

```csharp
[Queue("ai")]
[AutomaticRetry(Attempts = 3, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
public async Task RunAsync(Guid jobId, PerformContext? perform, CancellationToken ct)
{
    // 1. load row, guard status (đã terminal thì thoát — chống chạy lại sau khi cancel)
    // 2. MarkRunning + push realtime
    // 3. resolve IJobHandler theo type (registry keyed) → RunAsync
    // 4. success: MarkSucceeded(resultLink, summary đã redact) + notify "job_succeeded"
    //    Link = resultLink ?? $"/jobs/{jobId}"
    // 5. fail: nếu là lần retry cuối → MarkFailed + notify severity=warning; chưa cuối thì throw để Hangfire retry
}
```

Handler = **bê nguyên thân hàm endpoint hiện tại vào**, đổi `ITenantAccessor.Require()` thành `ctx.TenantId`. Không viết lại logic agent.

Endpoint sau khi đổi (ví dụ docs):

```csharp
private static async Task<IResult> GenerateKitAsync(GenerateKitRequest body, IJobLauncher jobs,
    ITenantAccessor tenants, HttpContext http, CancellationToken ct)
{
    var jobId = await jobs.LaunchAsync("docs.generate-kit", $"Sinh bộ tài liệu: {body.Name}", body, ct: ct);
    return Results.Accepted($"/api/jobs/{jobId}", new { jobId, statusUrl = $"/jobs/{jobId}" });
}
```

### 2.4 API mới

| Route | Mô tả |
|---|---|
| `GET /api/jobs?status=&type=&mine=true` | danh sách job của tenant (mặc định 50 gần nhất) |
| `GET /api/jobs/{id}` | chi tiết: status, progress, note, result_link, error, timestamps |
| `POST /api/jobs/{id}/cancel` | huỷ (queued → cancelled; running → set cờ, handler check `ct`) |
| `POST /api/jobs/{id}/retry` | tạo job mới cùng payload |
| `GET /api/notifications/preferences` | ma trận per-type: in_app / push / email |
| `PUT /api/notifications/preferences` | cập nhật (upsert từng dòng) |
| `POST /api/push/subscribe` | lưu Web Push subscription (endpoint + p256dh + auth) |
| `DELETE /api/push/subscribe` | gỡ subscription khi logout / user tắt |

Permission mới: `jobs:view`, `jobs:manage` — phải seed vào `RbacSeeder` **và** `role_permissions` (endpoint gated mà không seed = 403 câm). Preferences + push subscribe: chỉ cần đăng nhập (self-scope, không gate permission).

### 2.5 Realtime

Tái dùng `NotificationHub` — thêm 1 method `JobUpdated(jobId, status, progress, note)` push vào group tenant/user. Không dựng hub mới. FE `useJobRealtime(jobId)` invalidate query.

Fallback: FE poll `GET /api/jobs/{id}` mỗi 3s khi job đang `queued|running` và hub rớt (giống cách chuông đã poll 30s — Topbar.tsx:51).

### 2.6 FE — job center = dialog ở `/agents` (không có route `/jobs`)

1. **Toast click được** — `Topbar.tsx:69-78`: `onClick={() => { if (toast.link) navigate(toast.link); markRead(toast.id); setToast(null); }}` + `role="button"` + phím Enter/Space. 5 dòng, vá ngay P0.
2. **Dialog "Việc đang chạy"** mở từ `/agents` (nút cạnh tiêu đề trang) **và** từ badge Topbar. Deep link `?job={id}` → dialog tự mở đúng job. 2 pane:
   - trái: danh sách job (tab Đang chạy / Xong / Lỗi), mỗi dòng: tiêu đề, loại, progress bar, thời gian.
   - phải: chi tiết job đang chọn — status pill, progress + note, nút **Mở kết quả** (`result_link`), **Chạy lại**, **Huỷ**, lỗi nếu có.
3. **Phản hồi tại chỗ khi submit**: nút "Sinh nội dung" bấm xong → 202 → chip "Đang chạy — xem tiến độ" mở dialog. Không spinner khoá màn.
4. **Badge "đang chạy" trên Topbar** cạnh chuông: đếm job `queued|running` của user, click mở dialog.
5. **Feed thông báo kiểu FB** (`/notifications` + dropdown chuông): nhóm gộp hiển thị `count` ("AI đã điều chỉnh 5 quảng cáo"), tab Tất cả / Chưa đọc, mark-read khi click, click → `link`.

### 2.7 Notification: gom nhóm (FB-style)

Sửa entity `Notification` (Notification.cs) — thêm cột:

```sql
ALTER TABLE notifications ADD group_key NVARCHAR(128) NULL;
ALTER TABLE notifications ADD occurrence_count INT NOT NULL DEFAULT 1;
ALTER TABLE notifications ADD last_occurred_at DATETIMEOFFSET NULL;
-- index riêng file (quy ước repo: index trên cột ALTER-added phải ở file migration riêng)
CREATE INDEX ix_notifications_group ON notifications (tenant_id, user_id, group_key, is_read);
```

`INotificationPublisher.PublishAsync` thêm logic gộp: nếu `GroupKey != null` và tồn tại row **chưa đọc** cùng `(tenant, user, group_key)` trong cửa sổ `GroupWindow` (mặc định 24h) → `occurrence_count++`, `last_occurred_at = now`, cập nhật `Title`/`Body` (dùng template đếm), **không** insert row mới; ngược lại insert. Realtime vẫn push để badge/toast cập nhật.

`NotificationRequest` thêm `GroupKey`, `GroupTitleTemplate` (ví dụ `"AI đã điều chỉnh {count} quảng cáo"`). Caller cũ không truyền = giữ nguyên hành vi, không breaking.

Group key mẫu: `ads.daypart:{tenant}:{yyyyMMdd}`, `job.failed:{type}`, `comment.autoreply:{yyyyMMdd}`.

### 2.8 Notification preferences (per-user, per-type)

```sql
CREATE TABLE notification_preferences (
    id         UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    tenant_id  UNIQUEIDENTIFIER NOT NULL,
    user_id    UNIQUEIDENTIFIER NOT NULL,
    type       NVARCHAR(64) NOT NULL,   -- notification type, hoặc "*" = mặc định
    in_app     BIT NOT NULL DEFAULT 1,
    push       BIT NOT NULL DEFAULT 1,
    email      BIT NOT NULL DEFAULT 0,
    updated_at DATETIMEOFFSET NOT NULL
);
CREATE UNIQUE INDEX ux_notification_prefs ON notification_preferences (tenant_id, user_id, type);
```

- Không có row = dùng default theo bảng chính sách trong code (in_app=on; push=on cho job người kích + severity warning; push=off cho nhóm máy móc).
- `in_app=0` → không vào feed. `push=0` → vào feed, không toast/web push.
- **Chốt cứng: `severity=warning` (job fail) luôn push, không cho tắt** — tắt được là AI hỏng mà không ai biết.
- UI: trang `/profile` thêm tab "Thông báo" — bảng type × (Trong ứng dụng / Đẩy / Email).

### 2.9 Web Push

- Service worker `public/sw.js` + VAPID key pair (public key vào env FE, private key vào server secret — **không hardcode**).
- FE: sau đăng nhập, nếu `Notification.permission === "default"` thì hỏi quyền **có ngữ cảnh** (khi user kích job nền đầu tiên, không hỏi ngay lúc login).
- BE: `WebPushSender` implement bằng `WebPush` NuGet (`Lib.Net.Http.WebPush`); `DbNotificationPublisher` sau khi persist → nếu preference `push=1` và tab không mở → gửi web push với `{title, body, url}` = `notification.link`.
- SW `notificationclick` → `clients.openWindow(url)`. Cùng `url` với toast → 1 đường deep link duy nhất.
- Subscription hết hạn (410 Gone từ push service) → xoá row, không retry.
- Email fallback: chỉ cho `job_failed` chưa đọc sau 30 phút (job Hangfire quét) — tận dụng `IEmailSender` có sẵn.

---

## 3. Phase

### P0 — Hạ tầng (không đổi hành vi endpoint nào) — XONG 2026-07-13
Merge được ngay: chưa endpoint nào đổi contract, chỉ thêm nền + vá toast.

- [x] Migration `deploy/migrations/0060_background_jobs.sql` (bảng + 3 index, 1 SqlCommand, không `GO`) + block repair trong `run-all.bat`.
- [x] Domain `BackgroundJob` + `BackgroundJobStatuses` + `BackgroundJobConfiguration` + DbSet. Terminal không quay ngược; `Requeue()` chỉ từ failed/cancelled.
- [x] `IJobHandler` / `IJobLauncher` / `IJobProgress` / `IJobRealtime` / `JobContext` / `JobResult` — `SharedKernel/Jobs/JobContracts.cs`.
- [x] `HangfireJobLauncher` (idempotency key, redact payload) + `JobRunner` (chỗ duy nhất notify) + `DbJobProgress` (ExecuteUpdate, không flush tracking của handler).
- [x] Queue `ai` trong `HangfireModule`; `[Queue("ai")]` + `[AutomaticRetry(3)]` trên JobRunner; chỉ notify lỗi ở lần retry cuối.
- [x] `/api/jobs` (list/get/cancel/retry) + permission `jobs:view` (mọi role) / `jobs:manage` (Admin, SalesLead, Marketer) trong `RbacSeeder.Matrix`.
- [x] `SignalRJobRealtime` push event `job` trên NotificationHub + FE `useJobsRealtime` + poll 3s dự phòng.
- [x] FE `JobCenterDialog` ở `/agents` (`?jobs=open`, deep link `?job={id}`), badge Topbar, **toast click được** (G3).
- [x] Test `JobRunnerTests` (7 test, xanh): succeeded + link, fallback link `/agents?job=`, failed + warning, thiếu handler, job đã huỷ, không chạy lại job đã xong, `Requeue` chỉ từ failed/cancelled.

Còn nợ của P0 (làm cùng P1): handler đầu tiên chưa có nên `IJobHandler` chưa có implementation nào — registry rỗng cho tới khi P1 thêm handler.

### P1 — Chuyển 3 luồng nặng nhất — XONG 2026-07-13
- [x] `docs.generate` + `docs.generate-kit` (`DocsJobHandlers.cs`) — kit báo tiến độ theo từng doc; `ResultLink = /documents`.
- [x] `content.generate` (`ContentGenerateJobHandler.cs`) — `ResultLink = /content?tab=queue&itemId={id}`.
- [x] `kb.test` (`KbTestJobHandler.cs`) — tiến độ theo từng case; `ResultLink = /kb?module={id}`; ghi accuracy lên bản deployed.
- [x] 3 endpoint trả `202 { jobId, statusUrl }`; validate (module/brief/template tồn tại) vẫn chạy đồng bộ nên lỗi nhập liệu vẫn trả 400 ngay.
- [x] FE: `useJobWatcher` + 3 màn (Content / Documents / KB) nhận 202, hiện "đang chạy nền", tự làm mới khi job xong.
- [x] Handler đăng ký trong `Program.cs` (4 dòng `AddScoped<IJobHandler, ...>`).

Đánh đổi đã chấp nhận ở KB: panel test trước đây hiện `passed/total/accuracy%` lấy từ response đồng bộ; giờ hiện `resultSummary` của job (cùng nội dung, dạng câu). Không mất dữ liệu — bảng accuracy vẫn cập nhật như cũ.

### P2 — Phần còn lại nhóm 1 — XONG (một phần) 2026-07-13
- [x] `content.repurpose`, `content.trends-scan` (idempotency key theo tuần: bấm 2 lần trả lại job cũ), `content.image-prompt`.
- [x] `ads.evaluate`, `ads.lookalike`.
- [x] `kb.test-cases-generate`.
- [x] **Sửa lỗi thiết kế P0**: `HangfireJobLauncher` KHÔNG redact PII payload nữa. Payload là input do user nhập và handler dùng để chạy — che hotline trong brief thì bài viết sinh ra mang hotline bị che. Redact vẫn áp cho `result_summary` + `error` (thứ agent sinh ra, chỗ dữ liệu khách thật sự rò).
- [x] Giữ nguyên notification ở `AdsAgentGrpcService.cs:66,149` — kiểm lại thì đó là cảnh báo ngân sách 90% / lookalike rỗng, khác loại với `job_succeeded`, không trùng.

- [x] `kb.classify-upload` — **staging qua object storage** (MinIO/local, `IDocumentStorage` đã có sẵn): endpoint đẩy file lên `kb-uploads/{tenant}/{guid}.ext`, payload job chỉ mang **key**, handler đọc lại file từ storage. File thô KHÔNG nằm trong bảng job. Thêm `IDocumentStorage.ReadAsync` (Minio: `GetObjectAsync`; Local: `File.ReadAllBytes`). Tiến độ theo từng tệp; tệp lỗi liệt kê trong tóm tắt.

**Chưa chuyển, có lý do (không phải quên):**
- `kb.upload-embed` (upload 1 file vào module): việc nặng là *trích text* (docx/pdf), không có LLM — vài giây, giữ đồng bộ.
- `leads.import-csv`: parse + insert thuần DB, không có LLM. Nếu sau này CSV lớn thì dùng đúng khuôn staging của `kb.classify-upload`.
- `leads.create-with-skills`: endpoint trả về chính lead vừa tạo, FE điều hướng ngay vào lead đó. Async đổi hẳn hợp đồng — cần thiết kế lại màn trước.
- `orchestration.plan-suggestions`: kết quả là checklist để user tick ngay trong dialog — tương tác, không phải việc "chạy xong báo sau".
- `analytics.report` (AnalyticsEndpoints.cs:140): nằm trong GET; muốn bỏ LLM khỏi GET phải có bản precompute + nút "Tạo lại" → thiết kế riêng, không nhét vội.

### P5 — Quét lại toàn bộ: MỌI lời gọi LLM đều là job — XONG 2026-07-13
Yêu cầu chốt của user: "phàm là gọi LLM AI thì đưa hết vào job quản lý".

- [x] `IJobHandler.NotifyOnSuccess` (mặc định true). Việc **tương tác** đặt false: job vẫn hiện trong "Việc đang chạy", huỷ được, **lỗi vẫn báo** — chỉ không rung chuông lúc xong (user đang ngồi nhìn màn hình chờ).
- [x] FE hook `useJobRun<T>()`: kích job, chờ, lấy kết quả JSON từ `resultSummary` rồi đổ thẳng vào panel. Kết quả trả qua callback `onResult` (không bắt call site viết effect-rồi-setState — lint React cấm).
- [x] Chuyển sang job: `saleassist.draft` (khoá idempotency theo hội thoại — gõ liên tục dùng lại đúng job đang chạy, không đẻ hàng chục job), `saleassist.summary`, `saleassist.upsell`, `agents.sandbox`, `leads.create-with-skills`, `orchestration.plan-suggestions`.
- [x] Test soi nguồn của sandbox (PII redact + LLM scope) trỏ sang `AgentSandboxJobHandler`; thêm test khoá `NotifyOnSuccess => false`.

**Rà xong, các chỗ còn gọi "agent" mà KHÔNG phải LLM (giữ đồng bộ, đúng):**
- `AnalyticsEndpoints` anomaly/forecast → `ReportAgentRunner` chỉ dùng `IAnomalyDetector` + `IForecaster` (z-score, toán thuần). Không có LLM.
- `saleassist.draft-feedback` → chỉ ghi nhận phản hồi vào DB.

**Cố ý giữ đồng bộ, có lý do:**
- `LlmConfigsEndpoints` test kết nối (ping "reply ok"): chẩn đoán cấu hình, user bấm là phải biết ngay đúng/sai. Đưa vào job thì không còn là kiểm tra kết nối.
- Review-gate trong `content.schedule`/publish: LLM chấm duyệt là **chốt chặn fail-closed** ngay trong hành động, không phải việc chạy nền.
- Chat trả lời khách (widget/consumer): vốn đã nằm ngoài HTTP request, chạy trong consumer bus.

### P2b — Cần thiết kế thêm (chưa làm)
- [ ] Bảng staging cho input lớn/PII (CSV lead, file KB) để `leads.import-csv` + `kb.upload-embed` + `kb.classify-upload` đi được đường job.
- [ ] `leads.create-with-skills` + `orchestration.plan-suggestions` + `analytics.report`: chốt lại UX trước khi chuyển async.

### P3 — Feed kiểu Facebook + hết job im lặng — XONG (phần lõi) 2026-07-13
- [x] Migration `0061_notification_grouping.sql` (3 cột) + `0062_notification_group_index.sql` (index riêng — quy ước cột ALTER) + block repair `run-all.bat`.
- [x] `NotificationGrouping.UpsertAsync` dùng chung cho cả 2 publisher (API SignalR + AgentService Redis bridge): 1 câu `ExecuteUpdate` cộng dồn, `rows==0` mới insert — chống race đẻ 2 dòng.
- [x] `NotificationRequest.GroupKey` (mặc định null = hành vi cũ, không breaking). Payload realtime + FE mang `occurrenceCount`; feed hiện chip "x5".
- [x] `PublishingContentNotifier.NotifyTrendScanAsync` persist notification (G5) — trước chỉ đẩy SignalR, user offline là mất.
- [x] **`JobFailureNotificationFilter`** (Hangfire `IElectStateFilter`): MỌI job hết retry vẫn fail → notification `warning`, `group_key=job.failed:{JobName}` (G8). Bỏ qua `JobRunner` (nó tự báo với tên việc + link riêng).
- [x] Notify: `MetaConnectionHealthJob` (token hỏng → nhắc re-auth, link `/system/channels`), `WeeklyAdsReportJob` (báo cáo tuần), `AdsDaypartPauseJob` (gom nhóm theo ngày).
- [x] Test `NotificationGroupingTests` (4 test): 5 event cùng nhóm = 1 dòng count=5; nhóm đã đọc thì nổi dòng mới; quá 24h thì tách dòng; không có group key thì không bao giờ gộp.

- [x] Nhóm máy móc, gom nhóm (`GroupKey`, cửa sổ 24h tự bó nên không cần hậu tố ngày): `AdsDaypartResumeJob`, `AdsCreativeRotationJob`, `AdsRemarketingJob`, `DripSequenceJob`, `CommentAutoReplyJob`.
- [x] Job tự học: `AgentMemoryDistillationJob` (bài học mới cho reviewer), `ContactMemoryExtractionJob` (gom nhóm — mỗi đêm hàng chục hội thoại).
- [x] Admin trigger recurring job → ghi 1 dòng `background_jobs` (ai bấm, lúc nào) hiện trong dialog Việc đang chạy (G9).

**`ForecastPrecomputeJob` cố ý để im lặng**: nó chỉ hâm nóng cache dự báo, không phải hành động agent tác động ra ngoài — báo cho user là nhiễu thuần tuý. Bất thường KPI đã có `AnomalyAlertJob` báo riêng.

### P4 — Preferences + Web Push + email fallback — XONG 2026-07-13
- [x] Migration `0063_notification_preferences.sql`, `0064_push_subscriptions.sql`, `0065_notification_email_sent.sql` + block repair `run-all.bat`.
- [x] `NotificationDeliveryPolicy` — 1 chỗ quyết định feed/push. Mặc định: mọi thứ vào feed; push bật trừ **nhóm việc máy móc** (ads_daypart, creative_rotation, remarketing, drip_sent, comment_auto_reply, *_memory_learned) — chúng vào feed nhưng không rung chuông.
- [x] **Chốt cứng thực thi bằng code + test**: `severity=warning/error/critical` LUÔN push và LUÔN hiện feed, bỏ qua preferences. `NotificationDeliveryPolicyTests` (4 test) khoá điều này.
- [x] `GET/PUT /api/notifications/preferences` + `GET /api/push/vapid-public-key` + `POST/DELETE /api/push/subscribe`. Self-scope, chỉ cần đăng nhập.
- [x] Feed lọc theo preference: `in_app=0` → loại khỏi `/api/notifications` + unread-count (trừ cảnh báo). Toast lọc bằng cờ `push` trong payload realtime.
- [x] Web Push: `Lib.Net.Http.WebPush` + `WebPushDispatchJob` (Hangfire), enqueue từ **cả 2 đường** — `DbNotificationPublisher` (API) và `RedisNotificationRelay` (thông báo do AgentService sinh, host không có Hangfire client). Subscription hết hạn (404/410) → xoá, không retry. Thiếu VAPID key → job tự thoát, feed + chuông vẫn chạy.
- [x] `public/sw.js`: `push` → showNotification; `notificationclick` → điều hướng tab đang mở, không mở thêm cửa sổ.
- [x] Xin quyền có ngữ cảnh: `enableWebPush()` gọi từ nút trong `/profile`, KHÔNG hỏi lúc login (bị từ chối 1 lần là mất vĩnh viễn).
- [x] `UnreadFailureEmailJob` (recurring `*/15`): cảnh báo `warning` chưa đọc sau 30 phút → email qua `IEmailSender`; cột `email_sent_at` chặn gửi lặp.
- [x] FE `NotificationSettingsCard` ở `/profile`: ma trận loại × (Trong ứng dụng / Đẩy về máy / Email) + nút bật/tắt push trình duyệt.

**VAPID key: KHÔNG bắt buộc để hệ thống chạy.** Thiếu key → `WebPushDispatchJob` thoát sớm, nút bật push trả false; feed + chuông + toast + email fallback vẫn chạy đủ. Dev đã có sẵn cặp key trong `run-all.bat` (cùng chỗ với JWT/Encryption key dev). `appsettings.json` để rỗng.
**Production BẮT BUỘC thay cặp khác**: key trong repo coi như đã lộ — ai có nó gửi được push tới subscriber nếu biết endpoint. Sinh cặp mới, private key đưa qua secret/env, không commit.

## 8. Review 2026-07-13 — 4 lỗi chặn đã vá
1. **DI đệ quy vô hạn**: `AddScoped(sp => sp.GetRequiredService<PushServiceClient>())` bọc quanh chính typed client → stack overflow lúc resolve. Bỏ lớp bọc, gán `DefaultAuthentication` trong job (typed client là transient).
2. **Job scope không có tenant**: `ContentImagePromptService` gọi `ITenantAccessor.Require()` → job `content.image-prompt` fail 100%. Thêm overload nhận `tenantId`. Bẫy chung: global query filter theo tenant làm mọi query `ITenantOwned` **trả rỗng âm thầm** trong job — handler phải `IgnoreQueryFilters()` + tenant tường minh.
3. **Idempotency nuốt lần chạy sau**: key `trends:{tuần}` tái dùng cả job đã xong → bấm "quét lại" nhận job cũ. Sửa: chỉ tái dùng job `queued|running`; unique index đổi thành index thường (migration 0066 + sửa 0060).
4. **Báo lỗi từ lần thử đầu**: `IElectStateFilter` thấy FailedState trước khi AutomaticRetry đổi sang Scheduled. Chuyển sang `IApplyStateFilter`.

Vá thêm sau review: thông báo job fail chỉ gửi **admin** (không broadcast toàn tenant — sale/marketer nhận "ContentPublishJob lỗi" là nhiễu), và chỉ tenant có trong tham số job nếu job mang `tenantId`. Admin-trigger job row nói rõ nó ghi nhận **thao tác kích**, không phải kết quả chạy.

---

## 4. Bẫy đã biết (từ lịch sử repo)

| Bẫy | Cách chặn |
|---|---|
| Migration có `GO` → chạy fail (1 SqlCommand/file) | tách file, không `GO` |
| `run-all.bat` chỉ replay `*.sql` trên DB mới; DB cũ dùng block repair hardcode | thêm `background_jobs` vào block repair |
| Endpoint gated permission mà quên seed `role_permissions` → 403 câm | seed `jobs:view`/`jobs:manage` trong RbacSeeder + migration |
| Text dẫn xuất từ tin khách phải redact PII | redact `payload_json`, `result_summary`, `error` trước khi lưu |
| Hangfire client không có ở AgentService | job launch chỉ từ API; agent đi qua bus/gRPC |
| Notification bắn 2 lần (agent tự publish + JobRunner publish) | P2 gỡ publish tay ở `AdsAgentGrpcService.cs:66,149` |

## 5. Test

- Unit: state machine `BackgroundJob` (không quay ngược từ terminal); `JobRunner` notify đúng 1 lần ở retry cuối, 0 lần ở retry giữa; handler throw → status `failed` + error đã redact.
- Integration: `POST /api/documents/generate-kit` trả 202 + jobId; poll `GET /api/jobs/{id}` tới `succeeded`; notification row tồn tại với `Link` đúng; idempotency key trùng → trả lại job cũ, không tạo 2.
- RBAC: user thiếu `jobs:view` → 403; user tenant A không đọc được job tenant B.
- FE: toast có link → click là navigate (test hiện đang thiếu hoàn toàn).

## 6. Rủi ro

1. **Spam thông báo** — rủi ro số 1. Chặn bằng 2 lớp: `group_key` gộp nhóm (§2.7) + preference tách feed khỏi push (§2.8). Nhóm máy móc mặc định `push=off`.
2. **Nghẽn worker** — job LLM vài phút chiếm worker. → queue `ai` riêng + cân nhắc `WorkerCount` riêng.
3. **Breaking FE** — endpoint đổi sang 202, FE không đổi kịp là hỏng màn. → P1/P2 đi theo từng luồng, BE+FE cùng commit.
4. **Job chạy lại sau khi cancel** — Hangfire retry không biết user đã huỷ. → JobRunner guard status terminal ở bước 1.
5. **Race gộp nhóm** — 2 event cùng group đến song song, cùng đọc "chưa có row" → insert 2 row. → gộp bằng 1 câu `UPDATE ... WHERE group_key=... AND is_read=0 AND last_occurred_at > @window` rồi check rows-affected, chỉ insert khi = 0 (upsert 1 round-trip, không đọc-rồi-ghi).
6. **Web Push permission bị từ chối vĩnh viễn** nếu hỏi sai lúc. → hỏi có ngữ cảnh (P4), không hỏi lúc login.

## 7. Đã chốt (2026-07-13)

1. Job center = **dialog ở `/agents`** (deep link `?job={id}`), KHÔNG có route `/jobs` riêng. Toast click được — bắt buộc.
2. Thông báo **kiểu Facebook**: mọi thứ vào feed, gom nhóm bằng `group_key` + `occurrence_count`, badge đếm nhóm; push tách khỏi feed.
3. `notification_preferences` per-user per-type: **LÀM** (P4).
4. Web Push: **LÀM** (P4) + email fallback cho fail chưa đọc.
