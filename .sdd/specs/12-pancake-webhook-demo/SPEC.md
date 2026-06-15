# SPEC-12 — Pancake Webhook Demo

**Status:** `DRAFT`
**Spec lead:** P1
**Last updated:** 2026-06-12
**Traces to:** FR-01, UC-A01..A02, SW-011..015

---

## 1. Business Context

Hiện tại Pancake gửi webhook HTTP POST về ClawBot, nhưng chưa có môi trường demo để:
- Kiểm tra luồng webhook từ Pancake đến từng tầng xử lý
- Quan sát JSON payload ở mỗi bước (debug/trace)
- Config access token linh hoạt (UI hoặc env)
- Chạy thử mà không ảnh hưởng production

SPEC này định nghĩa chế độ `DEMO_MODE=true`, hook vào pipeline production hiện tại (không tạo pipeline riêng), thêm trace points ở mỗi tầng, và serve 1 UI minimal + Swagger để debug.

## 2. User Stories

- AS A P1 (developer) I WANT nhập Pancake access token từ UI hoặc .env SO THAT tôi không cần hardcode.
- AS A P1 I WANT gửi tin nhắn từ Zalo và xem JSON format ở từng tầng SO THAT tôi debug được luồng.
- AS A QA (U3) I WANT trace 1 message từ webhook đến reply SO THAT tôi verify agent hoạt động đúng.
- AS A P1 I WANT Swagger UI để test webhook nhanh hơn SO THAT tôi không cần mở UI React.

---

## 3. Kiến trúc & quyết định nền tảng

### Q1: Pipeline riêng hay hook vào flow thật?

**Đáp án: Hook vào pipeline production hiện tại.**

Demo mode thêm trace points vào pipeline có sẵn (YARP -> Ingestor -> Outbox -> Agent -> Outbound), không tạo bản sao. Khi `DEMO_MODE=false` (production), trace points không active.

### Q2: Mục tiêu demo là gì?

**Đáp án: A — Quan sát flow thật 100% production.**

Demo mở trace logging. Agent gọi LLM thật (nếu có key). Outbound gửi Pancake thật (nếu có token). Nếu thiếu key/token, step đó báo `skipped` + lý do, các step khác vẫn chạy.

### Q3: Webhook nhận payload thế nào?

**Đáp án: Nhận payload gốc từ Pancake, không wrapper.**

Endpoint `POST /api/demo/webhook/pancake` nhận body là raw JSON Pancake gửi. `trace_id` do Gateway sinh (X-Trace-Id header). Không được chờ Pancake gửi trace_id.

---

## 4. Acceptance Criteria (EARS)

### 4.1 Webhook endpoint & HMAC

- THE SYSTEM SHALL expose `POST /api/demo/webhook/pancake` accepting Pancake webhook payload (raw body, không wrapper).
- THE SYSTEM SHALL validate HMAC-SHA256 signature from header `X-Pancake-Signature`.
- HMAC secret SHALL be configurable via env `PANCAKE_WEBHOOK_SECRET` (không dùng access token).
- THE SYSTEM SHALL compute HMAC on **raw request body bytes** (not normalized JSON).
- IF `X-Pancake-Signature` header is missing AND `DEMO_MODE=true` AND `DEMO_SKIP_HMAC=true` THEN THE SYSTEM SHALL skip validation (cho phép test webhook từ Postman).
- IF signature header exists but invalid THEN THE SYSTEM SHALL return 401.
- THE SYSTEM SHALL assume Pancake uses **HMAC-SHA256, hex-encoded**, computed over raw body bytes, sent as `X-Pancake-Signature: sha256=<hex>`.
- Pancake webhook secret details SHALL be ghi trong section 12 (cần verify thực tế với Pancake docs).

### 4.2 Trace ID

- THE SYSTEM SHALL generate `X-Trace-Id` at YARP Gateway (Guid/NewId) if the incoming request does not carry one.
- IF a trace ID is already present in request header `X-Trace-Id` THEN THE SYSTEM SHALL reuse it.
- Trace ID SHALL propagate through all layers via `ILogger LogContext` và HTTP/gRPC headers.

### 4.3 Webhook ACK timing + Rabbit publish fail

