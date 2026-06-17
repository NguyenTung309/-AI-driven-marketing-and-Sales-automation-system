# ClawBot Frontend — Design System & Screen Checklist

> Living doc cho `src/frontend/clawbot-web`. Nguồn thiết kế: **Google Stitch project `12301695846158842476` — "Draft mockup screen"** (~100 màn, nhóm theo role: Admin / Sale / MKT / QA-Data / Login & Profile).
> Convention checklist: `[ ]` chưa làm · `[~]` đang làm · `[x]` xong.
> Last updated: 2026-06-17

---

## Nguồn & quy trình

- Truy cập design: `stitch` MCP (`get_project` / `get_screen` / `list_screens`).
- Kéo HTML từng màn: skill `stitch-build:react-components` → `scripts/fetch-stitch.sh "<htmlCode.downloadUrl>" ".stitch/designs/<name>.html"` (Windows: nhớ `tr -d '\r'` cho URL — CRLF làm `curl (3) Malformed URL`).
- `.stitch/` đã gitignore (cache, re-fetch được).
- Port: đọc HTML → ráp React component reuse token + primitive đã có (KHÔNG copy verbatim các class faint như `text-outline`).

---

## Design rules (tokens — `src/index.css` `@theme`)

> Tên token mirror Stitch `tailwind.config` → class trong markup design resolve verbatim (`bg-primary`, `px-gutter`, `text-headline-md`...).

### Màu
- **Brand:** `primary #d32f2f` (Học Bá Red), `primary-hover #b71c1c`, `on-primary #ffffff`, `secondary #1e293b`.
- **Surface:** `surface #f8fafc` (canvas L0) · `surface-container-lowest #ffffff` (card L1) · `surface-container-low #f2f4f6` · `surface-variant #e2e8f0` · `on-surface #191c1e` · `on-surface-variant #5b6470`.
- **Viền:** `outline #e2e8f0`, `outline-variant #e4beba`.
- **Semantic:** `success #10b981` · `warning #f59e0b` · `error #ef4444`.

### Typography
- **Font:** Inter (UI) + JetBrains Mono (ID / metric / label code).
- **Scale:** `display-lg 36` · `headline-lg 32` · `headline-md 24` · `headline-sm 20` · `body-lg 16` · `body-md 14` · `label-lg 14/600` · `label-caps 12 uppercase` · `label-sm 11` · `telemetry-data 28` · `mono-status 13`.
- Hỗ trợ đầy đủ dấu tiếng Việt.

### Layout
- **Authed pages:** `AppShell` = sidebar đỏ cố định **260px** + topbar **64px** + content fluid (`md:ml-[260px]`, `pt-[64px]`).
- **Auth/centered pages** (quên mật khẩu): `AuthCardShell` = card giữa + watermark "HỌC BÁ EDUCATION" + footer.
- **Login:** split-screen (panel brand đỏ + panel form).
- Grid 12-col, gutter 24px. Dot-grid canvas cho workflow node.

### Shape & elevation
- Radius: **4px** (button/input) · **8px** (`rounded-lg`, node) · **12px** (card auth) · pill (status).
- Elevation: tonal + viền 1px `outline`, tránh shadow nặng. Modal = `shadow-2xl` trên backdrop `bg-black/50 backdrop-blur`.

### Conventions code
- TS: mỗi component có `Readonly` interface `[Name]Props`; không `any`; component PascalCase; hook `use*`.
- Icon: Material Symbols Outlined (`.material-symbols-outlined`).
- Reuse primitive trước khi viết mới. File ≤ 800 dòng.

---

## Component library

### `shared/ui/`
`Button` · `Card` · `StatusPill` · `MetricCard` (telemetry widget) · `ToggleSwitch` · `Input` · `DataTable<T>` · `WorkflowNode` · `Modal` · `Alert`.

### `shared/layout/`
`AppShell` · `Sidebar` (nav `nav.ts`) · `Topbar` · `AuthCardShell`.

---

