---
phase: requirements
title: Requirements & Problem Understanding
description: Clarify the problem space, gather requirements, and define success criteria
---

# Requirements & Problem Understanding

> Module **M18 — Content + Research pipeline** (P2 · Imp 3 · Diff 3 · T8) from [docs/module-checklist.md](../../module-checklist.md).
> Spec: SPEC-06 / SW-069..078. Decision: **real external connectors this phase** (trend sources + publisher), config-gated, keys provisioned as ops.

## Problem Statement
**What problem are we solving?**

- ClawBot markets a Chinese-language education product across 5 channels (Facebook, Instagram, TikTok, YouTube, Zalo). Marketers currently hand-write every post and manually hunt for trending topics — slow, inconsistent, and blind to what is actually trending in VN.
- Affected: the **Marketer** role (primary) and the PM who approves output. Today there is no in-product content workflow — the `ContentAgent`/`ResearchAgent` gRPC services and `/api/content` are stubs (501 / `"TBD"`).
- Current workaround: external trend tools (Google Trends, TikTok Creative Center) checked by hand + drafts written in Docs + posts copy-pasted into Buffer/Later. Nothing is tenant-scoped, auditable, or scheduled from one place.

## Goals & Objectives
**What do we want to achieve?**

Primary goals
- **Trend research**: weekly (Mon 07:00 **GMT+7**) scan of real external sources — Google Trends VN + YouTube most-popular VN (load-bearing), **TikTok best-effort scrape** + Baidu (config-gated, graceful skip) — biased toward **Chinese-language (中文) learning** topics → relevance-scored topics persisted as `content_briefs`.
- **Content generation**: per-platform posts drafted by an **OpenAI-compatible LLM via the OpenAI .NET library** (configurable base URL + model + key — e.g. DeepSeek/OpenAI; **not** Claude), tone/length/format tuned per TikTok/IG/FB/YT/Zalo, RAG-grounded in the Knowledge Base. Prompt templates externalized (KB/config), not inline literals — honors the CLAUDE.md "no hardcoded prompts" rule.
- **Approval workflow**: draft → approve/reject with `approved_by` + `approved_at` audit.
- **Repurpose**: one-click derive platform variants (e.g. TikTok script → IG Reels + YT Shorts).
- **Scheduling + publish**: schedule approved items — **auto golden-hour per platform when time omitted, manual override allowed** — and auto-publish via a real social publisher (Buffer/Later-shaped API) on a Hangfire job. **On publish failure: mark `failed` + alert via SignalR** (Telegram deferred).

Secondary goals
- Content calendar view; manual trend re-scan trigger; SignalR notification when a weekly scan lands.
- Cost + latency tracked per LLM call (token usage from the OpenAI-compatible response; `IClaudeCostTracker` is Anthropic-specific — log tokens/latency, generic cost tracker is a follow-up).

Non-goals (out of scope this phase)
- Image/video asset generation (M11 P2 `IImagePromptGenerator`/`IVideoScriptComposer`) — `assets_json` stays a reference list only.
- Frontend UI (M16) — REST + gRPC contracts only.
- Per-tenant publisher OAuth onboarding UI — publisher credentials come from config this phase (per-tenant encrypted config table is a documented follow-up).
- Ads (M19).

## User Stories & Use Cases
**How will users interact with the solution?**

- As a **Marketer**, I want a weekly list of trending VN topics with content ideas so that I can brief posts without manual research.
- As a **Marketer**, I want to generate a platform-specific draft from a brief so that I get an on-brand first version in seconds.
- As a **Marketer**, I want to repurpose a TikTok post into Reels + Shorts so that I cover channels without rewriting.
- As a **PM**, I want to approve or reject drafts so that only vetted content goes out, with an audit trail.
- As a **Marketer**, I want to schedule an approved item and have it auto-publish so that I don't post manually.

Key workflows
1. Weekly: `WeeklyTrendScanJob` (Mon 07:00) → fan-out trend sources → score → upsert `content_briefs` (status `pending`).
2. Brief → `POST /api/content/items/generate` → ContentAgent (Claude + RAG) → `content_items` (status `draft`).
3. Approve → `content_items.status=approved` → schedule → `content_schedule` (status `pending`).
4. `ContentPublishJob` (every N min) → due schedules → publisher → `MarkPosted`/`MarkFailed` + `post_url`.

Edge cases
- A trend source is down or its key is missing → skip that source, log, continue (degrade gracefully, never fail the whole scan).
- Publisher returns failure → `MarkFailed`, surface in calendar, do not retry indefinitely (bounded retries).
- Claude blocked/over cost cap → return a clear error, do not persist an empty draft.
- Duplicate trend topic in a week → idempotent upsert (no duplicate briefs).
- Schedule in the past → reject at validation.