- THE SYSTEM SHALL ACK the webhook HTTP request **ngay sau khi persist DB outbox** (transactional outbox pattern).
- Outbox rows SHALL be written in the same SQL transaction as the message and conversation data.
- Outbox Worker (background) SHALL poll the `inbox_outbox` table every 500ms, publish to RabbitMQ, then delete the row.
- Total latency budget for HTTP ACK: `<100 ms` (gateway + ingestor + db save).
- IF RabbitMQ is unavailable:
  - Outbox Worker SHALL retry with exponential backoff (1s, 2s, 4s, max 8s)
  - Outbox rows remain in DB with `status='pending'` — không mất message
  - Webhook đã ACK thành công từ trước đó
  - IF Rabbit still down after 5 retries THEN alert via log + Telegram
- Agent và outbound xử lý async sau khi Rabbit đã nhận event.

```
[Sync — HTTP request path]
Pancake -> YARP -> Ingestor -> DB (message + outbox) -> HTTP 200 OK  (<100ms)

[Async — background path]
Outbox Worker (500ms poll) -> RabbitMQ -> Agent -> Outbound -> Pancake API
```

### 4.4 Trace watchdog — chống trace "running" vĩnh viễn

- WHEN a trace is created THE SYSTEM SHALL set `trace.status = 'running'`.
- THE SYSTEM SHALL run a background watchdog every 60 seconds scanning traces with `status = 'running'`.
- IF `trace.created_at + 5 minutes < NOW()` THEN THE SYSTEM SHALL:
  - Mark `trace.status = 'partial'`
  - Append step: `{ "layer": "watchdog", "status": "failed", "reason": "processing_abandoned" }`
  - Log warning with trace_id
- ADDITIONAL — Outbox pending timeout:
  IF a trace has step outbox with status='pending' AND trace.created_at + 10 minutes < NOW()
  THEN watchdog SHALL:
    - Mark trace.status = 'partial'
    - Update outbox step: { 'status': 'failed', 'reason': 'rabbit_publish_timeout' }
    - Log warning with trace_id
  Rationale: Rabbit down lau khien trace running mai. 10 phut > max Rabbit retry (approx 2 phut) — du margin.
- Watchdog SHALL run in demo mode only (`DEMO_MODE=true`).
- Watchdog interval SHALL be configurable via `DEMO_WATCHDOG_INTERVAL_SECONDS` (default 60).

### 4.5 Runtime token update

- Pancake access token SHALL be read at **outbound execution time**, not captured at trace start.
- IF token is changed via `POST /api/demo/config/token` or env restart WHILE a message is being processed THEN the pending outbound call SHALL use the **current runtime token** (latest).
- **Race condition expected behavior:**
  - Request A reads token X, Request B reads token Y after update -> OK, expected
  - Không cần snapshot, không cần version
  - Dev không "fix" thành snapshot token — race là intentional
- Old tokens: không snapshot, không version. Nếu muốn rollback thì restart với env cũ.

### 4.6 SSE replay + Last-Event-ID

- WHEN a client connects to `GET /api/demo/events` THE SYSTEM SHALL:
  1. Replay the last N traces (configurable via `DEMO_SSE_REPLAY_COUNT`, default 10), each with full steps in order
  2. Send `event: replay_done` after replay
  3. Begin streaming live events
- SSE SHALL support `Last-Event-ID` header:
  - IF client sends `Last-Event-ID` THEN replay only events after that ID (not last N traces)
  - Event IDs SHALL be monotonically increasing (Redis INCR or timestamp-nanosecond)
  - IF `Last-Event-ID` is too old (trace expired) THEN full replay (last N traces)
- Reconnect behavior: duplicates allowed on full replay — không cần strict dedup cho demo

### 4.7 Token not configured — không block

- IF Pancake access token is not configured WHEN an inbound webhook arrives THEN:
  - Inbound path (Gateway -> Ingestor -> Bus -> Agent) SHALL run normally
  - Outbound step SHALL log `"skipped": true, "reason": "token_not_configured"` trong trace
  - Agent SHALL still produce draft and log it
  - User sees full trace đến step agent, outbound báo skipped
- This applies in demo mode only.

### 4.8 Trace persistence fail — không block webhook