## Screen checklist

### ✅ Core shell + design system
- [x] Design tokens (`@theme`) từ Stitch DESIGN.md
- [x] `AppShell` + `Sidebar` (đỏ 260px) + `Topbar`
- [x] Primitive library (10 component)
- [x] `AuthCardShell` (centered + watermark)

### ✅ Nhóm Login & Profile (DONE)
- [x] **Login** split-screen + 4 state (default / error / locked / loading)
- [x] **Quên mật khẩu** 4 bước (email → OTP + timer → đặt lại MK → thành công)
- [x] **Hồ sơ cá nhân** 3 tab (Thông tin cơ bản + 2FA · Phân quyền trực thuộc · Nhật ký bảo mật)
- [x] **Dialog đổi mật khẩu** (Modal + thanh đo độ mạnh + cảnh báo)

### ✅ M16 surfaces + S17 public (DONE)
- [x] **Dashboard tổng quan** — KPI + chart + forecast/funnel/agent telemetry + realtime SignalR
- [x] **Unified Inbox** (priority sort + filter + SignalR realtime)
- [x] **Conversation view** + context panel
- [x] **Sale Assist** (draft + quick reply + alert toast)
- [x] **KB editor** + version history + accuracy chart
- [x] **Agent dashboard** + start/stop + logs + right drawer cấu hình/sandbox (dùng `WorkflowNode`)
- [x] **Nhật ký tác vụ & Truy vết** — `/logs` wired to `/api/logs/task-runs`, `/api/logs/task-runs/{id}`, `/api/logs/audit`; follows Stitch traceability drawer as a full route.
- [x] **Pixel Agents Office (SW-043)** — `/agents-office` maps agent trạng thái, hàng đợi và trace feed into a compact operations floor; aligned to Stitch `Quản lý Agent (S11)` + `Chi tiết điều phối & Trace (S12-V1)`.
- [x] **Cấu hình Prompt gốc** — `/prompts` wired to `/api/prompts/configs`, detail/update/sandbox; follows Stitch `Cấu hình LLM` cards + sandbox modal.
- [x] **Lead list** + Kanban pipeline + detail
- [x] **Content brief editor** + queue + calendar — brief CRUD + generate/approve/reject/schedule/repurpose + trend scan/calendar wired to `/api/content`
- [x] **Document library** + preview + send — template CRUD + generated list/preview + generate/send email via `/api/docs`
- [x] **Analytics dashboard** (KPI 5 kênh) — `/analytics` wired to `/api/analytics` omnichannel/delta/funnel/agent-performance/agent-cost/forecast/anomalies/export
- [x] **Admin** (users / roles / api-keys / integrations / branding) — `/system` wired to `/api/admin/users`, `/api/rbac`, `/api/api-keys`, `/api/channels/pancake`, `/api/admin/tenant/branding`, `/api/admin/audit-logs`
- [x] **Notification center** — `/notifications` wired to `/api/notifications` + SignalR/in-app; Telegram retired by product decision
- [x] **Web Chat Widget (S17)** — public `/chat-widget/:tenantSlug` wired to `/api/public/widget/{tenantSlug}` lead capture + message append + tenant branding.
- [x] **FAQ / Support Page (S17)** — public `/support/:tenantSlug` wired to KB-backed `/api/public/widget/{tenantSlug}/faq` + tenant branding.
- [x] **Quản lý hạn ngạch Token** — `/tokens` wired to `/api/tokens/usage` + `/api/tokens/settings`, reads Claude cost ledger and stores quota/router settings in `AgentConfig`.

---

## Backend wiring

### Hạ tầng
- `shared/api/client.ts` — axios `apiClient` (baseURL `VITE_API_BASE_URL` ?? `/api`, Bearer interceptor từ localStorage).
- `shared/auth/AuthContext.tsx` — token ↔ localStorage + route guard `RequireAuth`.
- `shared/api/auth.ts` — module typed cho toàn bộ endpoint `/auth/*` (login, login/2fa, reset, 2fa enable/verify/disable, me).
- `shared/api/analytics.ts` — `getOmnichannel()`.
- Data fetch dùng **TanStack Query** (`useQuery`); mutation gọi thẳng hàm trong api module.

