---
phase: planning
title: Project Planning & Task Breakdown
description: Break down work into actionable tasks and estimate timeline
---

# Project Planning & Task Breakdown

> M18 Content + Research pipeline. Order chosen so each phase **builds + tests green** before the next, matching how M20 landed.

## Milestones
**What are the major checkpoints?**

- [x] **M-A Domain + contracts ready** — entity behaviors added, protos extended, builds 0/0.
- [x] **M-B Content generation GA** — brief CRUD + OpenAI-compatible per-platform generate + approve/reject, unit-tested.
- [x] **M-C Research + trends GA** — trend sources + scorer + weekly job persist `content_briefs`.
- [x] **M-D Schedule + publish GA** — schedule API + calendar + publish job round-trips a mock publisher.
- [x] **M-E Repurpose + polish** — repurpose flow, cost/latency tracking, docs + seed.

## Task Breakdown
**What specific work needs to be done?**

### Phase 1: Foundation (domain + contracts)
- [x] 1.1 Add domain behaviors: `ContentBrief.Update`/`MarkStatus`; `ContentItem.UpdateBody`/`MarkScheduled`/`MarkPublished` — [Content/](../../../src/shared/Clawbot.Domain/Content/).
- [x] 1.2 Extend `agent_content.proto` (additive: `repeated ContentVariant variants`, `rpc Repurpose`) + `agent_research.proto` review — [proto/](../../../proto/).
- [x] 1.3 `Clawbot.Api.Contracts/Content/ContentDtos.cs` (brief/item/schedule/trend/calendar records).
- [x] 1.4 Build 0/0 checkpoint.

### Phase 2: Core Features (generation + approval)
- [x] 2.0 One-page RFC (`.sdd/rfcs/`) for OpenAI-compatible LLM via OpenAI .NET lib; pin an audit-clean version (NuGetAudit gate).
- [x] 2.1 `IPromptTemplateProvider` per-platform templates (tone/length/format for TikTok/IG/FB/YT/Zalo) sourced from KB/config — **not inline literals**.
- [x] 2.2 `ContentAgent` orchestrator: brief → RAG retrieve → externalized template → **OpenAI-compatible LLM** (`OpenAiCompatibleChatClient`, `IContentLlmClient` + `ContentLlmOptions` base URL/model/key) → draft.
- [x] 2.3 Implement `ContentAgentGrpcService.Generate` (+ `Repurpose`): persist `content_items`, return per-platform variants.
- [x] 2.4 `ContentEndpoints`: brief CRUD + `POST items/generate` + `GET /api/content/queue` (SPEC-06 name) + item update/soft-delete + approve/reject; error format `{errorCode,message,requestId}`; register `MapContent()` + `ContentAgentClient` in `Program.cs`; remove `/api/content` stub.
- [x] 2.5 Unit tests: prompt builder, repurpose mapper, approve/reject transitions.

### Phase 3: Integration & Polish (research + schedule + publish)
- [x] 3.1 `ITrendSource` + `GoogleTrendsRssSource` (XDocument) + `YouTubeDataApiSource` (HttpClient + key Options) load-bearing; **`TikTokScrapeSource` best-effort (AngleSharp, enabled, graceful skip)** + `BaiduScrapeSource` (config-gated). Per-source `Enabled` flag + timeout.
- [x] 3.2 `WeightedTrendScorer` (keyword overlap vs KB modules + source-metric weighting) + `ResearchAgent` fan-out (parallel, per-source timeout, graceful skip).
- [x] 3.3 `ResearchAgentGrpcService.WeeklyTrends`: persist scored `content_briefs` (idempotent upsert) + SignalR notify.
- [x] 3.4 `WeeklyTrendScanJob` (Mon 07:00 GMT+7, queue `content`) in `HangfireModule` → **triggers `ResearchAgent` via gRPC**; register `ResearchAgentClient` in `Program.cs`; `GET /api/content/trends` + `POST /trends/scan`.
- [x] 3.5 `ISocialPublisher` + `HttpSocialPublisher` (Buffer-shaped, `PublisherOptions` endpoint+token) + Polly resilience.
- [x] 3.6 `IGoldenHourResolver` (per-platform optimal time, GMT+7); `IContentNotifier` (SharedKernel) + `SignalRContentNotifier` (Api over `IHubContext<DashboardHub>`); Schedule API (`items/{id}/schedule` — auto golden-hour when time omitted, else manual; `GET /calendar`; `DELETE /schedule/{id}`) + `ContentPublishJob` (every N min, GMT+7): due items → publish → `MarkPosted`/`MarkFailed` + **alert on fail via `IContentNotifier`**.
- [x] 3.7 Unit tests: RSS/JSON trend parsers (fixtures), scorer, publisher request builder, calendar shaping, schedule validation.
- [x] 3.8 Seed `deploy/seed/content-briefs.sql` sample briefs (idempotent MERGE) + appsettings keys (`Content:YouTubeApiKey`, `Content:Publisher:*`) documented in `.env.example`.
- [x] 3.9 Final build 0/0 + full test run; update [module-checklist.md](../../module-checklist.md) M18 ticks.

