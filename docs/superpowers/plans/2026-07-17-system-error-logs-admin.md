# Nhật ký quản trị hữu ích: bắt trọn lỗi hệ thống (HTTP 4xx/5xx, exception, job fail)

- Ngày: 2026-07-17
- Trạng thái: IMPLEMENTED 2026-07-17 — P1–P3 + retention (P4.1) xong; P4.2 rollup chart 200 và P4.3 gateway bỏ qua theo QĐ
- Phạm vi: Clawbot.Api, Clawbot.AgentService, Clawbot.Infrastructure, clawbot-web (trang `/system`)

## 1. Hiện trạng (kết quả rà soát)

### 1.1. "Nhật ký quản trị" hôm nay là gì

- Trang `/system` (`AdminConsolePage`) tab `audit` → component `AdminAuditTab` → `GET /api/admin/audit-logs` → bảng `audit_logs`.
- Bảng `audit_logs` được ghi bởi `AuditSaveChangesInterceptor` (`src/shared/Clawbot.Infrastructure/Audit/AuditSaveChangesInterceptor.cs`): interceptor EF SaveChanges ghi **mọi entity** Added/Modified/Deleted khi có tenant context, với `action = create|update|delete`, `resourceType = tên class CLR`, diff JSON đã PII-redact.
- Hệ quả đúng như phản ánh: nhật ký đầy các dòng kỹ thuật vô nghĩa với người vận hành — `MetaOAuthState` (row state tạm của OAuth flow, sống vài phút), `SocialCredential` update (refresh token), `ProcessedMessage`, v.v. Đây là audit trail CRUD, **không phải log lỗi**.

### 1.2. UI hiển thị thô

`AdminAuditTab.tsx`:
- "Đối tượng" = `resourceType` + 8 ký tự đầu của id ghép liền → ra chuỗi khó hiểu kiểu `MetaOAuthState48a93d2f`.
- Cột "Thay đổi" render chữ cứng `"Đã ghi nhận thay đổi"` thay vì nội dung diff.
- Không có filter (action/resourceType/thời gian — backend đã hỗ trợ nhưng UI không dùng), không hiển thị người thao tác (endpoint không select `UserId` dù entity có cột).

### 1.3. Lỗi hệ thống hiện KHÔNG được lưu vào DB ở bất kỳ đâu

| Nguồn lỗi | Hiện tại đi đâu | Xem được từ UI? |
|---|---|---|
| Unhandled exception trong API (500) | Serilog Console + file `logs/api-.log` (giữ 14 ngày). Không có `UseExceptionHandler` → response 500 rỗng | Không |
| HTTP 4xx (validation, 401/403, 404, 422...) | `UseSerilogRequestLogging()` ghi console/file | Không |
| gRPC AgentService lỗi | Console + file `logs/agent-*.log` | Không |
| Hangfire job fail (hết retry) | `JobFailureNotificationFilter` bắn notification cho admin; stack trace chỉ có trong Hangfire dashboard `/hangfire` | Một phần (notification, không có lịch sử/stack) |
| Background job (bảng `background_jobs` / JobRunner) | Row status failed + notification | Một phần |
| RabbitMQ consumer / polling service lỗi | Serilog console/file | Không |
| Gateway (401 JWT reject, 429 rate-limit) | Serilog console gateway | Không |

Các endpoint đã có convention trả lỗi `{ errorCode, message, requestId = HttpContext.TraceIdentifier }` (AdsEndpoints, ContentEndpoints, AdminMetaIntegrationEndpoints...) — nhưng `requestId` đó **không tra cứu được** ở đâu vì log không persist.

### 1.4. Vấn đề phụ phát hiện trong lúc rà soát

- `GET /api/admin/audit-logs` chỉ `RequireAuthorization()` — **mọi user đăng nhập** của tenant đọc được toàn bộ audit (diff dữ liệu). Nên gate permission.
- `AuditSaveChangesInterceptor` chạy PII-redact trên mọi string property của mọi entity mỗi lần SaveChanges — các entity kỹ thuật (MetaOAuthState...) vừa tạo rác vừa tốn CPU vô ích.

### 1.5. Hạ tầng sẵn có tận dụng được