### Đã wire (thật)
- [x] **Login** → `POST /auth/login`; xử lý **202 → 2FA** (`POST /auth/login/2fa`) + **423 locked** + 401 (`shared/api/auth.ts` → `LoginPage`).
- [x] **Quên mật khẩu** → `POST /auth/reset/request` sinh OTP 6 số, gửi/log OTP và cache mapping Identity token 10 phút; `POST /auth/reset/confirm` nhận OTP qua field `token`.
- [x] **Hồ sơ** → `GET /auth/me` (roles/perms) + `GET/PUT /api/profile` (displayName/phone/dateOfBirth) + `POST /api/profile/avatar`.
- [x] **2FA setup** (`TwoFactorSetupDialog`) → `POST /auth/2fa/enable` (lấy khóa) → `/auth/2fa/verify` (kích hoạt); toggle off → `/auth/2fa/disable`.
- [x] **Dashboard** → `GET /api/analytics/omnichannel` (useQuery): cộng dồn rows → MetricCard; StatusPill phản ánh `stale`/lỗi API.
- [x] **Đổi mật khẩu khi đã đăng nhập** (`ChangePasswordDialog`) → `POST /auth/change-password`.
- [x] **Nhật ký bảo mật** → `GET /api/profile/login-history` (current-user `auth.login` audit entries).
- [x] **Nhật ký tác vụ** → `GET /api/logs/task-runs` + detail `/api/logs/task-runs/{id}` + audit `/api/logs/audit` (tenant-scoped `AgentSession`/`AgentTrace`/`AuditLog`/`ClaudeCostLedger`).
- [x] **Cấu hình Prompt gốc** → `GET/PUT /api/prompts/configs/{code}` + `POST /api/prompts/configs/{code}/sandbox` (tenant-scoped `AgentConfig`, usage từ `ClaudeCostLedger`, sandbox ghi `AgentSession`/`AgentTrace`).

### Còn gap còn lại (Login/Profile)
- Không còn gap trực tiếp sau khi reset password đã chuyển sang OTP thật và Profile đã nối backend.

---

## Backend endpoint catalog (cho FE wire)

> Nguồn: `src/api/Clawbot.Api/Endpoints/*.cs`. Rate-limit: `/auth` = AuthPolicy(10/min) · `/api/inbox`+`/api/sale-assist` = ChatPolicy(60/min) · còn lại = GeneralPolicy(300/min) · webhook 120/min. Hầu hết `/api/*` yêu cầu auth (Bearer JWT); `(anon)` = AllowAnonymous.

### Auth — `/auth`
| Method | Path | Body / Note |
|---|---|---|
| POST | `/auth/login` (anon) | `{email,password}` → `{accessToken,expiresAt}` · 202 `{requiresTwoFactor}` · 401 · 423 locked |
| POST | `/auth/login/2fa` (anon) | `{email,password,code}` → `{accessToken,expiresAt}` |
| POST | `/auth/reset/request` (anon) | `{email}` → 200 (anti-enumeration; sinh/gửi OTP 6 số, cache 10 phút) |
| POST | `/auth/reset/confirm` (anon) | `{email,token,newPassword}` với `token` là OTP 6 số → 200 \| 400 |
| POST | `/auth/2fa/enable` | → `{sharedKey,authenticatorUri}` |
| POST | `/auth/2fa/verify` | `{code}` → 200 \| 400 |
| POST | `/auth/2fa/disable` | → 200 |
| GET | `/auth/me` | → `{sub,tenantId,tenantSlug,roles[],permissions[]}` |

### RBAC — `/api/rbac`
`GET\|POST /roles` · `PUT\|DELETE /roles/{id}` · `GET\|PUT /roles/{id}/permissions` · `GET /permissions`

