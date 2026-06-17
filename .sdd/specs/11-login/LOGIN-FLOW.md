# Login Flow — Tài liệu luồng (SPEC-11)

> Mô tả luồng đăng nhập / phiên / phân quyền **đúng theo code hiện tại**. Tham chiếu chi tiết
> quyết định thiết kế xem [SPEC.md](./SPEC.md).

## 1. Nguyên tắc cốt lõi

| Nguyên tắc | Vị trí code |
| --- | --- |
| **JWT KHÔNG chứa permission.** Token chỉ mang `sub`, `role_id`, `tenant_id`, `tenant_slug`. | [JwtTokenIssuer.cs](../../../src/api/Clawbot.Api/Auth/JwtTokenIssuer.cs) |
| **Permission tra runtime mỗi request** từ Redis → fallback DB. | [PermissionResolver.cs](../../../src/shared/Clawbot.Infrastructure/Auth/PermissionResolver.cs) |
| **Gateway chỉ verify JWT + forward** (Phương án A, ADR-007). Không đụng Redis/DB, không inject `X-Permissions`. | `Clawbot.Gateway` |
| **Backend là source-of-truth** cho enforce. FE chỉ dùng permission để gate UI. | [PermissionEndpointExtensions.cs](../../../src/api/Clawbot.Api/Auth/PermissionEndpointExtensions.cs) |
| **Access token in-memory ở FE**, refresh token httpOnly cookie + rotate. | [authStore.ts](../../../src/frontend/clawbot-web/src/shared/auth/authStore.ts), [client.ts](../../../src/frontend/clawbot-web/src/shared/api/client.ts) |

## 2. Luồng login (happy path)

```
FE                          BE /auth/login                         DB / store
│  POST /auth/login            │                                     │
│  { email, password }  ─────► │                                     │
│                              │ FindByEmailAsync + IsActive()       │
│                              │ CheckPasswordSignInAsync (lockout)  │
│                              │ GetTwoFactorEnabledAsync?           │
│                              │   └─ true → 202 { requiresTwoFactor }│
│                              │ IssueSessionAsync:                  │
│                              │   ├─ refreshTokens.IssueAsync ─────►│ INSERT refresh_token (family mới)
│                              │   ├─ Set-Cookie refresh_token (httpOnly)
│                              │   └─ IssueAccessTokenAsync:         │
│                              │        role name → role_id cố định  │
│                              │        issuer.Issue(...) → JWT      │
│  200 { accessToken, expiresAt } ◄──                               │
│                              │                                     │
│  GET /auth/me ─────────────► │ resolve role_id → permissions(Redis)│
│  200 { sub, roleId, role, permissions[] } ◄──                     │
│  authStore.setAuth(token) + setPermissions(...)                   │
```