- Serilog 4 (`Serilog.AspNetCore` 8.0.3) ở cả 3 host, `ReadFrom.Configuration` — Serilog 4 có sẵn batching (`IBatchedLogEventSink` + `BatchingOptions`), không cần package mới.
- `KeysetQuery` cursor pagination (BE) + `useInfiniteList` (FE) — pattern chuẩn của repo.
- `RequirePermission(...)` + `RbacSeeder` (đã có `system:config` cho Admin).
- `RegexPiiRedactor` (singleton, rẻ) — Infrastructure đã reference namespace này.
- Hangfire + `HangfireModule.ScheduleClawbotJobs` để đăng ký job retention.
- Convention lỗi `{errorCode, message, requestId}` — tận dụng làm khoá tra cứu (`trace_id`).

## 2. Mục tiêu / Không làm

### Mục tiêu

1. Mọi lỗi hệ thống (HTTP 4xx/5xx, unhandled exception, job fail, consumer fail, Warning+ từ mọi background service) được **persist vào DB** và xem được từ trang Nhật ký quản trị, có filter + chi tiết stack trace.
2. `requestId` mà FE nhận trong error response tra cứu ra đúng dòng log tương ứng.
3. Tab audit hiện tại bớt rác (bỏ entity kỹ thuật), hiển thị tiếng Việt dễ hiểu, có người thao tác + diff.
4. Nắm được cả lưu lượng 200 ở dạng thống kê (đếm theo giờ), không ghi từng dòng.

### Không làm (non-goals)

- Không dựng ELK/Seq/Grafana — self-host đơn giản, DB là đủ cho quy mô hiện tại.
- Không log request/response body (PII + phình DB).
- Không thay thế Hangfire dashboard (link sang khi cần).
- Không đụng pipeline OTel hiện có (`AddClawbotTelemetry`).

## 3. Kiến trúc đề xuất

Nguyên tắc: **Serilog là bus trung tâm** — mọi lỗi ở mọi host đều đã (hoặc sẽ) chảy qua Serilog. Thêm 1 sink DB batched nhận event `Warning+` là bắt trọn, không phải đi sửa từng chỗ phát sinh lỗi.

```
┌───────────────── Clawbot.Api ─────────────────┐
│ Request → LogEnrichmentMiddleware (TenantId,  │
│           UserId, TraceId vào LogContext)     │
│         → UseSerilogRequestLogging            │
│             GetLevel: 5xx/ex → Error          │
│                       4xx    → Warning        │
│                       2xx/3xx→ Information    │
│         → GlobalExceptionHandler (IException  │
│           Handler): LogError + trả            │
│           {errorCode, message, requestId}     │
│ Hangfire jobs / hosted services / consumers   │
│   → ILogger như hiện tại                      │
└───────────────┬───────────────────────────────┘
                │ Serilog events
┌───────────── Clawbot.AgentService ────────────┐
│ gRPC unhandled → framework log Error          │
└───────────────┬───────────────────────────────┘
                ▼
   SystemLogSink (IBatchedLogEventSink, Warning+)
   - map event → row, redact PII message
   - SqlBulkCopy batch 200 rows / 2s
   - lỗi sink tự nuốt (SelfLog), queue bounded
                ▼
          bảng dbo.system_logs
                ▼
   GET /api/admin/system-logs (+/{id})  ← perm system.logs
                ▼
   /system → tab "Lỗi hệ thống" (bảng + filter + drawer chi tiết)
```

Ghi chú quan trọng: sink ghi bằng SqlBulkCopy (không qua EF) nên **không** kích hoạt `AuditSaveChangesInterceptor` — không có nguy cơ log đẻ ra audit đẻ ra log.

## 4. Quyết định thiết kế

### QĐ1 — Tự viết sink, không thêm package

Hai lựa chọn:
- (a) `Serilog.Sinks.MSSqlServer`: battle-tested nhưng thêm dependency (NuGetAudit gate), và **không có hook transform message** → không PII-redact được trước khi ghi.
- (b) Tự viết `SystemLogSink : IBatchedLogEventSink` (~150 dòng): Serilog 4 có sẵn batching; SqlBulkCopy có sẵn qua Microsoft.Data.SqlClient (transitively từ EF SqlServer); redact message bằng `RegexPiiRedactor` trước khi ghi.

**Chọn (b)** — lý do quyết định: rule của repo "persisted text derived from customer msgs must be PII-redacted" (log lỗi auto-reply có thể chứa nội dung tin nhắn khách); (a) không đáp ứng được.

