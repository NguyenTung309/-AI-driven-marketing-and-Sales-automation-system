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

## M23 — Account & User administration  · **IN PROGRESS** — reconcile DONE 2026-06-13 (build 0/0, Infra 51/51)
- [done] T0 — **Identity↔DDL reconcile (Option A)**: `IdentityUserConfiguration` maps `AppUser`→`users` (bảng cả domain FK tới); `0013_identity_reconcile.sql` ALTER `users` (+8 cột Identity + date_of_birth/avatar_url) + CREATE 6 bảng `AspNet*`. Model verify qua EnsureCreated ✓. **⚠️ DDL 0013 cần integration auth test (Docker) verify trước prod** — fix luôn lỗi prod-auth tiềm ẩn (EF cần AspNetUsers, DDL chỉ có users).
- [done] T1 — `DateOfBirth`/`AvatarUrl`/`IsActive`/`LastLoginAt` thêm vào `AppUser` + map
- [todo] T2 — `SmtpEmailSender` (System.Net.Mail BCL, config-gated) + wire `/auth/reset/request`
- [todo] T2 — `SmtpEmailSender : IEmailSender` (System.Net.Mail BCL, config-gated graceful) + DI; wire vào `/auth/reset/request`
- [done] T2 — `SmtpEmailSender : IEmailSender` (System.Net.Mail BCL, config-gated) + DI; wired vào `/auth/reset/request`
- [done] T3 — `AdminUsersEndpoints` `/api/admin/users` GET/POST/PUT/{id}/{disable,enable,reset-password} (perm `admin.system`) qua `UserManager`
- [done] T4 — `ProfileEndpoints` `GET/PUT /api/profile`. **Avatar `POST /api/profile/avatar` DEFER** — `IDocumentStorage` chỉ đăng ký ở AgentService, cần đăng ký ở API + multipart.
- [done] T5 — `POST /auth/change-password` (authenticated) trong `AuthEndpoints` + `ChangePasswordRequest`
- [done] T6 — request records trong endpoint files + Contracts; map `MapAdminUsers()`/`MapProfile()` Program.cs
- [done] T7 — build **0/0**. Unit/integration auth tests chưa thêm (cần Docker cho DDL-path).
- [todo] avatar upload (MinIO) — follow-up.

## M24 — Notification center backend  · **DONE 2026-06-13** (build 0/0)
- [done] T1 — `0011_notifications.sql` (tenant_id, user_id NULL, type, severity, title, body, link, is_read, read_at, created_at; index tenant/user/is_read/created)
- [done] T2 — `Notification` entity (factory `Create`, `MarkRead`) + `NotificationConfiguration` (`ToTable("notifications")`) + DbSet
- [done] T3 — `INotificationPublisher` (SharedKernel) + `DbNotificationPublisher` (persist + SignalR push per-user/tenant group); `NotificationHub` `/hubs/notifications`; DI registered
- [done] T5 — `NotificationsEndpoints` `GET /api/notifications`(+`/unread-count`) + `POST /{id}/read` + `/read-all`; mapped Program.cs
- [todo] T4 — refactor `IInboxNotifier`/`IContentNotifier` alert path gọi qua publisher (additive; chưa làm)
- [todo] T6 — `RetentionPurgeJob` purge notifications >90d (chưa làm)
- [~] T7 — build **0/0 green** ✓ ; unit tests chưa thêm
- **Note**: broadcast (user_id null) = 1 row + tenant push (per-user fan-out defer tới khi Identity reconcile — cần list user).

## M25 — Agent control & observability  · **DONE 2026-06-13** (build 0/0, Infra tests 51/51) — trừ T5
- [skip] T1 — ~~`agent_settings`~~ : **reuse `AgentConfig`** (table `agents`, `Status` running/stopped + `Start()/Stop()`) — không cần bảng mới
- [done] T2 — `0012_claude_cost_ledger.sql`
- [done] T3 — `ClaudeCostEntry` entity + `ClaudeCostEntryConfiguration` + DbSet `ClaudeCostLedger`
- [done] T4 — `DbClaudeCostTracker` (IServiceScopeFactory → scoped DbContext, singleton-safe); override InMemory trong AgentService Program.cs **sau** `AddClawbotSkills` (RemoveAll + AddSingleton)
- [done] T6 — `AgentsEndpoints`: `GET /api/agents` (+lastRunAt) · `POST /{code}/enable|disable` (Start/Stop) · `GET /{code}/traces`
- [done] T7 — `GET /api/analytics/agent-cost` (group `claude_cost_ledger` theo agent_code)
- [done] T8 — build **0/0**; `Clawbot.Infrastructure.Tests` 51/51 green
- [done] T5 — ChatAgent honor flag: `IAgentToggleGate` (Agents.Core, default always-on) + `DbAgentToggleGate` (reads `AgentConfig.Status`); ChatAgent skips auto-reply khi disabled. Build 0/0, Agents.Tests 147/147.

## Sequencing note
User chọn build cả 3 tuần tự. Do M23 blocked, đề xuất: **(1) chốt Identity reconcile → M23**, hoặc **(2) build M24 → M25 trước** (ready ngay) rồi quay lại M23. Phụ (draft-feedback M14, least-load M15) sau.
