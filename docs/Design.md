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
- [~] **Dashboard tổng quan** (skeleton + mock metric; cần wire API)
- [ ] **Unified Inbox** (priority sort + filter + SignalR realtime)
- [ ] **Conversation view** + context panel
- [ ] **Sale Assist** (draft + quick reply + alert toast)
- [ ] **KB editor** + version history + accuracy chart
- [ ] **Agent dashboard** + start/stop + logs (dùng `WorkflowNode`)
- [ ] **Lead list** + Kanban pipeline + detail
- [ ] **Content brief editor** + queue + calendar
- [ ] **Document library** + preview + send
- [ ] **Analytics dashboard** (KPI 5 kênh)
- [ ] **Admin** (users / roles / api-keys / integrations)
- [ ] **Notification center** + Telegram link

---

## Backend wiring status

- [x] axios `apiClient` (baseURL `VITE_API_BASE_URL` ?? `/api`, Bearer interceptor) — `shared/api/client.ts`
- [x] `AuthContext` (token ↔ localStorage) + route guard `RequireAuth`
- [x] **Login** → `POST /api/auth/login` (thật)
- [ ] **Forgot** → `/auth/reset/request` + `/auth/reset/confirm` (UI đang mock client-side)
- [ ] **Profile** → `/auth/me` (load) + endpoint update profile + `/auth/2fa/*` (UI đang mock)
- [ ] **Dashboard / Inbox / Lead / …** → API tương ứng

> Backend endpoints đa số đã có (xem [module-checklist.md](module-checklist.md) M02/M08/M15…). Bước tiếp: thay mock bằng TanStack Query hooks gọi `apiClient`.