### QĐ2 — HTTP nào được persist

- 4xx → `Warning`, 5xx/exception → `Error` qua `GetLevel` của `UseSerilogRequestLogging` → tự vào sink với đủ `StatusCode/Method/Path/Elapsed`.
- 2xx/3xx giữ `Information` → chỉ console/file như hiện tại, **không** vào DB (volume: FE poll, SignalR negotiate... sẽ phình DB vô nghĩa).
- Nhu cầu "nắm cả 200": giải bằng **rollup thống kê theo giờ** (P4) — middleware đếm in-memory theo (status class, giờ), flush định kỳ vào bảng nhỏ, FE vẽ chart 24h/7 ngày. Kèm config `SystemLogs:CaptureAllRequests=true` (default false) để bật ghi từng dòng 2xx khi cần debug ngắn hạn.

### QĐ3 — Chuẩn hoá 500

Thêm `GlobalExceptionHandler : IExceptionHandler` + `app.UseExceptionHandler()`:
- LogError đầy đủ exception (tự chảy vào sink).
- Response body theo convention sẵn có của repo: `{ errorCode: "internal_error", message: "Đã xảy ra lỗi hệ thống, vui lòng thử lại.", requestId }` — không lộ stack/SQL/path (rule security).
- Giữ nguyên `GrpcErrorTranslationMiddleware` (chạy trong pipeline, trước exception handler ngoài cùng).

### QĐ4 — Schema `system_logs`

Không `ITenantOwned` (log ngoài tenant scope vẫn phải ghi được — bài học `hangfire-job-scope-has-no-tenant`); `tenant_id` nullable + filter tường minh trong endpoint.

```sql
CREATE TABLE dbo.system_logs (
    id BIGINT IDENTITY NOT NULL CONSTRAINT pk_system_logs PRIMARY KEY,
    occurred_at DATETIMEOFFSET NOT NULL,
    level NVARCHAR(16) NOT NULL,            -- Warning | Error | Fatal
    source NVARCHAR(32) NOT NULL,           -- api | agent-service (Application property)
    category NVARCHAR(256) NULL,            -- SourceContext (class phát log)
    message NVARCHAR(2048) NOT NULL,        -- rendered, đã PII-redact, cắt 2048
    exception NVARCHAR(MAX) NULL,           -- full stack
    status_code INT NULL,                   -- request log / exception handler
    method NVARCHAR(10) NULL,
    path NVARCHAR(512) NULL,
    elapsed_ms FLOAT NULL,
    trace_id NVARCHAR(64) NULL,             -- HttpContext.TraceIdentifier = requestId FE nhận
    tenant_id UNIQUEIDENTIFIER NULL,
    user_id UNIQUEIDENTIFIER NULL,
    properties NVARCHAR(MAX) NULL           -- JSON các property còn lại
);
CREATE INDEX ix_system_logs_occurred ON dbo.system_logs(occurred_at DESC) INCLUDE (level, tenant_id);
CREATE INDEX ix_system_logs_tenant ON dbo.system_logs(tenant_id, occurred_at DESC);
CREATE INDEX ix_system_logs_trace ON dbo.system_logs(trace_id) WHERE trace_id IS NOT NULL;
```

- PK `BIGINT IDENTITY` (insert-heavy, index gọn) → cần overload `KeysetQuery` cho khoá `long` (hiện chỉ có `(DateTimeOffset, Guid)`).
- EF entity `SystemLogEntry` chỉ để **đọc** (endpoint query); ghi hoàn toàn qua sink.

### QĐ5 — Endpoint đọc + phân quyền

- `GET /api/admin/system-logs`: cursor keyset `(occurred_at, id)`, filter `level`, `statusGroup` (4xx/5xx), `source`, `q` (LIKE trên path/message/category/trace_id), `from/to`. Trả kèm summary đếm nhanh 24h (số Error, số Warning) cho chip UI.
- `GET /api/admin/system-logs/{id}`: chi tiết (exception, properties).
- Tenant scoping: `WHERE tenant_id = @tenant OR tenant_id IS NULL` (log nền không tenant vẫn hiện cho admin).
- Permission mới `system.logs` gán role Admin trong `RbacSeeder`. Đồng thời **gate luôn** `GET /api/admin/audit-logs` bằng permission này (fix 1.4).

