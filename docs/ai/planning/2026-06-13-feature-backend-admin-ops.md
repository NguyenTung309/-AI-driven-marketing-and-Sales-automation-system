---
phase: planning
title: Planning — Backend Admin Ops (M23/M24/M25)
description: Task queue + blocker log for account/user-admin, notification center, agent control
---

# Planning — Backend Admin Ops

> Requirements: [module-checklist.md](../../module-checklist.md) M23/M24/M25. Design: [2026-06-13-feature-backend-admin-ops.md](../design/2026-06-13-feature-backend-admin-ops.md).
> Quy ước: `todo` · `in-progress` · `done` · `blocked` · `skipped`.

## ⛔ BLOCKER (chặn M23) — Identity schema chưa reconcile

**Phát hiện 2026-06-13 (lúc execute):** EF Identity (`AppUser`/`AppRole`) **không có `ToTable`** → map mặc định `AspNetUsers`/`AspNetRoles`/`AspNetUserRoles`/… NHƯNG `deploy/migrations/*.sql` (DDL = source of truth) **không định nghĩa bảng `AspNet*` nào** — chỉ có `users`/`roles`/`permissions` (domain). Unit test `EnsureCreated()` tạo `AspNet*`; integration test apply DDL tạo `users`. → 2 schema lệch; auth thật chưa chạy e2e trên DB DDL (harness bypass qua `TestAuthHandler`).

**Phải chốt trước khi M23:**
- **Option A** — Map Identity → DDL: thêm `ToTable("users"/"roles"/"user_roles"/…)` + `HasColumnName` cho toàn bộ AppUser/AppRole/UserRole/UserClaim/UserLogin/UserToken/RoleClaim trong 1 `IdentityModelConfiguration`; bổ sung DDL cho các bảng Identity còn thiếu (user_roles, user_claims, user_tokens…). Giữ DDL-as-source.
- **Option B** — Dùng `AspNet*` mặc định: thêm DDL `deploy/migrations/0010_identity_tables.sql` tạo đủ bảng `AspNet*`; bỏ/đánh dấu vestigial bảng `users` DDL. Ít map nhưng lệch convention snake_case.
- **Option C** — EF migration cho Identity (bỏ DDL-as-source riêng cho Identity).

→ **Cần user quyết.** M23 task queue dưới giả định đã chốt.

## M23 — Account & User administration  · **BLOCKED** (chờ quyết Identity)
- [blocked] T0 — Reconcile Identity↔DDL (Option A/B/C ở trên) + migration + EnsureCreated/DDL parity
- [todo] T1 — `0010` ALTER user table `+ date_of_birth`, `+ avatar_url`; thêm `DateOfBirth`/`AvatarUrl` vào `AppUser`
- [todo] T2 — `SmtpEmailSender : IEmailSender` (System.Net.Mail BCL, config-gated graceful) + DI; wire vào `/auth/reset/request`
- [todo] T3 — `AdminUsersEndpoints` `/api/admin/users` GET(list)/POST(create)/PUT/{id}/disable/reset-password (perm `admin.system`) qua `UserManager`
- [todo] T4 — `ProfileEndpoints` `GET/PUT /api/profile` + `POST /api/profile/avatar` (MinIO presigned, reuse `MinioDocumentStorage`)
- [todo] T5 — `POST /auth/change-password` (authenticated) vào `AuthEndpoints` (`UserManager.ChangePasswordAsync`)
- [todo] T6 — DTOs trong `Clawbot.Api.Contracts/Admin` + `Account`; map `MapAdminUsers()`/`MapProfile()` trong Program.cs
- [todo] T7 — build 0/0 + tests (admin user CRUD, change-password, profile)

## M24 — Notification center backend  · **DONE 2026-06-13** (build 0/0)
- [done] T1 — `0011_notifications.sql` (tenant_id, user_id NULL, type, severity, title, body, link, is_read, read_at, created_at; index tenant/user/is_read/created)
- [done] T2 — `Notification` entity (factory `Create`, `MarkRead`) + `NotificationConfiguration` (`ToTable("notifications")`) + DbSet
- [done] T3 — `INotificationPublisher` (SharedKernel) + `DbNotificationPublisher` (persist + SignalR push per-user/tenant group); `NotificationHub` `/hubs/notifications`; DI registered
- [done] T5 — `NotificationsEndpoints` `GET /api/notifications`(+`/unread-count`) + `POST /{id}/read` + `/read-all`; mapped Program.cs
- [todo] T4 — refactor `IInboxNotifier`/`IContentNotifier` alert path gọi qua publisher (additive; chưa làm)
- [todo] T6 — `RetentionPurgeJob` purge notifications >90d (chưa làm)
- [~] T7 — build **0/0 green** ✓ ; unit tests chưa thêm
- **Note**: broadcast (user_id null) = 1 row + tenant push (per-user fan-out defer tới khi Identity reconcile — cần list user).

## M25 — Agent control & observability  · **READY** (không dính Identity)
- [todo] T1 — `0012_agent_settings.sql` UNIQUE(tenant_id, agent_code), auto_action_enabled, updated_at/by
- [todo] T2 — `0013_claude_cost_ledger.sql` (tenant_id, agent_code, conversation_id NULL, input/output tokens, usd, created_at; index)
- [todo] T3 — `AgentSetting` + `ClaudeCostEntry` entities + EF configs + DbSets
- [todo] T4 — `DbClaudeCostTracker : IClaudeCostTracker` (ghi ledger) thay/đứng cạnh `InMemoryClaudeCostTracker`; DI swap
- [todo] T5 — AgentService: ChatAgent + jobs đọc `agent_settings.auto_action_enabled` trước auto-action
- [todo] T6 — `AgentsEndpoints` `GET /api/agents`(+status từ traces) + enable/disable (perm `agent.manage`) + `GET /{code}/traces`
- [todo] T7 — `GET /api/analytics/agent-cost` group ledger theo agent_code
- [todo] T8 — build 0/0 + tests

## Sequencing note
User chọn build cả 3 tuần tự. Do M23 blocked, đề xuất: **(1) chốt Identity reconcile → M23**, hoặc **(2) build M24 → M25 trước** (ready ngay) rồi quay lại M23. Phụ (draft-feedback M14, least-load M15) sau.