- IF Redis is unavailable WHEN processing webhook THEN THE SYSTEM SHALL:
  - Continue processing the webhook normally
  - Log warning
  - Return 200 OK with header `X-Trace-Warning: trace_not_persisted`
  - SSE will not receive events for this trace (graceful degradation)

### 4.9 Demo mode security

- WHERE `DEMO_MODE=true` THE SYSTEM SHALL bind demo endpoints **chỉ trên localhost / internal network interface** (127.0.0.1 or 10.x.x.x / 192.168.x.x).
- WHERE `DEMO_ADMIN_KEY` env is set THE SYSTEM SHALL require `Authorization: Bearer {DEMO_ADMIN_KEY}` header for `POST /api/demo/config/token` và `GET /api/demo/traces/*`.
- Default: không set admin key -> chỉ accessible từ localhost.
- Swagger SHALL **hide** the following endpoints from UI:
  - `POST /api/demo/config/token` (tránh accidentally saved token)
  - `POST /api/demo/config/webhook-secret`
  - Các endpoint còn lại visible (GET traces, GET events, GET config/status)
- Alternatively, require `DEMO_ADMIN_KEY` in Swagger "Authorize" button.

### 4.10 PII masking — whitelist approach

CLARIFICATION — Raw payload storage scope:
Raw payload SHALL be stored in Redis trace as-is (full bytes, including PII fields).
PII whitelist applies ONLY to API responses (GET /api/demo/traces, SSE events).
Redis trace record is server-side only — khong expose qua API tru export endpoint.
Export endpoint (GET .../export) SHALL require DEMO_ADMIN_KEY if configured.
Rationale: Raw luu full de debug schema drift. API filter PII ra ngoai.

- THE SYSTEM SHALL use a **whitelist** approach: chỉ những field trong whitelist mới xuất hiện trong trace response.
- Default whitelist: `contact.id`, `contact.name`, `contact.platform`, `message.id`, `message.role`, `message.content_length`, `message.content_truncated`, `source.*`
- All other fields in the raw payload SHALL be **excluded** from trace (not masked, excluded).
- Phone, email, address, note, avatar SHALL NOT appear in trace output.
- Configurable via `DEMO_MASK_PII` (default true). If false, full payload visible.
- Log file vẫn ghi full (file log không phải trace).

### 4.11 Timestamp convention

- All trace timestamps SHALL be stored in **UTC** internally.
- SSE events and REST responses SHALL return ISO 8601 with UTC suffix: `2026-06-12T14:30:00.000Z`.
- Demo UI SHALL convert to `+07:00` for display (client-side).
- Env `TZ` or `DEMO_TIMEZONE` has no effect on stored timestamps.

---

## 5. API Contracts / Data Models

### 5.1 Webhook input (Pancake gửi — raw, không wrapper)

`POST /api/demo/webhook/pancake`

Header: `X-Pancake-Signature: sha256=abc123...`

```json
{
  "thread_id": "zalo-thread-xyz",
  "message": {
    "id": "msg_001",
    "text": "Hoc phi HSK4 bao nhieu?",
    "sent_at": "2026-06-12T14:30:00+07:00",
    "direction": "inbound",
    "attachments": []
  },
  "contact": {
    "id": "contact_456",
    "name": "Nguyen Van A",
    "phone": "0901234567"
  },
  "source": {
    "platform": "zalo",
    "page_id": "zalo-oa-hocba",
    "channel_id": "ch_zalo_001"
  }
}
```

**Raw payload storage:
**Truncation rule:**
Raw payload SHALL be serialized to UTF-8 string first, THEN truncated to 256,000 characters (not bytes).
IF truncation occurs THEN append literal suffix: ...[TRUNCATED] to the stored string.
Stored value is treated as opaque string blob — khong parse lai sau khi truncate.
Export endpoint tra ve blob as-is with field raw_payload_truncated: true neu da truncate.

** Raw body (truncated 256KB) SHALL be stored in trace for schema-drift debugging. Truncation only applies at store time — không ảnh hưởng processing.

**Idempotency key:**
- Primary: `message.id` (if present)
- Fallback: `SHA256(thread_id + "|" + message.sent_at)` — nếu message.id missing
- IF cả 2 đều không xác định được THEN dedup disabled cho message đó (trace log warning)