### QĐ6 — UI: tab "Lỗi hệ thống" trong AdminConsolePage

- Tab mới cạnh tab audit ở `/system`:
  - Chip tổng quan: lỗi 5xx 24h, cảnh báo 4xx 24h, job fail 24h.
  - Filter bar: mức (Lỗi/Cảnh báo), nguồn (HTTP/Job/Agent), nhóm mã (4xx/5xx), khoảng thời gian, ô tìm kiếm (path/message/requestId).
  - Bảng infinite scroll (`useInfiniteList` + cursor): Thời điểm, Mức (badge màu), Mã, Method + Path (hoặc category với log nền), Thông điệp (truncate), Người dùng.
  - Click row → drawer: message đầy đủ, stack trace (pre, scroll), request info, `trace_id` (nút copy), tenant/user.
- Material icon trung tính, không emoji (theo preference).

### QĐ7 — Làm sạch + nhân hoá tab audit

- Marker interface `IAuditExempt` (đặt ở `Clawbot.SharedKernel.Audit`) — `AuditSaveChangesInterceptor` bỏ qua entity implement nó. Ứng viên gắn: `MetaOAuthState`, `ProcessedMessage`, `RefreshToken`, `BackgroundJob`, `Notification`, `PushSubscription`, outbox MassTransit (danh sách cuối chốt lúc implement bằng cách rà toàn bộ DbSet — nguyên tắc: bảng kỹ thuật/máy tự ghi thì exempt, thao tác người quản trị thì giữ).
- Endpoint audit trả thêm `userId` + `userEmail` (join `users`).
- FE `AdminAuditTab`: map action/resourceType → nhãn tiếng Việt (`SocialCredential` → "Tài khoản kết nối MXH", `create` → "Tạo mới"...; resourceType lạ thì hiện raw), cột "Người thực hiện", filter (hành động, loại đối tượng, thời gian), dialog xem diff (parse `diffJson`, bảng từ → thành) thay cho chữ cứng "Đã ghi nhận thay đổi".

### QĐ8 — Retention

- `LogRetentionJob` (Hangfire daily, ~03:00): xoá batch (`DELETE TOP (5000)` loop) `system_logs` quá `SystemLogs:RetentionDays` (default 30) và `audit_logs` quá `Audit:RetentionDays` (default 180).
- Stack trace giữ nguyên không redact (cần cho debug) — bù bằng retention ngắn + perm-gate Admin.

## 5. Kế hoạch thực hiện theo phase

### Phase 1 — Đường ống bắt lỗi (BE, không UI) ~1 ngày

| # | Việc | File |
|---|---|---|
| 1.1 | Migration tạo bảng | `deploy/migrations/0025_system_logs.sql` (mới; theo format 0021, `IF OBJECT_ID ... BEGIN CREATE TABLE + INDEX END`, không `GO`) |
| 1.2 | Schema cho DB đang tồn tại | Thêm block `IF OBJECT_ID(N'dbo.system_logs') IS NULL CREATE TABLE...` vào `DevDataSeeder.EnsureRuntimeSchemaAsync` **và** đường repair của `run-all.bat` (bài học `run-all-skips-migration-replay` — 3 chỗ: EF model/EnsureCreated cho dev DB mới, runtime repair cho dev DB cũ, migration cho deploy) |
| 1.3 | Entity đọc | `src/shared/Clawbot.Domain/Observability/SystemLogEntry.cs` + DbSet + configuration trong `AppDbContext` (ToTable `system_logs`, key `id` long) |
| 1.4 | Sink | `src/shared/Clawbot.Infrastructure/Observability/SystemLogSink.cs` (`IBatchedLogEventSink`; map event→DataTable, redact message qua `RegexPiiRedactor`, SqlBulkCopy; try/catch → `SelfLog`) + extension `LoggerSinkConfigurationExtensions.SystemLogs(this ..., connString, source)` với `BatchingOptions{ BatchSizeLimit=200, BufferingTimeLimit=2s, QueueLimit=10_000 }`, `restrictedToMinimumLevel: Warning` |
| 1.5 | Wire sink 2 host | `src/api/Clawbot.Api/Program.cs` + `src/agents/Clawbot.AgentService/Program.cs`: `.WriteTo.SystemLogs(ctx.Configuration.GetConnectionString("SqlServer"), "api" / "agent-service")` |
| 1.6 | Enrichment | `src/api/Clawbot.Api/Middleware/LogEnrichmentMiddleware.cs`: sau `UseAuthentication`, `LogContext.PushProperty` TenantId/UserId (từ claims) + TraceId |
| 1.7 | Request level mapping | `UseSerilogRequestLogging(o => { GetLevel = ...; EnrichDiagnosticContext = TraceId/TenantId/UserId })` — 5xx/ex→Error, 4xx→Warning, còn lại Information; đọc config `SystemLogs:CaptureAllRequests` để nâng 2xx→Warning khi bật |
| 1.8 | Exception handler | `src/api/Clawbot.Api/Middleware/GlobalExceptionHandler.cs` (`IExceptionHandler`) + `AddExceptionHandler/UseExceptionHandler` — body `{errorCode:"internal_error", message, requestId}` |
| 1.9 | Config | `appsettings.json`: section `SystemLogs { RetentionDays: 30, CaptureAllRequests: false }` |

