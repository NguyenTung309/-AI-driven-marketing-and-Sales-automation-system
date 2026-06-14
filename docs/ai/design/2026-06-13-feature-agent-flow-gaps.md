---
phase: design
title: Agent Business-Flow Gaps — System Design & Architecture
feature: agent-flow-gaps
date: 2026-06-13
status: in-review
requirements: ./../requirements/2026-06-13-feature-agent-flow-gaps.md
---

# Agent Business-Flow Gaps — System Design & Architecture

> Closes 3 missing + 7 actionable partials from the 2026-06-13 audit. Resolved forks: Chat-2 **spike-first**, upsell **hybrid hot+LLM**, competitor **admin-CRUD per-tenant**. All alerts via **SignalR/in-app** (no Telegram). Reuses existing infra (`INotificationPublisher`, Hangfire, `DripSequence*`, `RssCompetitorMonitor`, `IDocumentStorage`, `IClaudeChatClient`, `ILeadAssignmentService`).

## Architecture Overview

```mermaid
graph TD
  subgraph Ingest
    WH[WebhookEndpoints] -->|Pancake event| ING[ChannelMessageIngestor]
    ING -->|comment + purchase_intent| CAR[CommentAutoReplyJob/path]
    CAR -->|reply comment + open DM| PA[PancakeChannelAdapter]
  end

  subgraph Agents
    SA[SaleAssistAgent.SuggestUpsell] -->|hot-lead conv| LLM[(IClaudeChatClient)]
    RA[ResearchAgent] -.weekly.- TZ[GMT+7 cron]
    ADS[AdsRuleEngine + budget-ratio]
  end

  subgraph Jobs[Hangfire]
    CS[CompetitorScanJob daily] --> RCM[RssCompetitorMonitor]
    IDLE[IdleConversationAlertJob 5/10min]
    DRIP[DripSequenceJob] 
    ADSJOB[AdsRuleEvaluationJob hourly] --> ADS
  end

  subgraph Data[(SQL Server)]
    MSG[messages +type/+post_id]
    CSRC[competitor_sources]
    CPOST[competitor_posts]
    GDOC[generated_documents +expires_at]
    DENR[drip_enrollments]
    KPI[kpi_daily]
  end

  RCM --> CPOST
  CS -->|new posts| NP[INotificationPublisher]
  IDLE -->|tier2 10min| NP
  LeadFlow[RecordActivity hot transition] -->|assign least-busy + notify| NP
  LeadFlow -->|warm 30-69| DENR
  NP --> HUB[NotificationHub SignalR]
  ANALYTICS[AnalyticsEndpoints +delta] --> KPI
  DOCS[DocumentsEndpoints] --> GDOC
  DOCS -->|send gated| EMAIL[IEmailSender SMTP] & PA
```

### Key components & responsibilities
- **CommentAutoReplyJob / ingest path** (Chat-2) — detect comment w/ purchase intent → reply in-comment + open DM. **Gated by Pancake spike.**
- **SaleAssistAgent.SuggestUpsellAsync** (SaleAssist-4) — hybrid: gate on `Lead.Stage=='hot'`, then Claude reads conversation → closing-signal + contextual upsell.
- **CompetitorScanJob + CompetitorSource/Post** (Research-2) — activate orphaned `RssCompetitorMonitor`; admin-managed sources; persist posts; alert.
- **Lead hot-transition handler** (Lead-2) — on `RecordActivityAsync` → `Stage=='hot'` & unassigned → least-busy assign + notify.
- **Warm-lead drip enroll** (Lead-3) — on `Stage=='warm'` & not enrolled → `DripEnrollment.Enroll` into default warm sequence.
- **IdleConversationAlertJob tier-2** (SaleAssist-3) — add 10-min threshold → notify Sales Lead role.
- **WeeklyTrendScanJob TZ** (Research-1) — make GMT+7 explicit.
- **AnalyticsEndpoints delta** (Report-1) — dod/wow % from `kpi_daily`.
- **DocumentsEndpoints + GeneratedDocument.ExpiresAt** (Docs-1) — extract contact info, 7-day link, gated send.
- **AdsRuleEvaluationJob hourly + AdsRuleEngine budget-ratio** (Ads-1).

### Tech choices
- **WS1 messaging FIRST (prerequisite phase)** — wire MassTransit **EF transactional outbox** + domain-event publish interceptor + consumers BEFORE Lead-2/Lead-3. Lead-2 (`LeadBecameHot`→assign+notify) and Lead-3 (`LeadBecameWarm`→drip enroll) ship as **real consumers**, not inline handlers. Matches T-2/T-3 architecture. New migration for `InboxState`/`OutboxState`/`OutboxMessage`.
- **Reuse `RssCompetitorMonitor`** as-is (already parses RSS → `CompetitorPost` record); add persistence + scheduling around it.
- **Claude (`IClaudeChatClient`)** for upsell — already wired, cost tracked via ledger.

## Data Models