**Duplicate handling:**
- IF `message.id` exists AND same `message.id` received within 5 minutes:
  - Compare `SHA256(text)` của message cũ và mới
  - IF match -> trace status=`skipped`, reason=`duplicate`, linked_trace=`trc_prev`
  - IF mismatch -> trace status=`completed`, step outbox log `"duplicate_payload_mismatch": true`, linked_trace=`trc_prev`
    -> Cảnh báo provider bug

### 5.2 Gateway trace

Layer: `T-0.5 Gateway`

```json
{
  "trace_id": "trc_abc123",
  "layer": "gateway",
  "status": "success",
  "duration_ms": 8,
  "validation": {
    "hmac_valid": true,
    "hmac_secret_configured": true,
    "rate_limit_remaining": 59
  },
  "headers": {
    "x-trace-id": "trc_abc123"
  }
}
```

### 5.3 Ingestor trace

Layer: `T-1 Backend / Ingestor`

```json
{
  "trace_id": "trc_abc123",
  "layer": "ingestor",
  "status": "success",
  "duration_ms": 45,
  "action": "upsert_conversation",
  "conversation": {
    "id": "conv_789",
    "status": "new",
    "is_first_message": true
  },
  "contact": {
    "id": "contact_456",
    "platform": "zalo"
  },
  "message": {
    "id": "msg_001",
    "role": "contact",
    "content_truncated": true,
    "content_length": 24
  }
}
```

**PII whitelist applied:** contact.phone, contact.email, contact.address, contact.avatar, contact.note SHALL NOT appear.

### 5.4 Outbox trace

Layer: `T-2 Message Bus`

```json
{
  "trace_id": "trc_abc123",
  "layer": "outbox",
  "status": "success",
  "duration_ms": 12,
  "event_type": "inbound_message",
  "event_id": "evt_001",
  "queue": "inbox.ingest",
  "idempotency_key": "msg_001",
  "idempotency_key_source": "message.id",
  "duplicate_payload_mismatch": false
}
```

**Dedup:** Message `id` field used as idempotency key. Fallback: `SHA256(thread_id + sent_at)`. If duplicate found with different text, `duplicate_payload_mismatch: true`.

### 5.5 Agent trace

Layer: `T-3 AgentService`

```json
{
  "trace_id": "trc_abc123",
  "layer": "agent",
  "status": "success",
  "duration_ms": 3200,
  "agent": "AutoReplyAgent",
  "gates": {
    "gate1_lock": { "status": "passed", "locked_by": null },
    "gate2_rag": { "status": "passed", "top_score": 0.92, "path": "A" },
    "gate3_confidence": { "score": 95, "action": "auto_send" }
  },
  "intent": "pricing",
  "rag_chunks": [
    { "chunk_id": "ch_012", "score": 0.92, "source": "KB-gia-HSK4" }
  ],
  "llm": {
    "model": "claude-sonnet-4.6",
    "tier": "large",
    "prompt_tokens": 245,
    "completion_tokens": 89,
    "cost_usd": 0.0018
  },
  "draft_truncated": true,
  "draft_length": 156
}
```

**Notes:** Draft content truncated at 256 chars in trace. `status` values: `success`, `failed`, `skipped` (LLM key missing), `partial` (timeout).

### 5.6 Outbound trace

Layer: `T-6 Outbound`

```json
{
  "trace_id": "trc_abc123",
  "layer": "outbound",
  "status": "success",
  "duration_ms": 340,
  "action": "send_message",
  "token_configured": true,
  "api_call": {
    "method": "POST",
    "url": "https://openapi.pancake.vn/api/v1/messages/send",
    "body_summary": {
      "threadId": "zalo-thread-xyz",
      "text_length": 156,
      "platform": "zalo"
    },
    "polly_retry": 0,
    "status_code": 200
  },
  "replied_at": "2026-06-12T14:30:04.200Z"
}
```

**Khi token không config:**

```json
{
  "trace_id": "trc_abc123",
  "layer": "outbound",
  "status": "skipped",
  "reason": "token_not_configured",
  "token_configured": false,
  "suggested_draft": "Day a, hoc phi HSK4 la 3.900.000 VND..."
}
```

