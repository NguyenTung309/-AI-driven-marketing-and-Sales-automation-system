---
phase: planning
title: Agent Business-Flow Gaps — Implementation Plan
feature: agent-flow-gaps
date: 2026-06-13
status: in-progress
branch: feature-analytics-kpi
requirements: ./../requirements/2026-06-13-feature-agent-flow-gaps.md
design: ./../design/2026-06-13-feature-agent-flow-gaps.md
---

# Agent Business-Flow Gaps — Implementation Plan

> 3 missing + 7 actionable partials from the 2026-06-13 audit. Decisions: least-busy **replaces** round-robin · Lead-2/3 **event-driven via WS1 outbox** (prereq) · Ads-1 **hourly+throttle** · Chat-2 **spike-gated**. Alerts via SignalR (no Telegram). Verify gate: `dotnet build Clawbot.sln` 0/0 + `dotnet test` green (250 baseline) after each task.

## Task queue (ordered by phase)

### Phase 0 — Chat-2 Pancake spike *(BLOCKING gate · external dep)*
- [ ] **T0.1** Inspect Pancake webhook payload: does it deliver comment events (post_id, comment-vs-DM discriminator)? — read adapter + sample payloads / Pancake docs
- [ ] **T0.2** Confirm Pancake send-DM API exists (open parallel DM thread to a commenter)
- [ ] **T0.3** Record spike verdict in design doc → choose flow shape: full (comment reply + DM) | reply-only | defer Chat-2
- **Effort S · Risk: needs Pancake account/docs access (user-provided)**

### Phase 1 — WS1 messaging (MassTransit EF outbox) *(prerequisite for Lead-2/3)*
- [ ] **T1.1** Define event contracts (`Contracts.Events`): `LeadBecameHot`, `LeadBecameWarm` (+ existing `LeadCreated`)
- [ ] **T1.2** `DomainEventPublishInterceptor` — SaveChanges interceptor maps `AggregateRoot.DomainEvents` → outbox in same tx, then clears
- [ ] **T1.3** `AddEntityFrameworkOutbox<AppDbContext>` in `DependencyInjection.cs`; `ConfigureEndpoints`
- [ ] **T1.4** Migration `0019_masstransit_outbox.sql` — InboxState/OutboxState/OutboxMessage (no `GO`; indexes own file if needed)
- [ ] **T1.5** `Lead.AdjustScore` → `Raise(LeadBecameHot/Warm)` on stage transition (only on change)
- [ ] **T1.6** Test: in-memory bus harness — AdjustScore→hot publishes `LeadBecameHot`
- **Effort L · Risk: outbox migration + analyzer gate (pin clean MassTransit.EFCore pkg)**

### Phase 2 — Lead consumers + least-busy *(depends Phase 1)*
- [ ] **T2.1** Change `IAssignmentPoolSource` contract → return per-sale load (open conv + open warm/hot leads)
- [ ] **T2.2** `LeastBusyLeadAssignmentService : ILeadAssignmentService` (min-load pick; exclude offline/inactive); **replace** RoundRobin registration; update all callers atomically
- [ ] **T2.3** `LeadBecameHotConsumer` — if unassigned → least-busy assign + `INotificationPublisher` (hot-lead alert to owner)
- [ ] **T2.4** `LeadBecameWarmConsumer` — if no active enrollment → `DripEnrollment.Enroll` into default warm sequence (idempotent)
- [ ] **T2.5** Seed default warm `DripSequence` (TriggerEvent='warm_lead', 4 steps/7d) in `RbacSeeder` per-tenant
- [ ] **T2.6** Tests: hot→assign+notify; warm→enroll once; least-busy picks min-load
- **Effort M**

### Phase 3 — Independent items *(parallelizable; no cross-deps)*
- [ ] **T3.1 Research-2 competitor** — `CompetitorSource`/`CompetitorPost` entities + cfg; migrations `0016`/`0017`; `CompetitorScanJob` (daily, GMT+7) wrapping `RssCompetitorMonitor`; CRUD `/api/competitors/sources` + `GET /posts`; dedupe by hash; alert on new. **Effort M**
- [ ] **T3.2 SaleAssist-4 upsell** — `SaleAssistAgent.SuggestUpsellAsync` (gate Stage=='hot' + Claude closing-signal); rewrite `GET /api/sale-assist/upsell?conversationId=` (drop static string); LLM-error fallback. **Effort S**
- [ ] **T3.3 SaleAssist-3 idle tier-2** — `IdleConversationAlertJob` +10min threshold → notify Sales Lead role; no dup vs 5-min tier. **Effort S**
- [ ] **T3.4 Report-1 delta** — `AnalyticsEndpoints` omnichannel `?compare=dod|wow` → prior-day/week % from `kpi_daily`. **Effort S**
- [ ] **T3.5 Docs-1 quote complete** — migration `0018` ExpiresAt; auto-extract Contact name/phone→template Vars; set ExpiresAt=now+7d; download path 410 on expiry; gated send (SMTP + Zalo-via-Pancake). **Effort M**
- [ ] **T3.6 Ads-1 hourly+budget** — cron `0 */4`→`0 * * * *`; `AdsRuleEngine` proactive rule spend/budget≥0.9→alert (reuse `ads_budget`); per-platform throttle + 429 backoff. **Effort S** *(verify Meta/TikTok quota first)*
- [ ] **T3.7 Research-1 TZ explicit** — `WeeklyTrendScanJob` pass explicit GMT+7 `TimeZoneInfo` to `RecurringJobOptions` (already ~07:00 VN; encode intent). **Effort XS**
- [ ] **T3.8 Chat-2 schema** — migration `0015` messages +message_type/+parent_post_id; `ChannelMessage` + Pancake parse populate; `KeywordIntentClassifier` +`purchase_intent`. *(comment-reply+DM flow gated on T0.3)* **Effort M**

## Dependencies
- T1.* → T2.* (consumers need outbox).
- T0.3 verdict → gates T3.8 reply/DM flow (schema T3.8 lands regardless).
- T3.* independent of each other (can interleave).

## Risks
- WS1 outbox migration adds 3 tables; MassTransit.EntityFrameworkCore pkg must clear NuGetAudit [[clawbot-build-gates]].
- Least-busy contract change is in-repo breaking → update all callers same change.
- Chat-2 fully blocked if Pancake lacks send-DM (T0).
- Ads hourly may hit Meta/TikTok rate-limit (T3.6 gate).
- Migrations: no `GO`; ALTER-col indexes own file [[clawbot-migration-no-go]].

## Verification (per task)
`dotnet build Clawbot.sln` 0/0 · `dotnet test` green (no regression on 250) · new unit tests per task ≥80% new-logic branches · `/update-planning` after each.