### Migrations (DDL-as-source, no `GO`, one SqlCommand/file)
| File | Change |
|---|---|
| `0015_messages_comment.sql` | `ALTER messages ADD message_type varchar(16) NOT NULL DEFAULT 'text', parent_post_id varchar(128) NULL` (Chat-2) |
| `0016_competitor_monitor.sql` | `CREATE competitor_sources` + `CREATE competitor_posts` (new tables; inline PK) |
| `0017_competitor_indexes.sql` | indexes on competitor_posts(tenant_id, detected_at), unique(source_id, content_hash) — **separate batch** |
| `0018_generated_doc_expiry.sql` | `ALTER generated_documents ADD expires_at datetimeoffset NULL` (Docs-1) |
| `0019_masstransit_outbox.sql` | `CREATE InboxState/OutboxState/OutboxMessage` (WS1 prerequisite; per MassTransit EF schema) |

> Index on `messages.parent_post_id` (ALTER-added) → its own file `0015b`/`0019` per [[clawbot-migration-no-go]] (CREATE INDEX on ALTER-added col cannot share batch).

### New entities
```csharp
// Research-2
CompetitorSource : Entity<Guid>, ITenantOwned
  { TenantId, Name, Url, SourceType("rss"|"fanpage"), IsActive, CreatedAt }
CompetitorPost : Entity<Guid>, ITenantOwned
  { TenantId, SourceId, Url, Title, Snippet(PII-redacted), PublishedAt, DetectedAt, ContentHash(dedupe) }
```

### Extended entities
- **Message** — add `MessageType` (text|comment|dm, default text), `ParentPostId` (string?). `ChannelMessage` record gains same 2 fields; `PancakeChannelAdapter.ParseAsync` populates them.
- **Lead** — `AdjustScore` raises `LeadBecameHot(tenantId, leadId, score)` on →hot and `LeadBecameWarm(tenantId, leadId, score)` on →warm transitions; published via outbox, consumed by Lead-2/Lead-3 consumers.
- **IAssignmentPoolSource** — contract change: returns per-sale load counts (open conv + open warm/hot leads), not bare user IDs; `LeastBusyLeadAssignmentService` consumes it.
- **GeneratedDocument** — add `ExpiresAt`; `Create(...)` sets `expiresAt = createdAt + 7d`; download path 410 when expired.
- **DripSequence** — seed one default per tenant `TriggerEvent='warm_lead'`, 4 steps / 7 days (admin editable).

## Chat-2 Pancake spike — RESULT (2026-06-13, live probe)
Verified against a real Pancake token:
- **Base URL:** `https://pancake.vn/api/v1` · auth `?access_token=` (code default was a wrong placeholder `pages.fm/api/public_api/v1` → **fixed** commit `3811cc5`).
- **Endpoints confirmed (read-only):** `GET /me`, `GET /pages`, `GET /pages/{page_id}/conversations`.
- **Comment vs DM distinction EXISTS:** each conversation carries `"type"` (`INBOX` observed; `COMMENT` per Pancake model for Facebook pages). Conversation id format `pzl_u_<page>_<customer>`.
- **Verdict:** Chat-2 comment-reply + DM flow is **feasible**. Remaining to wire/test: a **Facebook page** must be connected (the test account only has a *personal Zalo* page, which has no post comments → no `COMMENT`/`post_id` data to verify the reply-comment + private-reply endpoints). Need an FB page OR a sample comment webhook payload to map `post_id`/private-reply action.

## API Design

| Method | Route | Auth | Purpose |
|---|---|---|---|
| GET/POST/PUT/DELETE | `/api/competitors/sources` | `perm:content.write` | Admin CRUD competitor feeds (Research-2) |
| GET | `/api/competitors/posts` | `perm:content.read` | List detected competitor posts (paged) |
| GET | `/api/sale-assist/upsell?conversationId=` | `perm:lead.read` | Dynamic upsell suggestion (SaleAssist-4) — replaces static string |
| GET | `/api/analytics/omnichannel?compare=dod\|wow` | `perm:analytics.read` | Adds delta % vs prior day / week (Report-1) |
| GET | `/api/docs/{id}/download` | token/expiry-checked | Enforce `ExpiresAt` → 410 (Docs-1) |

- No-new-endpoint items: Lead-2, Lead-3, SaleAssist-3 (idle tier), Research-1 (cron), Ads-1 (cron+rule), Chat-2 (ingest path) — internal job/handler changes.

## Component Breakdown
- **Backend modules:** M10/M06 (Chat-2 ingest+adapter), M14 (SaleAssist upsell), M15 (Lead assign/drip), M18 (Research competitor+TZ), M20 (Report delta), M17 (Docs), M19 (Ads).
- **Storage:** SQL Server (4 migrations), reuse `IDocumentStorage` (MinIO/Local) for docs.
- **Jobs:** new `CompetitorScanJob`; modify `IdleConversationAlertJob`, `AdsRuleEvaluationJob`, `WeeklyTrendScanJob`, `DripSequenceJob` (enroll trigger).
- **3rd-party:** Pancake (comment webhook + send-DM — **spike**), SMTP (gated), Claude.

