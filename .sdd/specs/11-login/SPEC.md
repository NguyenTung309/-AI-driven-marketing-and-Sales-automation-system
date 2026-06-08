# SPEC-11 — Login & Authentication (Refactor)

Status: `REVIEW`
Spec lead: `Thắng`
Last updated: `2026-06-06`
Traces to: FR-10, SPEC-10 (Admin & Security), ADR-007, UC-J02

> **Cho Claude agent (VS Code):** §11 "Implementation Notes" liệt kê chính xác file nào sửa
> gì. Đọc code thật trước khi sửa. KHÔNG đổi schema DB ngoài: (a) thêm bảng `refresh_tokens`,
> (b) seed lại role/permission. KHÔNG thêm dependency vào `Clawbot.Gateway` (giữ ADR-007).

---

## 1. Business Context

ClawBot là app **nội bộ** (5 nhân sự, 1 tenant). Auth hiện tại có 2 vấn đề:

1. **`JwtTokenIssuer.cs` nhồi permission vào JWT** (`perm` claims) + nhồi `role[]` mảng.
   `IssueAsync` query `role_permissions` rồi bơm vào token → permission "đóng băng", đổi quyền
   role phải đợi token hết hạn; token phình to.
2. **Chưa có refresh token** — `LoginResponse` chỉ có `(AccessToken, ExpiresAt)`. Access hết
   hạn là user bị đá ra login lại.

Ngoài ra phát hiện khi audit code: hệ thống đang có **2 kho role tách biệt** (xem D1) gây mơ
hồ khi xác định "role_id" — spec này gộp về 1 kho.

**Hướng refactor:** phân quyền cứng (5 role), JWT chỉ mang `userId` + `roleId`, backend tự
tra & enforce permission (Gateway giữ sạch theo ADR-007), thêm refresh token httpOnly + rotate.

## 2. User Stories

- AS A nhân viên (Sale/Marketer/QA/Viewer) I WANT đăng nhập 1 lần và giữ session qua F5
  SO THAT không phải nhập lại mật khẩu mỗi lần refresh.
- AS AN Admin I WANT đổi **permission của 1 role** có hiệu lực ngay (user không cần login lại)
  SO THAT quản lý quyền tập trung, không gián đoạn.
- AS A security reviewer I WANT access token sống ngắn + refresh token httpOnly + rotate
  SO THAT giảm rủi ro token bị trộm qua XSS / replay.

## 3. Decision Log (đọc trước khi code)