## Dependencies
**What needs to happen in what order?**

- Phase 1 → 2 → 3 (each gated on a green build).
- Reuses (already in repo): `QdrantRagRetriever` (M09), `HttpResiliencePolicies` (M01), Hangfire (M12), `AesEncryptor`/`IEncryptor`, endpoint + gRPC-client patterns (M14/M17/M20), `IClock`, `ITenantAccessor`, SignalR `DashboardHub`. (Content LLM is **new** — OpenAI-compatible, not the M10 `AnthropicChatClient`.)
- External (ops-provisioned, not code-blocking for build/test): OpenAI-compatible LLM base URL + key + model; YouTube Data API key; publisher (Buffer/Later/Ayrshare) token + endpoint.

## Timeline & Estimates
**When will things be done?**

- Phase 1: ~0.5 day (mechanical domain + proto).
- Phase 2: ~1.5 days (generation is the core value; prompt tuning + tests).
- Phase 3: ~2–2.5 days (connectors + 2 jobs + schedule/publish + tests).
- Buffer: ~0.5 day for connector quirks (RSS shape drift, publisher auth).
- Target: single T8 sprint, landed in 3 build-green increments (M-B, M-C, M-D/E).

## Risks & Mitigation
**What could go wrong?**

- **Build-gate (NuGetAudit/CA)** from new deps — the **OpenAI .NET lib** + `AngleSharp` (TikTok/Baidu scrape) must clear NuGetAudit. *Mitigation: pin audit-clean versions, verify on add; BCL parsing (XDocument/System.Text.Json) for Trends/YouTube; YouTube via raw HTTP, not SDK.*
- **TikTok/Baidu have no official API** — scrapers brittle/ToS-fragile → YT + Google Trends load-bearing; **TikTok best-effort (enabled, graceful skip)**, Baidu config-gated. *Mitigation: `ITrendSource` isolation + per-source enable flag + timeout.*
- **Publisher partner-gating** (Buffer/Later) → endpoint+token configurable, Buffer-shaped default, swappable to Ayrshare. *Mitigation: no vendor hard-coding; integration tested against a mock.*
- **LLM cost/latency** on bulk generation → per-platform single-call drafts (not N parallel); log token usage from the OpenAI-compatible response (generic cost tracker is a follow-up — `IClaudeCostTracker` is Anthropic-specific). 
- **Publish double-post** → idempotent: only `pending` schedules whose `scheduled_at<=now`, transition state in same tx; bounded retries on `failed`.
- **Missing creds in dev** → graceful degrade (skip source / mark schedule failed with clear reason), pipeline still build+test verifiable.

## Resources Needed
**What do we need to succeed?**

- Knowledge: SPEC-06 (`.sdd/specs/06-*`), RFC-001 (as the RFC format template — a new RFC records the OpenAI-compatible choice), existing `DocsAgent`/`ChatAgent`/`DailyKpiRollupJob` + `IInboxNotifier`/`SignalRInboxNotifier` as templates.
- Tools/services: OpenAI-compatible LLM endpoint/key/model (ops-provisioned), SQL Server + Hangfire (running), Qdrant (RAG).
- Ops (for live round-trip, not for merge): YouTube Data API key, social publisher account + token.
- Tests: xUnit + FluentAssertions in `Clawbot.Agents.Tests` / `Clawbot.Api.Tests`; trend-source HTTP fixtures (recorded responses) — no live network in tests.