## Design Decisions

1. **Chat-2 spike-gated** — Pancake comment-event + send-DM capability **unverified**. Phase 0 = spike (read Pancake webhook payload + API docs). If no send-DM → ship reply-comment-only, defer DM. Schema (`message_type`/`parent_post_id`) lands regardless (cheap, forward-compat).
2. **Upsell hybrid** — `Stage=='hot'` gate (cheap filter) avoids LLM cost on cold convs; Claude only on hot → closing-signal + suggestion. Falls back to generic suggestion if LLM errors.
3. **Competitor admin-CRUD** — per-tenant sources table (not config) → self-serve, queryable history. Dedupe by `content_hash`. Cap ~20 sources/tenant (constraint).
4. **Event-driven via WS1 outbox (DECIDED)** — Lead-2/3 are **MassTransit consumers**, not inline. `Lead.AdjustScore` raises `LeadBecameHot`/`LeadBecameWarm`; `DomainEventPublishInterceptor` maps `DomainEvents`→outbox in the same SaveChanges transaction (exactly-once, no loss on crash); consumers do assign+notify / drip-enroll + retry/dead-letter. **Scope expansion:** `AddEntityFrameworkOutbox<AppDbContext>`, new migration `000X_masstransit_outbox.sql` (3 tables), consumer endpoints + `ConfigureEndpoints`. Keep existing gRPC sync paths untouched (hybrid topology). This is a **prerequisite phase** sequenced first.
5. **Least-busy REPLACES round-robin (DECIDED)** — change `IAssignmentPoolSource` contract to return per-sale **load counts**; new `LeastBusyLeadAssignmentService` picks min(open conversations assigned + open leads owned in warm/hot). Single strategy (no round-robin retained); applies to both lead-create and hot-transition. Offline/inactive sales excluded. Breaking change is in-repo only → update all callers atomically.
6. **Research-1 = make TZ explicit, not a bugfix** — `Cron.Weekly(Monday,0,0)` UTC already = 07:00 GMT+7 (VN no DST). Fix = pass explicit `TimeZoneInfo` to `RecurringJobOptions` so intent is encoded, not coincidental.
7. **Report-1 delta on-the-fly** — query `kpi_daily` for prior-day + same-day-last-week at request time; no new column (history already persisted).
8. **Ads-1 hourly + throttle/backoff (DECIDED)** — cron `0 */4` → `0 * * * *`; add `AdsRuleEngine` proactive rule `spend/dailyBudget >= 0.9 → alert` reusing `ads_budget` notification. Add **per-platform throttle + exponential backoff on 429**; verify Meta/TikTok quota headroom before enabling hourly (PdA gate in plan).

## Non-Functional Requirements
- **Build gates:** 0/0 (NuGetAudit + CA); new packages clean. **No `GO`**; ALTER-col indexes in own files.
- **Tenant isolation:** new entities `ITenantOwned`; singleton jobs via `IServiceScopeFactory`.
- **PII:** competitor snippets + upsell text redacted; raw purged 30d [[pii-redact-derived-content]].
- **Cost:** Chat-2 + upsell + competitor count to ledger; honor $200/mo cap.
- **Perf:** comment reply target <30s (poll/event latency); upsell LLM call async, non-blocking inbox; competitor scan batched + dedupe.
- **Reliability:** jobs `[DisableConcurrentExecution]`; feed-fetch errors logged, don't fail batch; idempotent enroll/assign (no dup on re-run).
- **Tests:** unit per item ≥80% new-logic branches; don't break 250 green.

## Open Items (carried for /execute-plan)
1. **Chat-2 spike result** — gates the comment+DM flow shape (blocking).
2. Comment intent: extend `KeywordIntentClassifier` w/ `purchase_intent` (default) vs LLM.
3. Default warm drip sequence content (seed vs admin-create) — default: seed 4-step.
4. Docs send channel priority (Zalo via Pancake vs email SMTP) — default: both gated.
5. **Ads hourly quota headroom (Meta/TikTok)** — verify before enabling hourly cron (throttle+backoff added regardless).

## Resolved (this review)
- Least-busy **replaces** round-robin; metric = open conv + open warm/hot leads.
- Lead-2/3 **event-driven via WS1 outbox** (prerequisite phase, sequenced first).
- Ads-1 **hourly + per-platform throttle/backoff**.

## Sequencing (for /execute-plan)
**Phase 0:** Chat-2 Pancake spike (blocking gate) · **Phase 1 (WS1):** MassTransit outbox + interceptor + consumers + migration `0019` · **Phase 2:** Lead-2/3 (consumers + least-busy contract) · **Phase 3:** independent items (Research-2 competitor, SaleAssist-4 upsell, idle tier-2, Report-1 delta, Docs-1, Ads-1, Research-1 TZ) — parallelizable.
