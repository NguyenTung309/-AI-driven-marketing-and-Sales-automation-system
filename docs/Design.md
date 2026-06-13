# ClawBot Frontend — Design System & Screen Checklist

> Living doc cho `src/frontend/clawbot-web`. Nguồn thiết kế: **Google Stitch project `15408388482133270285` — "Học Bá Admin Dashboard"** (~100 màn, nhóm theo role: Admin / Sale / MKT / QA-Data / Login & Profile).
> Convention checklist: `[ ]` chưa làm · `[~]` đang làm · `[x]` xong.
> Last updated: 2026-06-13

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

### ⏳ Pending (M16 — 12 surface)
- [~] **Dashboard tổng quan** — KPI wired (`/api/analytics/omnichannel`); cần thêm chart + realtime SignalR
- [x] **Unified Inbox** (priority sort + filter + SignalR realtime)
- [x] **Conversation view** + context panel
- [ ] **Sale Assist** (draft + quick reply + alert toast)
- [x] **KB editor** + version history + accuracy chart
- [x] **Agent dashboard** + start/stop + logs (dùng `WorkflowNode`)
- [x] **Lead list** + Kanban pipeline + detail
- [ ] **Content brief editor** + queue + calendar
- [ ] **Document library** + preview + send
- [ ] **Analytics dashboard** (KPI 5 kênh)
- [ ] **Admin** (users / roles / api-keys / integrations)
- [~] **Notification center** + Telegram link — center + SignalR đã xong, Telegram adapter còn thiếu

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
- [x] **Quên mật khẩu** → `POST /auth/reset/request` (bước 1) + `POST /auth/reset/confirm` (bước đặt lại MK).
- [x] **Hồ sơ** → `GET /auth/me` (useQuery): tab **Phân quyền** dùng `permissions[]` thật, badge = `roles[0]`, thống kê = số vai trò/quyền.
- [x] **2FA setup** (`TwoFactorSetupDialog`) → `POST /auth/2fa/enable` (lấy khóa) → `/auth/2fa/verify` (kích hoạt); toggle off → `/auth/2fa/disable`.
- [x] **Dashboard** → `GET /api/analytics/omnichannel` (useQuery): cộng dồn rows → MetricCard; StatusPill phản ánh `stale`/lỗi API.

### Còn gap (cần backend hoặc bước sau)
- [ ] **OTP ≠ token**: backend reset dùng **Identity token** (đang log server-side, chưa gửi email), không phải OTP 6 số. UI mang giá trị OTP làm `token` khi confirm → cần endpoint gửi email/OTP thật.
- [ ] **Lưu hồ sơ** (họ tên/SĐT/ngày sinh): `/auth/me` chỉ trả claims (sub/tenant/roles/perms) — **không có** field tên/email/phone, cũng **chưa có** endpoint update profile. Field giữ editable, nút Lưu chưa nối.
- [ ] **Đổi mật khẩu khi đã đăng nhập** (`ChangePasswordDialog`): **chưa có** endpoint (chỉ có reset qua token). Cần `POST /auth/change-password`.
- [ ] **Nhật ký đăng nhập** (tab bảo mật): chưa có API login-history per-user → đang mock (`/api/admin/audit-logs` là admin-scope, khác mục đích).

---

## Backend endpoint catalog (cho FE wire)

> Nguồn: `src/api/Clawbot.Api/Endpoints/*.cs`. Rate-limit: `/auth` = AuthPolicy(10/min) · `/api/inbox`+`/api/sale-assist` = ChatPolicy(60/min) · còn lại = GeneralPolicy(300/min) · webhook 120/min. Hầu hết `/api/*` yêu cầu auth (Bearer JWT); `(anon)` = AllowAnonymous.

### Auth — `/auth`
| Method | Path | Body / Note |
|---|---|---|
| POST | `/auth/login` (anon) | `{email,password}` → `{accessToken,expiresAt}` · 202 `{requiresTwoFactor}` · 401 · 423 locked |
| POST | `/auth/login/2fa` (anon) | `{email,password,code}` → `{accessToken,expiresAt}` |
| POST | `/auth/reset/request` (anon) | `{email}` → 200 (anti-enumeration) |
| POST | `/auth/reset/confirm` (anon) | `{email,token,newPassword}` → 200 \| 400 |
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
`POST /draft` · `POST /summary` · `GET\|POST /quick-replies` · `PUT\|DELETE /quick-replies/{id}` · `GET /daily-summary` · `GET /upsell-suggestions`

### Leads — `/api/leads`
`GET` (paged, score desc) · `GET /{id}` · `POST` · `POST /create-with-skills` · `POST /{id}/activities` · `POST /{id}/assign` · `GET /forecast` · `GET /{id}/context` · **`/api/lead-scoring-rules`**: `GET\|POST` · `DELETE /{id}`

### Chat scenarios — `/api/chat-scenarios`
`GET` · `GET /{id}` · `POST` · `PUT /{id}` · `DELETE /{id}` · `POST /match` · `POST /{id}/outcome`

### Content — `/api/content`
`GET\|POST /briefs` · `GET\|PUT\|DELETE /briefs/{id}` · `GET /trends` · `POST /trends/scan` · `POST /items/generate` · `GET /queue` · `PUT\|DELETE /items/{id}` · `POST /items/{id}/approve\|reject\|schedule\|repurpose` · `GET /calendar` · `DELETE /schedule/{id}`

### Documents — `/api/docs`
`POST /generate` → `{documentId,fileUrl,fileHash,sizeBytes,latencyMs}` · `GET\|POST /templates` · `PUT\|DELETE /templates/{id}` · `GET /generated`

### Analytics — `/api/analytics`
`GET /omnichannel?from=&to=` → `{from,to,rows[{platform,leads,dms,replies,conversions,avgResponseTimeSec,adSpend,cpl}],stale}` · `GET /funnel` · `GET /agent-performance` · `GET /anomalies` · `GET /forecast` · `GET /export`

### Ads — `/api/ads`
`GET\|POST /rules` · `PUT\|DELETE /rules/{id}` · `GET /campaigns` · `PUT /campaigns/{id}/target-cpl` · `GET /actions` · `POST /campaigns/{id}/evaluate` · `POST /lookalike`

### Channels — `/api/channels/pancake`
`GET\|PUT\|DELETE /config` · `GET /webhook-url`

### Admin / Contacts / Health / Webhook
`GET /api/admin/audit-logs` · `POST /api/contacts/merge` · `GET /health/live\|/ready\|/channels/pancake` (anon) · `POST /webhooks/pancake/{tenantSlug}` (anon, HMAC)