| # | Quyết định | Lý do |
|---|---|---|
| D1 | **Gộp về 1 kho role = Identity `AppRole`.** Seed 5 role với **Id CỐ ĐỊNH** (hằng số GUID, không `NewGuid()`). Bảng `role_permissions.role_id` trỏ vào `AppRole.Id` cố định đó. Domain `roles` table KHÔNG dùng cho auth. | Hiện có 2 kho role (Identity `AppRole` Id random + domain `roles` Id random), nối nhau bằng Name → `role_id` mơ hồ. Id cố định khử mơ hồ, không cần resolve name→id lúc login. |
| D2 | **GIỮ schema `user_roles` many-to-many.** Enforce "1 user = 1 role" ở **application layer** (gán role: xóa dòng cũ rồi insert mới). | Đổi schema rủi ro cao, lợi ích thấp. Many-to-many "dư khả năng" nhưng không sai, giữ cửa mở tương lai. |
| D3 | JWT mang `sub` (userId) + `role_id` (AppRole.Id) đơn trị. **Bỏ** claim `perm[]` và `role[]`. | Permission tra runtime ở backend → đổi quyền role có hiệu lực ngay. |
| D4 | **Phương án A — Gateway chỉ verify JWT + forward. Backend tự tra & enforce permission.** Gateway KHÔNG đụng Redis/DB, KHÔNG inject `X-Permissions`. | ADR-007: "Gateway zero project refs, infrastructure only". Backend đã có sẵn EF + Redis. Tránh thêm dep + RFC cho Gateway. Triệt tiêu luôn vector spoof header. |
| D5 | Permission **seed cứng** vào `permissions` + `role_permissions` (trỏ AppRole.Id cố định). | Phase nội bộ không cần editor UI. |
| D6 | Thêm route `/auth/{**catch-all}` vào Gateway (login/refresh AllowAnonymous). Cookie `Path=/`. | Gateway hiện chỉ route `/webhook`,`/api`,`/hubs` → `/auth` sẽ 404; cookie `Path=/auth` sẽ không khớp path FE gọi. |
| D7 | **Repoint RBAC editor về cùng kho với auth (Identity AppRole.Id), HOẶC tạm disable `PUT /api/rbac/roles/{id}/permissions`.** | `RolesEndpoints.SetRolePermissionsAsync` hiện ghi `role_permissions` theo **domain** `RbacRoles.Id`, còn D1 cho backend resolve theo **Identity** `AppRole.Id` → 2 kho phân kỳ, editor thành dead/sai. Phải chốt: (a) sửa editor để thao tác trên Identity role Id cố định + DEL `perm:role:{id}` (giữ được user-story #2 "đổi quyền hiệu lực ngay"), hoặc (b) disable editor trong phase này và đánh dấu user-story #2 chỉ đạt khi sửa quyền qua seed/migration + restart. **Đề xuất (a).** |
| D8 | **Map endpoint → permission code tường minh** (bảng §6a). Endpoint CHƯA có code tương ứng thì giữ `RequireAuthorization()` (chỉ cần authenticated), KHÔNG enforce permission. | Bề mặt endpoint thật rộng hơn matrix cũ: có `/api/kb/*`, `/api/docs`, `/api/sale-assist`, `/api/chat-scenarios`, `/api/channels/pancake`, `/api/api-keys`, `/api/rbac/*`, `/api/inbox`. Không liệt kê → hoặc bị khóa hết, hoặc không biết gate code nào. Test hiện có còn dùng `kb:read`/`kb:write`. |
| D9 | **SignalR `/hubs/*` đọc token từ query string `?access_token=`** (JwtBearerEvents `OnMessageReceived`), không chỉ header. | WebSocket không set được header `Authorization` → nếu chỉ verify Bearer header thì mọi kết nối hub 401. |
| D10 | **Server grace-window cho rotate, dùng SIBLING-ROTATION.** Token vừa rotate, nếu được dùng lại trong N giây (đề xuất 10s) → **rotate lại lần nữa, cấp token mới cùng `family_id`** (chấp nhận chain rẽ nhánh — nhiều sibling cùng cha), KHÔNG coi là theft. Reuse-detection (revoke family) chỉ kích hoạt khi dùng lại token revoked NGOÀI grace window. | Single-flight chỉ gộp trong 1 tab (1 JS context). Nhiều tab dùng chung cookie httpOnly, không share promise → Tab A rotate T0→T1, Tab B vẫn gửi T0 → khớp reuse-detection → **logout oan cả family**. **Lưu ý cơ chế**: `refresh_tokens` chỉ lưu `token_hash`, KHÔNG lưu raw → server KHÔNG thể "trả lại đúng successor T1" cho Tab B (raw T1 đã trao Tab A, không lưu đâu). Vì vậy phải **cấp sibling mới** cho late-caller, không phải trả lại T1. (FE nên thêm cross-tab lock BroadcastChannel/Web Locks để giảm tải, nhưng server grace là bắt buộc.) |
| D11 | **Map cột `users.is_active` vào `AppUser` + sửa `IsActive()` kiểm cả cột này.** | `AppUser` hiện chỉ có `TenantId`+`DisplayName`, KHÔNG có `IsActive`. `IsActive()` trong `AuthEndpoints.cs` chỉ kiểm lockout. Cột `users.is_active` tồn tại nhưng không ai đọc → AC "vô hiệu hóa user" + "refresh re-check IsActive" hiện **vô tác dụng**. |
| D12 | **Toàn bộ `/auth/*` AllowAnonymous tại Gateway; backend tự enforce auth** trên `/auth/me`, `/auth/2fa/*` (đã có `.RequireAuthorization()`). `/auth/logout` cũng anonymous. | 1 route catch-all chỉ gắn được 1 policy → không thể vừa anon (`login`/`refresh`) vừa auth (`me`/`2fa`). Logout chỉ cần cookie, KHÔNG cần access token còn hạn — nếu Gateway bắt JWT hợp lệ thì user không logout được sau khi token hết hạn. |

## 4. Acceptance Criteria (EARS)

**Role model (D1, D2)**
- THE SYSTEM SHALL seed đúng 5 Identity role `Admin/Sale/Marketer/QA/Viewer` với Id cố định (hằng số).
- THE SYSTEM SHALL trỏ `role_permissions.role_id` vào Id cố định của Identity role tương ứng.
- THE SYSTEM SHALL giữ bảng `user_roles` many-to-many (không đổi schema).
- WHEN gán role cho 1 user THE SYSTEM SHALL xóa toàn bộ dòng `user_roles` cũ của user trước khi
  insert dòng mới (đảm bảo 1 user đúng 1 role).
- IF một user có 0 role hoặc role ngoài 5 role cứng THEN THE SYSTEM SHALL default-deny (coi như
  không có quyền nào) và trả 403 ở handler có yêu cầu permission.

**Login**
- THE SYSTEM SHALL phát hành access token (JWT HS256) chứa `sub` (userId) + `role_id` (AppRole.Id),
  KHÔNG chứa `perm` hay `role[]`.
- WHEN login thành công THE SYSTEM SHALL trả `{ accessToken, expiresAt }` trong body và set refresh
  token vào cookie (cấu hình cookie theo môi trường, xem AC dev/prod bên dưới).
- WHEN hoàn tất 2FA qua `/auth/login/2fa` THE SYSTEM SHALL cũng phát hành refresh token + set cookie
  giống `/auth/login` (không bỏ sót nhánh 2FA).
- WHEN login THE SYSTEM SHALL từ chối (401) nếu user `is_active = false` (cột DB, qua D11) — không
  chỉ kiểm lockout.
- WHEN login fail 5 lần liên tiếp THE SYSTEM SHALL khóa tài khoản 30 phút (giữ lockout hiện có).

**Refresh token**
- THE SYSTEM SHALL lưu refresh token dạng **SHA-256 hash** trong `refresh_tokens` (không lưu raw).
- WHEN access token hết hạn HOẶC user F5 (RAM mất token) THE SYSTEM SHALL cho phép `POST /auth/refresh`
  (đọc cookie) để cấp access token mới mà không nhập lại mật khẩu.
- WHEN refresh token được dùng THE SYSTEM SHALL rotate one-time: set `revoked_at` + `replaced_by`
  cho token cũ, phát hành token mới **kế thừa `family_id`** của token cũ.
- WHEN login mới THE SYSTEM SHALL tạo `family_id` MỚI (mỗi phiên login = 1 family); rotate kế thừa family.
- WHERE một token vừa rotate được dùng lại trong **grace window** (đề xuất 10s) THE SYSTEM SHALL
  rotate lại tạo **sibling token mới cùng `family_id`** và set vào cookie cho late-caller, KHÔNG coi
  là theft (khử false-positive multi-tab F5 — D10). (Không thể trả lại raw successor vì chỉ lưu hash.)
- WHEN xử lý `/auth/refresh` THE SYSTEM SHALL re-check user còn `is_active = true` (cột DB, qua D11)
  và không bị lockout; IF user đã bị khóa/vô hiệu hóa THEN THE SYSTEM SHALL trả 401 + revoke token.
- IF một refresh token ĐÃ revoked được dùng lại NGOÀI grace window THEN THE SYSTEM SHALL coi là
  bị trộm và revoke **toàn bộ family** của user (1 UPDATE `WHERE family_id = ...`).
- IF refresh token hết hạn / không tồn tại THEN THE SYSTEM SHALL trả 401 và clear cookie.
- WHEN user logout (`POST /auth/logout`) THE SYSTEM SHALL revoke refresh token hiện tại + clear cookie;
  THE SYSTEM SHALL idempotent — không cookie / token đã revoked vẫn trả 204.
- WHEN user reset/đổi mật khẩu thành công (`/auth/reset/confirm`, hoặc đổi pass khác sau này)
  THE SYSTEM SHALL revoke **toàn bộ refresh token family** của user đó (ép mọi thiết bị re-login).
  Lý do: reset thường vì nghi lộ tài khoản — nếu refresh token cũ còn sống tới 7 ngày thì kẻ trộm
  vẫn giữ phiên dù nạn nhân đã đổi pass → vô hiệu hóa mục đích reset. (Identity `SecurityStamp` đổi
  khi reset nhưng JWT bearer không validate stamp mặc định → revoke refresh là cách thực tế ở đây.)
- THE SYSTEM SHALL có job dọn `refresh_tokens` đã hết hạn hoặc revoked > N ngày (tránh bảng phình
  do mỗi F5 tạo 1 row mới khi rotate).

**Cookie theo môi trường**
- WHERE môi trường = Production THE SYSTEM SHALL set cookie `HttpOnly; Secure; SameSite=Strict; Path=/`.
- WHERE môi trường = Development THE SYSTEM SHALL set cookie `HttpOnly; SameSite=Strict; Path=/` —
  chỉ BỎ `Secure` (vì dev chạy http). KHÔNG cần hạ `SameSite`: vite proxy (`/api`,`/hubs` → backend)
  khiến browser chỉ thấy origin `:5173` → mọi request là **same-origin**, `SameSite=Strict` vẫn chạy tốt.
- THE SYSTEM SHALL khi clear refresh cookie (lúc 401 refresh / logout) dùng **đúng attributes như lúc
  set** (`Path=/`, `SameSite`, `Secure` theo env, `Domain` nếu có); IF clear cookie sai `Path`/attributes
  THEN browser KHÔNG xóa → refresh token "zombie" còn trong browser.
- THE SYSTEM SHALL ghi nhận: ở **dev, request KHÔNG qua Gateway** (vite proxy đi thẳng backend) → D6/D12
  (route `/auth` + verify JWT tại Gateway) chỉ kiểm chứng được ở **prod**; ở dev backend tự lo auth.
  Browser-path khi qua proxy là `/api/auth/...` nên `Path=/` (D6) là đúng — KHÔNG quay lại `Path=/auth`.

**Gateway (D4, D6) — chỉ verify + route**
- THE SYSTEM SHALL thêm route `/auth/{**catch-all}` vào Gateway với **toàn bộ AllowAnonymous**;
  backend tự enforce auth trên `/auth/me`, `/auth/2fa/*` (đã có `.RequireAuthorization()`). `/auth/logout`
  cũng anonymous (chỉ cần cookie). (D12 — 1 catch-all không thể vừa anon vừa auth.)
- WHEN request qua Gateway tới route cần auth THE SYSTEM SHALL verify chữ ký + hạn access token rồi
  forward; Gateway KHÔNG tra permission, KHÔNG đọc DB/Redis, KHÔNG set `X-Permissions`.
- THE SYSTEM SHALL cấu hình `ClockSkew` tường minh **ở CẢ Gateway VÀ backend** (`Program.cs`
  `AddJwtBearer.TokenValidationParameters`) cùng giá trị (đề xuất 30s) và cùng chấp nhận claim shape
  mới (`role_id`); IF lệch nhau THEN trong khoảng skew Gateway chấp nhận nhưng backend từ chối (hoặc
  ngược lại) → request lỗi khó lần.

**Backend authorization (Phương án A)**
- WHEN xử lý request cần quyền THE SYSTEM SHALL đọc `role_id` từ JWT, tra permission của role
  (Redis cache → fallback `role_permissions`), rồi enforce tại biên handler qua policy
  `RequirePermission("conversations:write")` theo bảng map §6a.
- WHERE một endpoint CHƯA có entry trong bảng map §6a THE SYSTEM SHALL chỉ yêu cầu authenticated
  (`RequireAuthorization()`), KHÔNG enforce permission (tránh khóa nhầm `/api/kb/*`, `/api/docs`, v.v.).
- IF Redis cache miss THEN THE SYSTEM SHALL nạp từ `role_permissions` → ghi Redis (TTL 600s) → tiếp tục.
- WHEN Admin đổi `role_permissions` của 1 role (qua editor đã repoint — D7) THE SYSTEM SHALL DEL
  Redis `perm:role:{roleId}` để hiệu lực ngay.
- IF role không có permission yêu cầu THEN THE SYSTEM SHALL trả 403.

**RBAC editor nhất quán kho role (D7)**
- THE SYSTEM SHALL đảm bảo `PUT /api/rbac/roles/{id}/permissions` thao tác trên **cùng kho role**
  mà backend resolve permission (Identity `AppRole.Id` cố định) — KHÔNG ghi vào domain `RbacRoles`.
- IF editor chưa repoint được trong phase này THEN THE SYSTEM SHALL disable endpoint đó (trả 501/404)
  và ghi rõ user-story #2 chỉ đạt qua seed + restart.

**SignalR auth (D9)**
- WHEN client kết nối `/hubs/*` THE SYSTEM SHALL chấp nhận access token qua query string
  `?access_token=` (JwtBearerEvents `OnMessageReceived`) ngoài header `Authorization`.
- WHILE một kết nối hub đang mở (long-lived, lâu hơn TTL access token 15 phút) THE SYSTEM SHALL
  để FE dùng SignalR `accessTokenFactory` trỏ vào `authStore`, và reconnect hub với token mới sau
  mỗi lần `/auth/refresh` (tránh stream realtime chết âm thầm khi token hết hạn giữa kết nối).

**Rate limit (F)**
- THE SYSTEM SHALL áp rate-limit policy `AuthPolicy` (10 req/phút/IP — đã có trong
  `RateLimitingExtensions`) cho `/auth/login`, `/auth/login/2fa`, `/auth/refresh`.

**Backend không bị bypass**
- THE SYSTEM SHALL đảm bảo backend (`:5051`) không nhận request trực tiếp từ ngoài, chỉ qua Gateway:
  bằng network isolation (prod) HOẶC shared-secret header Gateway→Backend kiểm ở middleware.
  (Lưu ý: với Phương án A không còn `X-Permissions` để spoof, nhưng vẫn cần chặn bypass auth verify.)

**Frontend (Zustand)**
- THE SYSTEM SHALL lưu access token trong Zustand store **in-memory** (KHÔNG persist localStorage/sessionStorage).
- WHEN app khởi động / F5 THE SYSTEM SHALL gọi `POST /auth/refresh` 1 lần để hydrate token vào RAM
  trước khi render route bảo vệ (hiển thị loading trong lúc chờ).
- WHEN `/auth/refresh` lúc khởi động trả 401 do **chưa từng login / không có cookie** (khách lần đầu)
  THE SYSTEM SHALL coi là trạng thái bình thường: chuyển sang `/login` lặng lẽ, KHÔNG hiện lỗi,
  KHÔNG lặp redirect (phân biệt với 401 giữa phiên đang dùng).
- WHEN nhiều request song song trong **CÙNG tab** cùng nhận 401 THE SYSTEM SHALL dùng **single-flight**:
  gộp các lần refresh vào 1 promise duy nhất, các request chờ cùng kết quả. Single-flight CHỈ gộp được
  trong 1 tab (1 JS context); trường hợp **đua nhau giữa nhiều tab** (cùng cookie httpOnly, không share
  promise) dựa vào **server grace-window (D10)** để không bị đá ra login oan, và FE CÓ THỂ thêm
  cross-tab lock (BroadcastChannel / Web Locks API) để giảm tải.
- WHEN API call trả 401 do token hết hạn THE SYSTEM SHALL gọi `/auth/refresh` (single-flight) rồi
  retry request gốc 1 lần; nếu refresh cũng 401 THEN clear store + redirect `/login`.
- THE SYSTEM SHALL lấy danh sách permission của user từ `GET /auth/me` (trả kèm `permissions`) để
  gate UI (ẩn/hiện menu, disable nút).
- THE SYSTEM SHALL coi permission ở FE CHỈ để gate UI; backend là source-of-truth. Permission lấy 1
  lần lúc mount có thể stale nếu Admin đổi quyền giữa phiên → backend vẫn chặn đúng (403), menu FE
  cập nhật ở lần F5/`/auth/me` kế tiếp.

## 5. API Contracts

```
POST /auth/login
  req : { email, password }
  res : 200 { accessToken, expiresAt }      // + Set-Cookie: refresh_token (httpOnly)
        202 { requiresTwoFactor: true }
        401 | 423 Locked

POST /auth/login/2fa
  req : { email, password, code }
  res : 200 { accessToken, expiresAt }      // + Set-Cookie: refresh_token  (giống /login)

POST /auth/refresh
  req : (no body) — refresh token từ cookie
  res : 200 { accessToken, expiresAt }      // + Set-Cookie: refresh_token mới (rotate)
        401 (clear cookie)

POST /auth/logout
  res : 204 — revoke refresh token + clear cookie

GET  /auth/me
  res : 200 { sub, roleId, role, permissions: string[] }   // permissions để FE gate UI
```

### JWT claims

```
Trước:  sub, tenant_id, tenant_slug, role[], perm[]
Sau  :  sub (userId), role_id (AppRole.Id cố định), tenant_id, tenant_slug
```

## 6. Data Models

### Bảng mới — `refresh_tokens`

```sql
-- deploy/migrations/000X_refresh_tokens.sql  (ADR-009: DDL là source of truth)
CREATE TABLE refresh_tokens (
    id          UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
    user_id     UNIQUEIDENTIFIER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    family_id   UNIQUEIDENTIFIER NOT NULL,         -- session/family — revoke cả family bằng 1 UPDATE WHERE family_id=
    token_hash  NVARCHAR(128) NOT NULL,            -- SHA-256, không lưu raw
    expires_at  DATETIMEOFFSET NOT NULL,
    revoked_at  DATETIMEOFFSET,
    replaced_by UNIQUEIDENTIFIER,                  -- id token kế tiếp (audit, KHÔNG đặt self-FK để tránh multi-cascade-path)
    created_at  DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    created_ip  NVARCHAR(64)
);
CREATE INDEX ix_refresh_tokens_user   ON refresh_tokens (user_id, expires_at DESC);
CREATE INDEX ix_refresh_tokens_hash   ON refresh_tokens (token_hash);
CREATE INDEX ix_refresh_tokens_family ON refresh_tokens (family_id);
```

### Bảng dùng lại (không sửa schema)

`users`, `permissions`, `role_permissions`, `user_roles`, `AspNetRoles` (Identity).
`role_permissions.role_id` giờ trỏ vào Identity `AppRole.Id` cố định (D1).

### Identity role Id cố định (seed hằng số)

```
Admin     = 11111111-1111-1111-1111-111111111111
Sale      = 22222222-2222-2222-2222-222222222222
Marketer  = 33333333-3333-3333-3333-333333333333
QA        = 44444444-4444-4444-4444-444444444444
Viewer    = 55555555-5555-5555-5555-555555555555
(giá trị minh hoạ — chốt GUID thực khi implement, miễn là HẰNG SỐ)
```

### Permission Matrix (seed cứng)

| Permission | Admin | Sale | Marketer | QA | Viewer |
|---|:--:|:--:|:--:|:--:|:--:|
| conversations:read  | ✅ | ✅ | ✅ | ✅ | ✅ |
| conversations:write | ✅ | ✅ | ❌ | ❌ | ❌ |
| leads:read          | ✅ | ✅ | ✅ | ✅ | ✅ |
| leads:write         | ✅ | ✅ | ❌ | ❌ | ❌ |
| content:read        | ✅ | ✅ | ✅ | ✅ | ✅ |
| content:write       | ✅ | ❌ | ✅ | ❌ | ❌ |
| ads:read            | ✅ | ❌ | ✅ | ✅ | ✅ |
| ads:write           | ✅ | ❌ | ✅ | ❌ | ❌ |
| analytics:read      | ✅ | ✅ | ✅ | ✅ | ✅ |
| kb:read             | ✅ | ✅ | ✅ | ✅ | ✅ |
| kb:write            | ✅ | ❌ | ✅ | ✅ | ❌ |
| docs:read           | ✅ | ✅ | ✅ | ✅ | ✅ |
| docs:write          | ✅ | ✅ | ✅ | ❌ | ❌ |
| sale-assist:use     | ✅ | ✅ | ❌ | ❌ | ❌ |
| chat-scenarios:read | ✅ | ✅ | ✅ | ✅ | ✅ |
| chat-scenarios:write| ✅ | ❌ | ✅ | ✅ | ❌ |
| channels:manage     | ✅ | ❌ | ❌ | ❌ | ❌ |
| api-keys:manage     | ✅ | ❌ | ❌ | ❌ | ❌ |
| rbac:manage         | ✅ | ❌ | ❌ | ❌ | ❌ |
| users:manage        | ✅ | ❌ | ❌ | ❌ | ❌ |
| system:config       | ✅ | ❌ | ❌ | ❌ | ❌ |

> Ma trận trên là **đề xuất** — Thắng confirm/chỉnh. Permission nào chưa cần enforce phase này thì
> để endpoint ở mức authenticated (D8) và bỏ code khỏi map §6a.

### 6a. Endpoint → Permission map (enforce theo D8)

| Endpoint group | Permission code | Ghi chú |
|---|---|---|
| `/api/inbox` (GET) | conversations:read | |
| `/api/inbox` (POST/reply) | conversations:write | |
| `/api/leads` (GET) | leads:read | |
| `/api/leads` (POST/PUT) | leads:write | |
| `/api/lead-scoring-rules` | leads:write | |
| `/api/docs` (GET) | docs:read | |
| `/api/docs` (POST) | docs:write | |
| `/api/sale-assist` | sale-assist:use | |
| `/api/chat-scenarios` (GET) | chat-scenarios:read | |
| `/api/chat-scenarios` (POST/PUT) | chat-scenarios:write | |
| `/api/kb/*` (GET) | kb:read | |
| `/api/kb/*` (POST/PUT) | kb:write | |
| `/api/channels/pancake` | channels:manage | |
| `/api/api-keys` | api-keys:manage | |
| `/api/rbac/*` | rbac:manage | editor — xem D7 |
| (BoundedContext stubs 501) | — | giữ authenticated, chưa enforce |
| `/auth/me`, `/auth/2fa/*` | — | authenticated, không cần permission |

> Endpoint KHÔNG có trong bảng này → chỉ `RequireAuthorization()` (D8).

### Redis (backend dùng, KHÔNG phải Gateway)

```
Key        : perm:role:{roleId}
Value      : JSON array permission code
TTL        : 600s
Invalidate : DEL khi Admin đổi role_permissions
Fallback   : Redis down → đọc thẳng role_permissions, không chặn request
```
> App nội bộ 1 instance có thể dùng `IMemoryCache` thay Redis cũng được; Redis đã wire sẵn ở
> Infrastructure DI nên dùng lại cho nhất quán + đa instance.

## 7. Technical Constraints

- **Gateway** giữ ADR-007: chỉ thêm route `/auth` trong `appsettings.json` + verify JWT (YARP
  hỗ trợ qua `Microsoft.AspNetCore.Authentication.JwtBearer` ở tầng host — đây là infra auth, KHÔNG
  phải project reference tới shared, nên không vi phạm ADR-007). Dùng chung `JwtOptions.SigningKey`.
- **Refresh token**: random 256-bit (`RandomNumberGenerator.GetBytes(32)`), raw qua cookie, lưu
  SHA-256 hash; so khớp bằng hash.
- **TTL**: access 15 phút (đổi `JwtOptions.AccessTokenMinutes` từ **60 → 15**); refresh 7 ngày
  (cân nhắc sliding — Open Questions).
- **Frontend**: dùng **Zustand** (đã có trong `package.json`); access token in-memory; axios
  `withCredentials: true` đã bật sẵn.
- Stack STRICT (Constitution Art.1): không thêm dep client mới.

## 8. Out of Scope

- RBAC editor UI động (SPEC-10).
- SSO / SAML / OIDC.
- Multi-role-per-user thực thi ở DB (schema giữ many-to-many, app enforce đơn trị).
- 2FA flow nội tại — giữ nguyên, chỉ bổ sung set refresh cookie ở nhánh hoàn tất.
- Multi-tenant logic — giữ `tenant_id` claim, không đổi.

## 9. Non-Functional Requirements

- NFR-03 (Security): refresh httpOnly + hash-at-rest + rotate one-time + reuse-detection revoke
  family; backend re-check IsActive; TLS 1.3 (prod).
- **CSRF**: `/auth/refresh` + `/auth/logout` là POST dựa cookie, không CSRF token → dựa **SameSite**
  để chống (prod Strict, dev cũng Strict — vite proxy = same-origin nên giữ Strict; cả hai chặn POST cross-site). Ghi rõ đây là cơ chế chống CSRF.
- **Token trong log**: `?access_token=` của SignalR (D9) là secret → SHALL scrub/redact query param
  `access_token` khỏi Serilog request logging (theo tinh thần LESSON-002), không để token lọt vào log.
- **Cookie Path=/**: refresh token gửi trên mọi request (rộng hơn cần) — chấp nhận với httpOnly;
  cân nhắc scope lại path sau khi routing đã thông (ghi nhận, không chặn).
- NFR-02 (Uptime ≥99.5%): Redis miss fallback DB, không chặn request.
- Latency: permission check thêm ≤ 5ms p95 khi cache hit.

## 10. Phân biệt quan trọng (tránh hiểu nhầm)

- **Đổi PERMISSION của 1 role** → hiệu lực **ngay** (backend tra runtime + invalidate Redis).
- **Đổi ROLE của 1 user** → hiệu lực sau **≤15 phút** (khi access token refresh) vì `role_id`
  nằm trong JWT. Nếu cần ngay, phải revoke refresh token của user đó để ép re-login.

## 11. Implementation Notes (file & thay đổi cụ thể)

**Backend — `src/api/Clawbot.Api`**
- `Auth/JwtTokenIssuer.cs`: bỏ param `permissions` + bỏ claim `perm`; bỏ `roles` mảng + `ClaimTypes.Role`;
  nhận 1 `roleId` (Guid) → add claim `role_id`.
- `Auth/JwtOptions.cs`: `AccessTokenMinutes` 60 → 15.
- `Endpoints/AuthEndpoints.cs`:
  - `IssueAsync`: bỏ query `perms`; map tên role (GetRolesAsync) → Id cố định (D1); sinh + lưu refresh token (hash).
  - Thêm `POST /auth/refresh` (rotate + re-check IsActive + reuse-detection), `POST /auth/logout`.
  - `LoginAsync` + `LoginWithTwoFactorAsync`: cả hai set refresh cookie (theo env).
  - `ConfirmResetAsync`: sau khi `ResetPasswordAsync` thành công → revoke toàn bộ refresh token
    family của user (qua `RefreshTokenService`) để ép re-login mọi thiết bị.
  - `Me`: trả `{ sub, roleId, role, permissions }` (tra permission cho FE gate UI).
- Thêm middleware/policy `RequirePermission(code)`: đọc `role_id` → Redis `perm:role:{id}` → fallback
  `role_permissions` → enforce.
- Thêm `RefreshTokenService` (sinh/hash/verify/rotate/revoke-family) + entity map cho `refresh_tokens`.
- Cleanup job (HostedService) dọn refresh token hết hạn/revoked.
- `Endpoints/RolesEndpoints.cs` (D7): repoint `SetRolePermissionsAsync` + `ListRolePermissionsAsync`
  sang Identity `AppRole.Id` cố định (bỏ `db.RbacRoles`), DEL `perm:role:{id}` sau khi ghi; HOẶC tạm
  disable 2 endpoint permission đó nếu chưa repoint.
- `Program.cs`: `AddSignalR` + JwtBearer `OnMessageReceived` đọc `?access_token=` cho `/hubs/*` (D9);
  set `TokenValidationParameters.ClockSkew` = 30s (đồng bộ Gateway, D10/#4); gắn
  `RequireRateLimiting(AuthPolicy)` cho `/auth/login`, `/auth/login/2fa`, `/auth/refresh` (F);
  scrub query `access_token` khỏi Serilog request log.

**Tests — `tests/Clawbot.Api.Tests` (bắt buộc theo DoD: TDD, test pass)**
- `JwtTokenIssuerTests.cs`: SẼ VỠ COMPILE khi đổi chữ ký `Issue` (bỏ `roles[]`/`permissions`, thêm
  `roleId`). Cập nhật: bỏ `Includes_permission_claims_*`, đổi sang assert claim `role_id` + `sub`;
  cập nhật mọi call `Issue(...)`.
- Viết test mới: refresh rotate (one-time), reuse-detection revoke family, refresh re-check IsActive,
  cookie env (dev no-Secure / prod Secure), 401→single-flight→retry (FE, nếu có test FE).

**Identity — `src/shared/Clawbot.Infrastructure/Identity`**
- `AppUser.cs` (D11): thêm property `bool IsActive` map cột `users.is_active`; cấu hình EF mapping
  cột tương ứng. `AuthEndpoints.IsActive()`: kiểm cả `user.IsActive` (không chỉ lockout).

**Identity seed — `src/shared/Clawbot.Infrastructure/Identity/RbacSeeder.cs`**
- Đổi `Id = Guid.NewGuid()` → Id **cố định** theo bảng §6.
- Seed `permissions` + `role_permissions` (trỏ AppRole.Id cố định) theo matrix §6 (idempotent).

**DB — `deploy/migrations/` + `deploy/seed/`**
- Thêm `000X_refresh_tokens.sql` (§6). Seed permission matrix.
- **Đồng bộ mọi đường tạo schema**: ADR-009 coi DDL `deploy/migrations/*.sql` là source of truth, và
  `Program.cs` chạy `RbacSeeder.SeedAsync` lúc khởi động (KHÔNG có EF auto-migration trong repo hiện
  tại). Đảm bảo: (a) bảng `refresh_tokens` + cột map `is_active` có mặt qua đường dev dùng để dựng DB
  (apply file SQL, hoặc EF model nếu sau này thêm), KHÔNG chỉ ghi file SQL rồi quên; (b) `RbacSeeder`
  seed role Id cố định + permission matrix idempotent; tài khoản test (vd `admin@clawbot.local`) vẫn
  gán được role hợp lệ sau khi đổi role Id.

**Gateway — `src/gateway/Clawbot.Gateway`**
- `appsettings.json`: thêm route `auth-routes` match `/auth/{**catch-all}` → cluster backend.
- `Program.cs`: thêm JwtBearer authentication (host-level) + verify trên route cần auth; `/auth/login`,
  `/auth/refresh` anonymous; set `ClockSkew`. KHÔNG thêm project reference / Redis / EF.

**Frontend — `src/frontend/clawbot-web/src`**
- `shared/auth/authStore.ts` (mới, Zustand in-memory): `{ accessToken, permissions, setAuth, clear }`.
- `shared/auth/AuthContext.tsx`: thay bằng/wrap Zustand store; bỏ localStorage.
- `shared/api/client.ts`: request interceptor đọc token từ `authStore`; response interceptor
  401 → single-flight `/auth/refresh` → retry; fail → clear + redirect `/login`.
- `app/providers.tsx`: mount → gọi `/auth/refresh` 1 lần hydrate token + `/auth/me` lấy permissions,
  loading state trước khi render `RouterProvider`.
- `features/auth/LoginPage.tsx`: set token + permissions vào `authStore` thay localStorage.

## 12. Error Handling Matrix

| Error condition | Detection | User-visible | Recovery |
|---|---|---|---|
| Access token hết hạn | Gateway verify fail (exp) | (ẩn) | FE single-flight `/auth/refresh` → retry |
| Refresh hết hạn/không tồn tại | DB lookup fail | redirect `/login` | login lại |
| Refresh reuse (token đã revoked) | DB `revoked_at != null` | redirect `/login` | revoke cả family, login lại |
| User bị khóa/vô hiệu hóa | refresh re-check IsActive | redirect `/login` | liên hệ Admin |
| Sai email/mật khẩu | Identity check fail | "Login failed. Check credentials." | nhập lại |
| Khóa TK (5 fail) | `IsLockedOut` | "Tài khoản bị khóa 30 phút" (423) | đợi 30 phút |
| Redis down (DB ok) | Redis exception | (ẩn) | fallback `role_permissions` |
| Thiếu quyền | role không có permission | 403 "Không có quyền" | đổi role / liên hệ Admin |
| 0 role / role lạ | resolve role_id fail | 403 default-deny | gán role hợp lệ |
| Cookie không gửi (dev http) | refresh 401 lặp | redirect `/login` | dùng cookie config dev (SameSite=Strict, no Secure) |
| Reset/đổi mật khẩu | `ResetPasswordAsync` success | mọi thiết bị về `/login` | revoke toàn bộ refresh family → login lại bằng pass mới |

## 13. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Refresh TTL 7 ngày fixed hay sliding? | Thắng | — | open |
| Cleanup job chạy cadence nào (daily?) + giữ revoked bao lâu cho audit? | Thắng | — | open |
| Backend anti-bypass: network isolation (prod) đủ chưa, hay cần shared-secret header? | Thắng | — | open |
| `tenant_id` còn cần trong claim khi 1 tenant nội bộ? | Thắng | — | open |
| Dùng Redis hay IMemoryCache cho permission cache (1 instance)? | Thắng | — | open |
| Prod: FE same-origin với Gateway hay cross-origin (cần CORS + credentials ở Gateway)? Dev dùng proxy nên không động tới CORS; Gateway hiện chưa có CORS. | Thắng | — | open |
| Logout không vô hiệu hóa access token đang sống (JWT stateless → token cũ hợp lệ tới ≤15 phút sau logout). Chấp nhận, hay cần denylist? | Thắng | — | open |