### 5.7 Full trace response

`GET /api/demo/traces/{trace_id}`

```json
{
  "trace_id": "trc_abc123",
  "status": "completed",
  "total_duration_ms": 4200,
  "created_at": "2026-06-12T14:30:00.000Z",
  "completed_at": "2026-06-12T14:30:04.200Z",
  "steps": [
    {
      "layer": "gateway",
      "status": "success",
      "duration_ms": 8,
      "timestamp": "2026-06-12T14:30:00.008Z",
      "output": { "...": "..." }
    },
    {
      "layer": "ingestor",
      "status": "success",
      "duration_ms": 45,
      "timestamp": "2026-06-12T14:30:00.053Z",
      "output": { "...": "..." }
    },
    {
      "layer": "agent",
      "status": "running",
      "duration_ms": null,
      "timestamp": "2026-06-12T14:30:01.000Z",
      "output": { "partial": true, "message": "dang xu ly..." }
    }
  ],
  "errors": []
}
```

**Status values (trace level):**
- `pending` — trace created, chưa có step nào complete
- `running` — đang xử lý, có thể có step partial
- `completed` — tất cả steps done
- `partial` — một số step failed/skipped/include watchdog abandoned

**Status values (step level):**
- `success`, `failed`, `skipped`, `pending`, `running`

### 5.8 SSE endpoint

`GET /api/demo/events`

Support `Last-Event-ID` header for reconnect.

Response format (SSE):

```
event: trace_step
id: 1001
data: {"trace_id":"trc_abc123","layer":"gateway","status":"success","duration_ms":8}

event: trace_step
id: 1002
data: {"trace_id":"trc_abc123","layer":"ingestor","status":"success","duration_ms":45}

event: trace_complete
id: 1005
data: {"trace_id":"trc_abc123","status":"completed","total_duration_ms":4200}
```

On connect:
1. IF `Last-Event-ID` not provided: replay last N traces (`DEMO_SSE_REPLAY_COUNT`, default 10), each with full steps
2. IF `Last-Event-ID` provided: replay events after that ID (if still in Redis)
3. Send `event: replay_done` after replay
4. Begin streaming live events

### 5.9 Download trace endpoint

`GET /api/demo/traces/{trace_id}/export`

Response: `Content-Type: application/json` + `Content-Disposition: attachment; filename="trace_trc_abc123.json"`

Full JSON same as `GET /api/demo/traces/{trace_id}` but with raw payload included (if within size limit). Require `DEMO_ADMIN_KEY` if configured.

---

## 6. Env Config

```env
# === Demo mode ===
DEMO_MODE=true
DEMO_MASK_PII=true
DEMO_SKIP_HMAC=false
DEMO_ADMIN_KEY=
DEMO_SSE_REPLAY_COUNT=10
DEMO_WATCHDOG_INTERVAL_SECONDS=60

# === Token (co the nhap qua UI, env override) ===
PANCAKE_ACCESS_TOKEN=

# === Webhook secret (bat buoc cho HMAC) ===
PANCAKE_WEBHOOK_SECRET=

# === Trace ===
DEMO_TRACE_TTL_MINUTES=60
```

### TTL clamping

`DEMO_TRACE_TTL_MINUTES` SHALL be clamped:
- Minimum: 5 (quá thấp thì trace mất trước khi debug xong)
- Maximum: 1440 (24h — quá cao thì Redis đầy)
- Effective formula: `Clamp(value, 5, 1440)`

### Token lookup order

Token SHALL be resolved in order:
1. Runtime memory (set via `POST /api/demo/config/token` — survives env)
2. `PANCAKE_ACCESS_TOKEN` env var (fallback)
3. If both empty -> outbound step = `skipped` + reason `token_not_configured`

### Timezone

All timestamps stored UTC. UI renders local `+07:00`.

---

## 7. Demo UI