## Success Criteria
**How will we know when we're done?**

- `dotnet build Clawbot.sln` → all projects **0 errors / 0 warnings** (NuGetAudit + CA gates green).
- Unit tests green for: relevance scorer, per-platform prompt builder, repurpose mapper, CSV/calendar shaping, publisher request builder, RSS/JSON trend parsers (parse fixtures, no live network).
- `POST /api/content/items/generate` returns a real Claude draft; approve/reject mutate status + audit fields; schedule + publish job round-trips against a mock publisher.
- `WeeklyTrendScanJob` run against fixture/recorded responses persists scored `content_briefs`.
- Real round-trip to live YouTube Data API + Buffer/Later + OpenAI-compatible LLM succeeds **once ops provisions keys** (code wired, creds are ops — same posture as Anthropic/Pancake).
- p95 single-draft generation < 10s; `GET /api/content/queue` + `/calendar` list p95 < 200ms (SPEC-06 NFR-01); weekly scan (Mon 07:00 GMT+7) completes within the Hangfire batch window.
- Pipeline supports the SPEC-06 throughput target of **40+ posts/week** per tenant (generate → schedule → publish).

## Constraints & Assumptions
**What limitations do we need to work within?**

Technical constraints
- net8 across the solution; SDK pinned `8.0.418` (Gateway retargeted to net8 — the "net10" memory is stale). See [clawbot-build-gates].
- **Build gates**: `NuGetAudit` + CA analyzers are errors. Any new package (AngleSharp for HTML, Google.Apis.YouTube.v3 for YT) must clear the audit; prefer dependency-free parsing (RSS via `XDocument`, JSON via `System.Text.Json`) where viable.
- DDL is source of truth; `content_briefs`/`content_items`/`content_schedule` tables already exist in [0001_init.sql](../../../deploy/migrations/0001_init.sql) — **no new tables** needed for the core flow.
- Secrets via config/env only (Options pattern); per-tenant publisher/LLM token, if stored, encrypted via existing `IEncryptor`/`AesEncryptor`.
- **LLM**: content generation uses an **OpenAI-compatible** endpoint via the official `OpenAI` .NET library (config base URL/model/key, audit-clean version pinned); does **not** reuse `AnthropicChatClient`. New NuGet must clear the `NuGetAudit` gate.
- **Timezone**: all scheduling + the weekly scan trigger computed in **GMT+7** (the KPI rollup job is UTC — do not copy blindly).
- Endpoint naming aligns to SPEC-06: item list is `GET /api/content/queue`.

Business constraints
- TikTok Creative Center and Baidu have **no official public API** — real ingestion is scrape-based and ToS-fragile; must be isolated behind `ITrendSource` and individually disableable.
- Buffer's API is partner/legacy-gated; Later API is partner-gated. The publisher is therefore endpoint+token-**configurable** (Buffer-shaped default), swappable to Later/Ayrshare without code change.

Assumptions
- Real API keys/accounts (YouTube Data API key, publisher token) are provisioned by ops, not in scope of the code change.
- Knowledge Base content exists (or RAG returns empty gracefully) for grounding.

## Questions & Open Items
**What do we still need to clarify?**

**Resolved (requirements review 2026-06-07):**
- TikTok → **best-effort scrape now** behind `ITrendSource` + per-source enable flag (graceful skip on failure); Baidu config-gated. YouTube + Google Trends load-bearing.
- Scheduling → **auto golden-hour per platform + manual override**.
- Publish failure → **mark `failed` + SignalR alert** (Telegram deferred until channel adapter lands).
- Content LLM → **OpenAI-compatible via the OpenAI .NET library** (configurable endpoint/model; not Claude).
- Endpoint naming aligned to SPEC-06 (`GET /api/content/queue`); timezone GMT+7; throughput 40+/week.

**Still open:**
- Canonical OpenAI-compatible target + model (DeepSeek vs OpenAI vs local) and base URL — config value, set by ops.
- Canonical publisher (Buffer / Later / Ayrshare) — design assumes Buffer-shaped, config-swappable.
- Per-tenant vs global publisher/LLM credentials — global config this phase; per-tenant encrypted table deferred.
- Trend relevance scoring inputs (keyword overlap vs KB modules? source-metric weighting?) — proposed transparent weighted scorer.
- Prompt-template source — KB table vs appsettings (to satisfy no-hardcoded-prompts rule); confirm in design review.
