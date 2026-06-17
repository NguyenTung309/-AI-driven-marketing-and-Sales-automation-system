---
phase: planning
title: Agent Business-Flow Gaps — Implementation Plan
feature: agent-flow-gaps
date: 2026-06-13
status: implemented (Chat-2 code path wired; live Pancake payload verification pending)
branch: feature-analytics-kpi
requirements: ./../requirements/2026-06-13-feature-agent-flow-gaps.md
design: ./../design/2026-06-13-feature-agent-flow-gaps.md
---

# Agent Business-Flow Gaps — Implementation Plan

> 0 missing code gaps + remaining external/live-verification partials from the 2026-06-13 audit. Decisions: least-busy **replaces** round-robin · Lead-2/3 **event-driven via WS1 outbox** (prereq) · Ads-1 **hourly+throttle** · Chat-2 **Pancake comment path wired, live webhook payload still needs ops verification**. Alerts via SignalR (no Telegram). Verify gate: `dotnet build Clawbot.sln` 0/0 + `dotnet test` green (250 baseline) after each task.

## Status (2026-06-13 — implemented)
- **Phase 0 (Chat-2 spike):** ✅ CODE WIRED — design spike confirmed Pancake COMMENT conversations; adapter/job path implemented. Live tenant webhook payload still needs ops verification.
- **Phase 1 (WS1 outbox):** ✅ DONE (commit `82354fd`/earlier) — events, interceptor (pre-save → outbox), `AddEntityFrameworkOutbox`, migration `0015`. *Runtime relay needs SQL Server + RabbitMQ (Docker) to verify end-to-end.*
- **Phase 2 (lead consumers + least-busy):** ✅ DONE — `LeastBusyLeadAssignmentService` (replaces round-robin), `LeadBecameHot/WarmConsumer`, seeded warm drip.
- **Phase 3 (independent):** ✅ DONE for code gaps — 3.1 competitor monitor · 3.2 upsell · 3.3 idle tier-2 · 3.4 report delta · 3.5 docs quote completion · 3.6 ads hourly+budget · 3.7 research TZ · 3.8 chat comment schema/parse/job.
- **Verify:** build 0/0; unit tests green (Domain 47 / Agents 148 / Api 28 / Infra 55 / AgentService 6 / Application 1). Migrations `0015–0019` need a SQL Server run (Docker/CI) to validate DDL.

## Task queue (ordered by phase)

### Phase 0 — Chat-2 Pancake spike *(code wired · live verification pending)*
- [x] **T0.1** Inspect Pancake webhook payload: does it deliver comment events (post_id, comment-vs-DM discriminator)? — design spike recorded COMMENT conversation with `post_id`; live tenant payload still needs ops sample verification.
- [x] **T0.2** Confirm Pancake send-DM API exists (open parallel DM thread to a commenter) — design spike confirmed messages on COMMENT conversation are the feasible Pancake path.
- [x] **T0.3** Record spike verdict in design doc → choose flow shape: full (comment reply + DM) | reply-only | defer Chat-2
- **Effort S · Risk: needs Pancake account/docs access (user-provided)**

### Phase 1 — WS1 messaging (MassTransit EF outbox) *(prerequisite for Lead-2/3)*
- [x] **T1.1** Define event contracts: `LeadBecameHot`, `LeadBecameWarm` in Domain lead events
- [x] **T1.2** `DomainEventDispatchInterceptor` publishes `AggregateRoot.DomainEvents` during SaveChanges and clears them
- [x] **T1.3** `AddEntityFrameworkOutbox<AppDbContext>` in `DependencyInjection.cs`; consumers configured for RabbitMQ-backed relay
- [x] **T1.4** Migration `0015_masstransit_outbox.sql` — InboxState/OutboxState/OutboxMessage (no `GO`)
- [x] **T1.5** `Lead.AdjustScore` raises `LeadBecameHot/Warm` on stage transition
- [x] **T1.6** Tests cover AdjustScore hot/warm events plus `LeadBecameHot/WarmConsumer` behavior
- **Effort L · Risk: outbox migration + analyzer gate (pin clean MassTransit.EFCore pkg)**