```
+--------------------------------------------------------------+
|  ClawBot Demo - Pancake Webhook                              |
|                                                              |
|  +- Config -------------------------------------------------+ |
|  |  Token:    [______________________] [Luu]  Status: conf  | |
|  |  Secret:   [______________________] [Luu]  Status: conf  | |
|  |  Mask PII: [x]  Skip HMAC: [ ]  Watchdog: [x]           | |
|  |  Admin Key: [****]                                       | |
|  +----------------------------------------------------------+ |
|                                                              |
|  +- Live Log (SSE - auto scroll) -------------------------+ |
|  |  14:30:00 [Gateway]   success (8ms)                    | |
|  |  14:30:00 [Ingestor]  success (45ms)                   | |
|  |  14:30:01 [Outbox]    success (12ms)                   | |
|  |  14:30:04 [Agent]     success - confidence=95          | |
|  |  14:30:04 [Outbound]  success (340ms)                  | |
|  |  ---- Replay: 10 traces loaded ----                    | |
|  +----------------------------------------------------------+ |
|                                                              |
|  +- Trace Detail -----------------------------------------+ |
|  |  trc_abc123  (completed, 4.2s)     [Copy] [Download]  | |
|  |  [Gateway] [Ingestor] [Outbox] [Agent] [Outbound]      | |
|  |     ok         ok        ok       ok        ok          | |
|  |  -- Gateway ---------------------------                | |
|  |  { "hmac_valid": true, "rate_limit": 59 }              | |
|  +----------------------------------------------------------+ |
|                                                              |
|  +- History (last 20 traces) ----------------------------+ |
|  |  14:31  trc_abc124  completed   3.2s  zalo            | |
|  |  14:29  trc_abc123  completed   4.2s  zalo   [view]  | |
|  |  14:28  trc_abc122  partial     -     zalo   [view]  | |
|  |                     ^-- expired (watchdog)            | |
|  +----------------------------------------------------------+ |
+--------------------------------------------------------------+
```

**Trace expired state:** UI SHALL display "Trace expired" badge (không lỗi đỏ) when GET trace returns 404. Backend 404 message: `{ "error": "trace_expired", "detail": "Trace da qua 60 phut va bi xoa khoi Redis" }`.

---

## 8. Technical Constraints

- Demo mode hook vào pipeline production — không tạo pipeline riêng
- Trace lưu Redis với TTL clamp [5, 1440] phút
- SSE thay vì WebSocket (đơn giản, đủ dùng)
- UI React minimal hoặc vanilla JS (1 HTML file)
- Swagger/OpenAPI tự động từ ASP.NET Core (`Swashbuckle` hoặc `NSwag`)
  - **Hide** sensitive endpoints (config/token, config/webhook-secret) từ Swagger UI
  - Require `DEMO_ADMIN_KEY` cho sensitive operations
- Raw payload truncated 256KB — lưu raw luôn để debug schema drift
- PII whitelist approach — chỉ field trong danh sách mới xuất hiện
- Timestamp all UTC — UI render local timezone
- Trace watchdog chạy mỗi 60s, timeout 5 phút

---

## 9. Non-Functional Requirements

| NFR | Yêu cầu |
|---|---|
| NFR-01 | Webhook ACK <100ms (DB sync path) |
| NFR-02 | Trace step log <5ms overhead mỗi tầng |
| NFR-03 | Trace không chứa plaintext token |
| NFR-04 | SSE reconnect: support Last-Event-ID |
| NFR-05 | Watchdog timeout: traces >5 phút -> partial |

---

