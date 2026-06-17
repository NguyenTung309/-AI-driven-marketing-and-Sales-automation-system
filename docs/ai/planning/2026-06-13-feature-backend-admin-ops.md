---
phase: planning
title: Planning — Backend Admin Ops (M23/M24/M25)
description: Task queue + blocker log for account/user-admin, notification center, agent control
---

# Planning — Backend Admin Ops

> Requirements: [module-checklist.md](../../module-checklist.md) M23/M24/M25. Design: [2026-06-13-feature-backend-admin-ops.md](../design/2026-06-13-feature-backend-admin-ops.md).
> Quy ước: `todo` · `in-progress` · `done` · `blocked` · `skipped`.

## RESOLVED — Identity schema reconciled for M23

**Resolved 2026-06-13:** Option A was implemented. `AppUser` maps to the DDL-backed `users` table, Identity support tables are present through the reconcile migrations, and offline DDL preflight is covered by `deploy/ci/verify-identity-ddl.ps1`. SQL Server auth e2e still requires Docker/Testcontainers before production sign-off.

## M23 — Account & User administration  · **DONE 2026-06-13** (build 0/0; SQL Server auth e2e pending Docker)
- [done] T0 — **Identity↔DDL reconcile (Option A)**: `IdentityUserConfiguration` maps `AppUser`→`users` (bảng cả domain FK tới); `0013_identity_reconcile.sql` ALTER `users` (+8 cột Identity + date_of_birth/avatar_url) + CREATE 6 bảng `AspNet*`. Model verify qua EnsureCreated ✓. **⚠️ DDL 0013 cần integration auth test (Docker) verify trước prod** — fix luôn lỗi prod-auth tiềm ẩn (EF cần AspNetUsers, DDL chỉ có users).
- [done] T1 — `DateOfBirth`/`AvatarUrl`/`IsActive`/`LastLoginAt` thêm vào `AppUser` + map
- [done] T2 — `SmtpEmailSender : IEmailSender` (System.Net.Mail BCL, config-gated) + DI; wired vào `/auth/reset/request`
- [done] T3 — `AdminUsersEndpoints` `/api/admin/users` GET/POST/PUT/{id}/{disable,enable,reset-password} (perm `admin.system`) qua `UserManager`
- [done] T4 — `ProfileEndpoints` `GET/PUT /api/profile` + avatar upload `POST /api/profile/avatar` wired through API document storage.
- [done] T5 — `POST /auth/change-password` (authenticated) trong `AuthEndpoints` + `ChangePasswordRequest`
- [done] T6 — request records trong endpoint files + Contracts; map `MapAdminUsers()`/`MapProfile()` Program.cs
- [done] T7 — build/tests green; SQL Server auth e2e remains pending Docker/Testcontainers for the DDL path.
- [done] avatar upload (MinIO/document storage) — `POST /api/profile/avatar` wired.

## M24 — Notification center backend  · **DONE 2026-06-13** (build 0/0)
- [done] T1 — `0011_notifications.sql` (tenant_id, user_id NULL, type, severity, title, body, link, is_read, read_at, created_at; index tenant/user/is_read/created)
- [done] T2 — `Notification` entity (factory `Create`, `MarkRead`) + `NotificationConfiguration` (`ToTable("notifications")`) + DbSet
- [done] T3 — `INotificationPublisher` (SharedKernel) + `DbNotificationPublisher` (persist + SignalR push per-user/tenant group); `NotificationHub` `/hubs/notifications`; DI registered
- [done] T5 — `NotificationsEndpoints` `GET /api/notifications`(+`/unread-count`) + `POST /{id}/read` + `/read-all`; mapped Program.cs
- [done] T4 — content alert path routes through `PublishingContentNotifier` (SignalR + notification persistence); inbox alert persistence remains explicit in `IdleConversationAlertJob` to avoid persisting every inbox realtime event.
- [done] T6 — `RetentionPurgeJob` purge notifications >90d; covered by `RetentionPurgeJobTests`
- [done] T7 — build/tests green; notification persistence and retention are covered.
- **Note**: broadcast (`user_id` null) remains a single persisted tenant-wide row plus tenant SignalR push by design.

## M25 — Agent control & observability  · **DONE 2026-06-13** (build 0/0, Infra tests green)
- [skip] T1 — ~~`agent_settings`~~ : **reuse `AgentConfig`** (table `agents`, `Status` running/stopped + `Start()/Stop()`) — không cần bảng mới
- [done] T2 — `0012_claude_cost_ledger.sql`
- [done] T3 — `ClaudeCostEntry` entity + `ClaudeCostEntryConfiguration` + DbSet `ClaudeCostLedger`
- [done] T4 — `DbClaudeCostTracker` (IServiceScopeFactory → scoped DbContext, singleton-safe); override InMemory trong AgentService Program.cs **sau** `AddClawbotSkills` (RemoveAll + AddSingleton)
- [done] T6 — `AgentsEndpoints`: `GET /api/agents` (+lastRunAt) · `POST /{code}/enable|disable` (Start/Stop) · `GET /{code}/traces`
- [done] T7 — `GET /api/analytics/agent-cost` (group `claude_cost_ledger` theo agent_code)
- [done] T8 — build **0/0**; `Clawbot.Infrastructure.Tests` 51/51 green
- [done] T5 — ChatAgent honor flag: `IAgentToggleGate` (Agents.Core, default always-on) + `DbAgentToggleGate` (reads `AgentConfig.Status`); ChatAgent skips auto-reply khi disabled. Build 0/0, Agents.Tests 147/147.

## Sequencing note
M23/M24/M25 are implemented. Remaining verification for this planning track is the Docker-backed SQL Server auth e2e path, which is covered by the CI/Testcontainers preflight but cannot run locally until Docker is available.