Code: [`LoginAsync`](../../../src/api/Clawbot.Api/Endpoints/AuthEndpoints.cs#L43) →
[`IssueSessionAsync`](../../../src/api/Clawbot.Api/Endpoints/AuthEndpoints.cs#L243) →
[`IssueAccessTokenAsync`](../../../src/api/Clawbot.Api/Endpoints/AuthEndpoints.cs#L260).

Điểm cần nhớ:
- `CheckPasswordSignInAsync` chỉ kiểm password + lockout, **không** báo `RequiresTwoFactor` → 2FA phải
  check riêng bằng `GetTwoFactorEnabledAsync` ([AuthEndpoints.cs:61-65](../../../src/api/Clawbot.Api/Endpoints/AuthEndpoints.cs#L61-L65)).
- Role name của user → `role_id` cố định qua `RbacSeeder.RoleIds`; role lạ / 0 role → `Guid.Empty`
  → backend default-deny mọi endpoint có gate quyền.

## 3. Luồng 2FA (TOTP)

1. Login trả `202 { requiresTwoFactor: true }` nếu tài khoản bật 2FA.
2. FE gọi `POST /auth/login/2fa { email, password, code }`.
3. BE check lại password (không lockout) + `VerifyTwoFactorTokenAsync` → đúng thì `IssueSessionAsync`
   y hệt login thường. Code: [`LoginWithTwoFactorAsync`](../../../src/api/Clawbot.Api/Endpoints/AuthEndpoints.cs#L70).

Bật/tắt 2FA (cần đăng nhập): `/auth/2fa/enable` (sinh authenticator key + otpauth URI) →
`/auth/2fa/verify` (xác nhận code rồi `SetTwoFactorEnabledAsync(true)`) ; `/auth/2fa/disable` reset key.

## 4. Refresh & rotation

`POST /auth/refresh` (anonymous, đọc cookie `refresh_token`):

- Cookie rỗng → clear cookie + 401.
- `RotateAsync(raw)` → một trong các kết quả ([RefreshTokenService.cs](../../../src/shared/Clawbot.Infrastructure/Auth/RefreshTokenService.cs)):
  - **Success**: token hợp lệ chưa revoke → mint successor, đánh dấu token cũ `revoked + replaced_by`
    (one-time rotation). Cấp access token mới + set cookie mới.
  - **Reuse**: dùng lại token đã revoke ngoài grace window → coi là bị đánh cắp → **revoke cả family** → 401.
  - **Invalid**: không tồn tại / hết hạn → 401.
  - **Grace race (D10)**: token đã rotate được replay trong `GraceSeconds` (F5 đa tab) → cấp 1 sibling
    cùng family thay vì coi là theft.
- Sau rotate Success: re-check `user.IsActive()`; nếu account bị khoá → revoke all + 401.

Mỗi lần login = 1 **family** mới. Refresh token lưu **hash SHA-256**, không lưu raw.
Dọn token hết hạn: [RefreshTokenCleanupJob.cs](../../../src/shared/Clawbot.Infrastructure/Jobs/RefreshTokenCleanupJob.cs) (Hangfire).

## 5. Phân quyền (enforce) mỗi request

```
Request có gate quyền
  → RequirePermission("conversations:write")  [endpoint filter]
      ├─ đọc role_id từ JWT (Guid.Empty / thiếu → 403)
      ├─ IPermissionResolver.GetPermissionsAsync(roleId)
      │     ├─ Redis GET perm:role:{roleId}  (hit → trả luôn)
      │     └─ miss/Redis down → LoadFromDb (role_permissions ⨝ permissions)
      │                         → ghi Redis TTL 600s
      └─ code ∉ permissions → 403 { errorCode: "forbidden", message: "Không có quyền" }
```

Code: [PermissionEndpointExtensions.cs](../../../src/api/Clawbot.Api/Auth/PermissionEndpointExtensions.cs).
Endpoint không có entry §6a chỉ cần `RequireAuthorization()`, không enforce permission.
Map endpoint → permission code: xem §6a trong [SPEC.md](./SPEC.md).

## 6. Logout & reset

- `POST /auth/logout` (anonymous): có cookie thì `RevokeAsync` token đó, luôn clear cookie + 204
  (idempotent). Không cần access token còn hạn.
- `POST /auth/reset/request`: luôn trả 200 (chống dò email); token reset hiện log ra cho dev
  (TODO M03: gửi email).
- `POST /auth/reset/confirm`: đổi mật khẩu thành công → **revoke toàn bộ family** của user (ép re-login
  mọi thiết bị).

## 7. Frontend

- Access token **chỉ ở RAM** (Zustand [authStore.ts](../../../src/frontend/clawbot-web/src/shared/auth/authStore.ts)),
  không localStorage. F5 mất token → hydrate lại.
- Khởi động / F5: [AuthProvider](../../../src/frontend/clawbot-web/src/shared/auth/AuthContext.tsx) gọi
  `POST /auth/refresh` 1 lần; có token thì `loadPermissions()` (`GET /auth/me`); 401 → trạng thái `anon` (im lặng).
- [client.ts](../../../src/frontend/clawbot-web/src/shared/api/client.ts):
  - request interceptor gắn `Authorization: Bearer <token>`.
  - response interceptor: gặp 401 → `refreshAccessToken()` (single-flight) → retry request gốc 1 lần;
    refresh fail → clear store + chuyển `/login`.
  - `refreshClient` riêng (không có interceptor) để 401 trên `/auth/refresh` không đệ quy.
- `hasPermission(code)` chỉ để gate UI (ẩn/hiện menu, nút). **Backend vẫn là source-of-truth.**

## 8. Ngữ nghĩa thu hồi quyền (quan trọng)

| Thao tác | Hiệu lực |
| --- | --- |
| **Đổi permission của 1 role** (qua `PUT /api/rbac/roles/{id}/permissions`) | **Ngay lập tức.** Lưu DB xong gọi `InvalidateAsync` → DEL Redis `perm:role:{id}` ([RolesEndpoints.cs:138](../../../src/api/Clawbot.Api/Endpoints/RolesEndpoints.cs#L138)). Request kế tiếp resolve lại từ DB. |
| **Sửa thẳng `role_permissions` trong DB** (không qua API) | Trễ tối đa **600s** (TTL Redis) vì không có invalidate. |
| **Đổi role gán cho user** | **Không tức thì.** `role_id` nằm trong JWT → chờ access token hết hạn + refresh, hoặc revoke refresh-token của user để ép re-login. |
| **Khoá / vô hiệu hoá tài khoản** | Access token còn hạn vẫn qua endpoint chỉ cần auth. Chặn triệt để khi token hết hạn: `/auth/refresh` re-check `IsActive()` → 401. Muốn ngay thì `RevokeAllForUserAsync`. |

> Hệ quả thiết kế: nên giữ **access token ngắn** (vài phút) để 2 dòng cuối tiệm cận "tức thì".

## 9. Endpoint tóm tắt

| Method | Path | Auth | Ghi chú |
| --- | --- | --- | --- |
| POST | `/auth/login` | anon | 200 token / 202 cần 2FA / 401 / 423 locked |
| POST | `/auth/login/2fa` | anon | xác thực code TOTP |
| POST | `/auth/refresh` | anon (cookie) | rotate, trả access token mới |
| POST | `/auth/logout` | anon (cookie) | idempotent, 204 |
| POST | `/auth/reset/request` | anon | luôn 200 |
| POST | `/auth/reset/confirm` | anon | revoke all family |
| POST | `/auth/2fa/enable\|verify\|disable` | authed | quản lý 2FA |
| GET | `/auth/me` | authed | `{ sub, roleId, role, permissions[] }` |