## 10. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Token missing | Outbound step | Step=`skipped`, reason=`token_not_configured` | Nhập token qua UI hoặc set env |
| HMAC secret missing & not skipped | Gateway | 401 `{ "error": "hmac_secret_not_configured" }` | Set env hoặc `DEMO_SKIP_HMAC=true` |
| HMAC invalid | Gateway | 401 `{ "error": "invalid_signature" }` | Kiểm tra webhook secret |
| Trace expired | `GET /api/demo/traces` | 404 `{ "error": "trace_expired", "detail": "Trace da qua 60 phut" }` | Gửi tin nhắn mới |
| Trace expired (UI) | UI GET 404 | Show badge "Trace expired" (không lỗi đỏ) | Tự động ẩn sau 3s |
| LLM timeout | Agent timeout 15s | Step=`failed`, reason=`llm_timeout`, fallback=template | Thử lại |
| LLM key missing | Agent step | Step=`skipped`, reason=`llm_key_missing` | Config LLM key |
| Redis down | Trace service catch | Webhook OK, header `X-Trace-Warning: trace_not_persisted` | Restart Redis |
| RabbitMQ down | Outbox Worker retry | Webhook OK (DB outbox pending). Trace outbox step=`pending` | Worker retry exponential backoff |
| Pancake API 5xx | Outbound retry hết | Step=`failed`, retry_count=3, last_status=502 | Manual from UI |
| Duplicate message.id same text | Ingestor dedup | Step=`skipped`, reason=`duplicate`, linked_trace=`trc_prev` | Không, dedup đúng |
| Duplicate message.id diff text | Ingestor dedup | Step=`completed`, `duplicate_payload_mismatch`: true -> cảnh báo provider bug | Điều tra provider |
| Payload >256KB | Ingestor truncate | `content_truncated: true`, `content_length: 256000` | Chấp nhận |
| Concurrency ordering | Trace step timestamp | UI sort by timestamp ascending | OK |
| Missing message.id | Ingestor fallback | Idempotency key = SHA256(thread_id + sent_at) | Warning trong trace step |
| Trace running >5 phút | Watchdog scan | Trace status=`partial`, step watchdog `processing_abandoned` | Phục hồi sau restart |
| Outbox pending >10 phut | Watchdog scan | Trace status=partial, outbox step=failed, reason=rabbit_publish_timeout | Kiem tra RabbitMQ |
| UI fetch trace trong lúc TTL expire | UI refresh 404 | Show "Trace expired" badge, không popup lỗi | Tự cleanup |

---

## 11. Files cần tạo/chạm

| File | Mô tả | Chạm vào production? |
|---|---|---|
| `src/api/Clawbot.Api/Endpoints/DemoEndpoints.cs` | Webhook, token, trace, config, export | Chỉ active khi DEMO_MODE=true |
| `src/api/Clawbot.Api/Services/DemoTraceService.cs` | Redis trace, TTL, watchdog, thread-safe | Service mới |
| `src/api/Clawbot.Api/Background/DemoWatchdogService.cs` | BackgroundService: scan running traces >5 phút | Chỉ active khi DEMO_MODE=true |
| `src/api/Clawbot.Api/Middleware/DemoModeMiddleware.cs` | Check DEMO_MODE, bind restriction | Middleware mới, skip if !demo |
| `src/shared/Clawbot.SharedKernel/Demo/DemoTrace.cs` | Trace model, status enum, step model | Model mới |
| `src/api/Clawbot.Api/Middleware/PancakeHmacMiddleware.cs` | HMAC verify (nếu chưa có từ spec-01) | Có thể đã có  **BLOCKED** until Section 12 verified. Implement stub: if DEMO_SKIP_HMAC=true -> pass through. Khong implement HMAC logic cho den khi confirm header name + encoding |
| **Modify** `src/api/Clawbot.Api/Program.cs` | Đăng ký demo endpoints + conditional | Thêm `if (DEMO_MODE)` block |
| **Modify** `src/api/Clawbot.Api/Program.cs` | Swagger config: hide sensitive endpoints | Condition trên env |
| `src/frontend/demo/index.html` | Demo UI (React hoặc vanilla JS) | Static file, không ảnh hưởng FE |

---

## 12. Pancake webhook signature — chờ verify

Pancake API docs chưa được confirm. Spec hiện tại assume:

| Field | Giá trị | Ghi chú |
|---|---|---|
| Header | `X-Pancake-Signature` | Cần verify từ Pancake docs |
| Format | `sha256=<hex>` | Hoặc raw hex/base64 — cần confirm |
| Algorithm | HMAC-SHA256 | Standard |
| Payload | Raw body bytes | Không normalize JSON |
| Secret | `PANCAKE_WEBHOOK_SECRET` | Riêng, không phải access token |

**Khi có Pancake docs chính xác, cập nhật section này và HMAC middleware.**

---

## 13. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Pancake webhook signature format (header name, encoding) | P1 | T7 | open — **BLOCK** implement PancakeHmacMiddleware |
| Pancake có retry webhook khi 5xx không? | P1 | T7 | open |
| UI: React hay vanilla JS (SPA 1 file)? | P1 | T7 | open |
| Demo có cần simulate "Send test message" từ UI? | P1 | T7 | open |

---