Nghiệm thu P1: gọi endpoint ném exception → row Error trong `system_logs` có stack + trace_id khớp `requestId` trong response; gọi 404/422 → row Warning; job Hangfire fail → row Error từ log của Hangfire/filter.

### Phase 2 — API đọc + tab "Lỗi hệ thống" ~1 ngày

| # | Việc | File |
|---|---|---|
| 2.1 | Overload keyset cho `long` | `src/api/Clawbot.Api/Common/Pagination/KeysetQuery.cs` |
| 2.2 | Endpoints | `src/api/Clawbot.Api/Endpoints/AdminSystemLogsEndpoints.cs` (list + detail, filter như QĐ5, summary 24h) — map trong `Program.cs` |
| 2.3 | Permission | `RbacSeeder`: thêm `("system.logs", [Admin])`; gate endpoints mới + `GET /api/admin/audit-logs` |
| 2.4 | FE API | `src/frontend/clawbot-web/src/shared/api/admin.ts`: types `SystemLogEntry`, `listSystemLogs`, `getSystemLog` (cursor envelope — nhớ bài học `list-envelope-change-breaks-fe-cast`: khai đúng shape cursor `{items, nextCursor, total}`) |
| 2.5 | FE tab | `src/frontend/clawbot-web/src/features/admin/AdminSystemLogsTab.tsx` (mới) + wire tab vào `AdminConsolePage.tsx` (queryKey riêng, không trùng key với query thường — bài học `react-query-infinite-vs-regular-samekey`) |

Nghiệm thu P2: admin thấy tab "Lỗi hệ thống", filter chạy, drawer hiện stack, tìm theo requestId ra đúng dòng; user không có perm bị 403.

### Phase 3 — Audit sạch + dễ đọc ~0.5–1 ngày

| # | Việc | File |
|---|---|---|
| 3.1 | `IAuditExempt` + gắn entity kỹ thuật | `src/shared/Clawbot.SharedKernel/Audit/IAuditExempt.cs`; sửa filter trong `AuditSaveChangesInterceptor`; rà DbSet chốt danh sách exempt |
| 3.2 | Audit endpoint trả user | `AdminEndpoints.ListAuditLogsAsync`: select `UserId` + left join `users` lấy email |
| 3.3 | FE audit tab | `AdminAuditTab.tsx`: label map tiếng Việt, cột người thực hiện, filter action/resourceType/thời gian, dialog diff from→to |

Nghiệm thu P3: thao tác OAuth connect không còn đẻ dòng MetaOAuthState; dòng audit đọc hiểu được bằng tiếng Việt kèm người thao tác và diff.

### Phase 4 — Vận hành ~0.5–1 ngày

| # | Việc | File |
|---|---|---|
| 4.1 | Retention | `src/shared/Clawbot.Infrastructure/Jobs/LogRetentionJob.cs` + đăng ký trong `HangfireModule.ScheduleClawbotJobs` |
| 4.2 | Rollup request theo giờ | Middleware đếm in-memory `(status_class → count)` flush mỗi phút vào bảng `request_stats_hourly(tenant_id NULL-able, bucket_hour, status_class, count)` (MERGE); endpoint + chart cột 24h/7d trên tab Lỗi hệ thống (đây là chỗ "nắm 200") |
| 4.3 | (NGOÀI PHẠM VI — đã chốt bỏ qua, xem mục 8.4) Gateway sink | Ghi chú tương lai: nếu sau này cần bắt 401/429 tầng gateway thì dùng chính `SystemLogSink` qua package chung; hiện giữ zero-ref DB (SPEC-11 D4/ADR-007) |
| 4.4 | (Tuỳ chọn) Cảnh báo ngưỡng | Job 5 phút: >N lỗi 5xx/5' → `INotificationPublisher` group_key `system.error_spike` (tái dùng hạ tầng notification sẵn có) |