### Phase 2 — Lead consumers + least-busy *(depends Phase 1)*
- [x] **T2.1** Change `IAssignmentPoolSource` contract → return per-sale load (open conv + open warm/hot leads)
- [x] **T2.2** `LeastBusyLeadAssignmentService : ILeadAssignmentService` (min-load pick; exclude offline/inactive); **replace** RoundRobin registration; update all callers atomically
- [x] **T2.3** `LeadBecameHotConsumer` — if unassigned → least-busy assign + `INotificationPublisher` (hot-lead alert to owner)
- [x] **T2.4** `LeadBecameWarmConsumer` — if no active enrollment → `DripEnrollment.Enroll` into default warm sequence (idempotent)
- [x] **T2.5** Seed default warm `DripSequence` (TriggerEvent='warm_lead', 4 steps/7d) in `RbacSeeder` per-tenant
- [x] **T2.6** Tests: hot→assign+notify; warm→enroll once; least-busy picks min-load
- **Effort M**

### Phase 3 — Independent items *(parallelizable; no cross-deps)*
- [x] **T3.1 Research-2 competitor** — `CompetitorSource`/`CompetitorPost` entities + cfg; `CompetitorScanJob` (daily 06:00 VN) wrapping `RssCompetitorMonitor`; CRUD `/api/competitors/sources` + `GET /posts`; dedupe by hash; alert on new. **Effort M**
- [x] **T3.2 SaleAssist-4 upsell** — `SaleAssistAgent.SuggestUpsellAsync` hot-gated by conversation lead; `/api/sale-assist/upsell-suggestions` now calls dynamic per-conversation upsell and returns fallback reason on service errors. **Effort S**
- [x] **T3.3 SaleAssist-3 idle tier-2** — `IdleConversationAlertJob` +10min threshold → notify `SalesLead` role users; no dup vs 5-min tier. **Effort S**
- [x] **T3.4 Report-1 delta** — `AnalyticsEndpoints` `/omnichannel-delta?compare=dod|wow` → prior-day/week % from `kpi_daily`. **Effort S**
- [x] **T3.5 Docs-1 quote complete** — migration `0018` ExpiresAt; auto-extract Contact name/phone→template Vars; set ExpiresAt=now+7d; download path 410 on expiry; gated send (SMTP + Zalo-via-Pancake). **Effort M**
- [x] **T3.6 Ads-1 hourly+budget** — cron `0 * * * *`; `AdsRuleEngine` proactive rule spend/budget≥0.9→alert (reuse `ads_budget`); `AdsPlatformThrottle` serializes outbound calls per platform; HTTP policy retries 429. **Effort S**
- [x] **T3.7 Research-1 TZ explicit** — `WeeklyTrendScanJob` pass explicit Vietnam `TimeZoneInfo` to `RecurringJobOptions` for 07:00 VN local. **Effort XS**
- [x] **T3.8 Chat-2 schema** — migration `0015` messages +message_type/+parent_post_id; `ChannelMessage` + Pancake parse populate; `KeywordIntentClassifier` +`purchase_intent`; webhook enqueues `CommentAutoReplyJob` for comment events. **Effort M**

## Dependencies
- T1.* → T2.* (consumers need outbox).
- Live Pancake tenant payload verification → may require field-name adjustment in `PancakeChannelAdapter.ParseAsync`; code path is covered by tests against the documented COMMENT shape.
- T3.* independent of each other (can interleave).

## Risks
- WS1 outbox migration adds 3 tables; MassTransit.EntityFrameworkCore pkg must clear NuGetAudit [[clawbot-build-gates]].
- Least-busy contract change is in-repo breaking → update all callers same change.
- Chat-2 runtime remains ops-sensitive until a real tenant webhook/comment sample verifies the Pancake field names and send semantics.
- Ads hourly quota risk is mitigated by per-platform throttling + HTTP 429 backoff; real Meta/TikTok quota still needs ops review before high-volume rollout.
- Migrations: no `GO`; ALTER-col indexes own file [[clawbot-migration-no-go]].

## Verification (per task)
`dotnet build Clawbot.sln` 0/0 · `dotnet test` green (no regression on 250) · new unit tests per task ≥80% new-logic branches · `/update-planning` after each.