### API keys — `/api/api-keys`
`GET` · `POST` (plaintext-once) · `DELETE /{id}`

### Knowledge Base — `/api/kb`
`GET\|POST /modules` · `GET\|PUT /modules/{id}` · `POST /modules/{id}/archive` · `GET\|POST /modules/{id}/versions` · `GET /modules/{id}/versions/{versionId}` · `POST .../deploy` · `POST .../rollback` · `GET /modules/{id}/diff?fromVersion=&toVersion=` · `GET\|POST /modules/{id}/test-cases` · `POST /modules/{id}/test` · `GET /api/kb/accuracy`

### Inbox — `/api/inbox` (ChatPolicy, + SignalR `InboxHub` realtime)
`GET /conversations` (paged, filter status/platform, hot-first) · `GET /conversations/{id}` · `POST /conversations/{id}/assign` · `/resolve` · `/escalate` · `/messages` (gửi outbound)

### Sale Assist — `/api/sale-assist` (ChatPolicy)
`POST /draft` · `POST /summary` · `GET\|POST /quick-replies` · `PUT\|DELETE /quick-replies/{id}` · `GET /daily-summary` · `GET /upsell-suggestions` · **`GET /upsell?conversationId=`** → `{eligible,suggestion,reason,leadScore}` (SaleAssist-4: hot-gate + Claude; `eligible=false` khi lead chưa 'hot')

### Leads — `/api/leads`
`GET` (paged, score desc) · `GET /{id}` · `POST` · `POST /create-with-skills` · `POST /{id}/activities` · `POST /{id}/assign` · `GET /forecast` · `GET /{id}/context` · **`/api/lead-scoring-rules`**: `GET\|POST` · `DELETE /{id}`

### Chat scenarios — `/api/chat-scenarios`
`GET` · `GET /{id}` · `POST` · `PUT /{id}` · `DELETE /{id}` · `POST /match` · `POST /{id}/outcome`

### Content — `/api/content`
`GET\|POST /briefs` · `GET\|PUT\|DELETE /briefs/{id}` · `GET /trends` · `POST /trends/scan` · `POST /items/generate` · `GET /queue` · `PUT\|DELETE /items/{id}` · `POST /items/{id}/approve\|reject\|schedule\|repurpose` · `GET /calendar` · `DELETE /schedule/{id}`

### Documents — `/api/docs`
`POST /generate` (body `{templateCode,contactId?,vars?,sentVia?}`; `sentVia="email"` → gửi SMTP gated + đánh dấu sent) → `{documentId,fileUrl,fileHash,sizeBytes,latencyMs}` · `GET\|POST /templates` · `PUT\|DELETE /templates/{id}` · `GET /generated` (kèm `expiresAt`) · **`GET /{id}/download`** → 302 redirect tới fileUrl · **410** khi link quá hạn 7 ngày (Docs-1). Auto-fill var từ Contact: `contact_name/customer_name/contact_phone/contact_email`.

### Analytics — `/api/analytics`
`GET /omnichannel?from=&to=` → `{from,to,rows[{platform,leads,dms,replies,conversions,avgResponseTimeSec,adSpend,cpl}],stale}` · **`GET /omnichannel-delta?from=&to=&compare=dod|wow`** → `{from,to,compare,prevFrom,prevTo,metrics[{metric,current,previous,deltaPct}]}` (Report-1) · `GET /funnel` · `GET /agent-performance` · `GET /anomalies` · `GET /forecast` · `GET /export` · `GET /agent-cost`

### Experiments — `/api/experiments`
`GET /?targetType=chat_scenario|kb_version` · `POST /` create A/B experiment with weighted variants · `POST /{id}/assign` deterministic subject assignment · `POST /{id}/events` exposure/conversion/custom event log · `GET /{id}/summary` conversion rate + winner · `POST /{id}/stop`.