## 6. Test plan (theo chuẩn repo: xUnit, AAA)

Unit:
- `SystemLogSink`: map LogEvent → row (đủ cột, cắt 2048, redact số điện thoại/email trong message, exception giữ nguyên); batch lỗi DB không throw.
- `GetLevel` mapping: 200→Information, 404→Warning, 500/exception→Error; bật `CaptureAllRequests` → 200→Warning.
- `AuditSaveChangesInterceptor`: entity `IAuditExempt` không sinh audit row; entity thường vẫn sinh.
- `KeysetQuery` overload long: encode/decode + slice.

Integration (WebApplicationFactory):
- Endpoint ném exception → 500 body `{errorCode:"internal_error", requestId}` không chứa stack.
- `GET /api/admin/system-logs`: filter level/status/q, cursor sang trang, tenant khác không thấy log tenant mình; thiếu perm → 403 (lưu ý harness bypass perm — viết test theo cách các perm test hiện có).
- Audit endpoint trả `userEmail`.

FE: kiểm tra thủ công theo checklist nghiệm thu từng phase (repo chưa có bộ test FE cho khu vực admin).

## 7. Rủi ro & giảm thiểu

| Rủi ro | Giảm thiểu |
|---|---|
| Sink ghi DB khi DB down → mất log / treo | `IBatchedLogEventSink` + queue bounded (drop khi đầy), lỗi ghi → `SelfLog`, không bao giờ throw ngược vào request path; console/file vẫn còn nguyên như hiện tại (sink là ADD, không thay) |
| Spam 4xx (bot quét 401/404) phình bảng | Retention 30d + index; gateway đã rate-limit; nếu vẫn ồn thì thêm sampling per-path (ghi nhận, chưa làm) |
| Log chứa PII của khách | Redact message qua `RegexPiiRedactor` ngay trong sink; không log body; stack giữ nguyên nhưng retention ngắn + chỉ Admin có perm |
| Vòng lặp log→audit | Sink ghi SqlBulkCopy ngoài EF → không qua interceptor; `SystemLogEntry` chỉ dùng để đọc |
| DB dev hiện hữu thiếu bảng (EnsureCreated không chạy trên DB có sẵn) | 1.2 — thêm cả 3 chỗ: migration, `EnsureRuntimeSchemaAsync`, repair block run-all.bat |
| Gate perm audit endpoint làm user thường mất quyền xem | Chủ đích (fix lỗ hổng); đã chốt ở mục 8.3 |

## 8. Quyết định đã chốt (user duyệt 2026-07-17)

1. **Request 200: KHÔNG ghi từng dòng.** Chỉ 4xx/5xx persist từng dòng; lưu lượng 200 xem qua chart thống kê theo giờ (P4.2) + toggle `SystemLogs:CaptureAllRequests` (default false) bật tạm khi cần debug.
2. **Retention: system_logs 30 ngày, audit_logs 180 ngày** — đều đọc từ config (`SystemLogs:RetentionDays`, `Audit:RetentionDays`).
3. **Phân quyền: thêm perm `system.logs` chỉ gán role Admin**, và gate luôn `GET /api/admin/audit-logs` bằng perm này — user thường mất quyền xem audit là chủ đích (fix lỗ hổng 1.4).
4. **Gateway: bỏ qua ở giai đoạn này.** Lỗi 401/429 chặn tại gateway không vào DB; giữ nguyên zero-ref DB theo ADR-007. P4.3 chỉ là ghi chú tương lai, không nằm trong phạm vi đợt này.

## 9. Thứ tự triển khai đề xuất

P1 → P2 là lõi (2 ngày, sau P2 là dùng được). P3, P4 độc lập với nhau, làm sau theo độ ưu tiên. Mỗi phase 1 commit riêng, build gates (NuGetAudit + CA analyzers) phải xanh — thiết kế không thêm package mới nên rủi ro gate thấp.