### Competitors — `/api/competitors` (Research-2)
`GET /sources` (perm `content.read`) · `POST /sources` (perm `content.write`, max 20/tenant, body `{name,url,sourceType?}`) · `PUT\|DELETE /sources/{id}` · `GET /posts?sourceId=&take=` → `[{id,sourceId,url,title,snippet,publishedAt,detectedAt}]`. Quét tự động hàng ngày 06:00 VN (CompetitorScanJob) → notification `competitor` khi có bài mới.

### Ads — `/api/ads`
`GET\|POST /rules` · `PUT\|DELETE /rules/{id}` · `GET /campaigns` · `PUT /campaigns/{id}/target-cpl` · `GET /actions` · `POST /campaigns/{id}/evaluate` · `POST /lookalike`

### Channels — `/api/channels/pancake`
`GET\|PUT\|DELETE /config` · `GET /webhook-url`

### Admin users / Profile / Notifications / Agents (M23/M24/M25)
- **`/api/admin/users`** (perm `admin.system`): `GET` list · `POST` create · `PUT /{id}` · `POST /{id}/enable\|disable` · `POST /{id}/reset-password` (tenant-scoped).
- **`/api/admin/tenant/branding`** (perm `admin.system`): `GET` current tenant brand defaults · `PUT` update `brandName`, `logoUrl`, `primaryColor`, `accentColor`, `supportName`, `widgetGreeting`.
- **`/api/profile`**: `GET` · `PUT` (displayName/phone/dateOfBirth) · `POST /avatar` (multipart ≤2MB image, IDocumentStorage) · `GET /login-history`.
- **`/api/notifications`**: `GET` (paged + `?unread=`) · `GET /unread-count` · `POST /{id}/read` · `POST /read-all`. SignalR `NotificationHub` `/hubs/notifications`. Types: `hot_lead`·`idle`·`idle_escalation`·`ads_budget`·`competitor`·`anomaly`·`system`.
- **`/api/agents`** (M25): `GET` list (status/last-run) · `POST /{code}/enable\|disable` · `GET\|PUT /{code}/settings` (model/provider/systemPrompt/skills/KB modules) · `POST /{code}/sandbox` (creates `AgentSession` + `AgentTrace`) · `GET /{code}/traces`.
- **`/api/tokens`**: `GET /usage` aggregates `ClaudeCostLedger` by agent/model and returns quota burn-down; `PUT /settings` saves per-agent monthly quota, warning percent, router tier, and in-app low-balance alert settings via `AgentConfig.ConfigJson`.
- **`/api/logs`**: `GET /task-runs` returns paged agent session history + trace/token/audit stats; `GET /task-runs/{sessionId}` returns ordered trace detail + related audit events; `GET /audit` returns tenant-scoped audit log for the `/logs` surface.
- **`/api/prompts`**: `GET /configs` list prompt/runtime configs across `AgentConfig`; `GET|PUT /configs/{code}` detail/update provider/model/systemPrompt/temperature/maxTokens; `POST /configs/{code}/sandbox` tests a draft prompt and records trace.

### Public Web Chat Widget (S17)
- **`GET /api/public/widget/{tenantSlug}/bootstrap`** (anon) → tenant display name, support metadata, greeting, suggested questions and `branding`.
- **`GET /api/public/widget/{tenantSlug}/faq`** (anon) → active KB test cases grouped by tenant module plus `branding` for the public FAQ/support page.
- **`POST /api/public/widget/{tenantSlug}/lead`** (anon) → creates/updates `Contact`, creates/updates warm `Lead`, opens `Conversation`, appends inbound + bot reply, pushes Inbox SignalR.
- **`POST /api/public/widget/{tenantSlug}/messages`** (anon) → appends visitor message + bot acknowledgement to an existing widget conversation.
- `POST /auth/change-password` (đã đăng nhập).

### Admin / Contacts / Health / Webhook
`GET /api/admin/audit-logs` · `POST /api/contacts/merge` · `GET /health/live\|/ready\|/channels/pancake` (anon) · `POST /webhooks/pancake/{tenantSlug}` (anon, HMAC)
