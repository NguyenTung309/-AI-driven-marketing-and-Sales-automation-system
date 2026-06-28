# SPEC-16 — Agent-to-Agent Autonomy (Dynamic Orchestration V3)

Status: `DRAFT`
Traces to: SPEC-03 (AI Agent Management), SPEC-06 (Content), SPEC-08 (Analytics), SPEC-09 (Ads), [plan](../../../docs/ai/planning/2026-06-27-dynamic-agent-orchestration-v3.md)
Owner: P? · Sprint: TBD

## 1. Business Context

The dynamic orchestrator plans a DAG and delegates to sub-agents, but **data-defined agents can only emit LLM text** — they cannot touch the system. The "hands" already exist (Pancake outbound, social publisher, content store, ads connectors, KPI/report jobs, SignalR notifications, agent schedules) but the orchestrator never wires to them. Goal: agents **act on the system and report back**; humans only **plan, track, decide/approve**. Default = fully autonomous; humans can flip a per-tenant switch to require approval (reuse `Tenant.RequireOrchestrationApproval`).

Current blocking bug: content-agent LLM call hits a hard-coded 30s timeout → 4 retries all time out → every round fails → `max_rounds`.

## 2. User Stories

- AS A marketer I WANT to type "Lên kế hoạch đăng bài tuyển sinh hằng ngày" SO THAT agents generate content, store drafts, schedule/publish, and report back — without me doing each step.
- AS AN owner I WANT high-risk actions (publish, ad spend, customer messages) to run autonomously BUT be able to switch on approval gating SO THAT I keep control when needed.
- AS A user I WANT a readable plan, a live agent graph (who ran, how many times, doing what now), and a notification when a run finishes/fails SO THAT I can track without watching terminal logs.
- AS AN operator I WANT a long content task to not die at 30s SO THAT multi-day plans complete.

## 3. Acceptance Criteria (EARS)

### Phase 0 — Hotfix (unblock)
- THE SYSTEM SHALL read the LLM HTTP timeout from config (`Llm:HttpTimeoutSeconds`, default `120`) instead of a hard-coded 30s.
- WHEN an agent task fails with a **transient** error (timeout / HTTP 5xx / 429) THE SYSTEM SHALL retry the **same task** with backoff (cap 2) and SHALL NOT trigger a re-plan.
- WHEN an agent task fails with a **logical/business** error THE SYSTEM SHALL re-plan, bounded by `MaxRounds`.
- WHILE an LLM call exceeds 15s THE SYSTEM SHALL emit a heartbeat trace so the UI does not appear hung.

### Phase 1 — Tool layer (agents can act)
- THE SYSTEM SHALL expose an `IAgentTool` registry (name + JSON input schema + handler) enumerable by the worker.
- THE SYSTEM SHALL read `AgentDefinition.AllowedToolsJson` in the catalog (currently dropped) and pass the matching tools to execution.
- WHEN a data-defined agent runs AND `AllowedToolsJson` is non-empty THE SYSTEM SHALL run a tool-execution (ReAct/JSON-action) loop and SHALL only invoke tools in the allow-list.
- WHEN a tool not in the allow-list is requested THE SYSTEM SHALL reject it and record a trace.
- WHEN an `agent_definition.Code` equals a static adapter name THE SYSTEM SHALL allow delegation to that adapter (NOT silently downgrade to text-only).
- THE SYSTEM SHALL carry structured tool results/observations through `AgentResult` across the DAG (not just a free-form string).
- THE `UpsertAgent` API SHALL accept and persist `AllowedToolsJson` (and `InputSchemaJson`).

### Phase 2 — Close the action loop + reporting
- WHEN the content-agent completes generation in an orchestration run THE SYSTEM SHALL persist a `ContentItem` (status `draft`) attributed to the content-agent.
- THE SYSTEM SHALL spawn/reuse a **reviewer (lead-type) agent** that reviews drafts and approves them; `ContentItem.Approve` SHALL accept an **agent actor** (not only a human userId).
- THE SYSTEM SHALL provide agent-callable tools for `approve`, `schedule`, and `publish` content; `approve` is callable only by the reviewer agent.
- WHEN `Tenant.RequireOrchestrationApproval = false` THE reviewer agent SHALL approve passing drafts and the run SHALL proceed to schedule/publish; approval attribution = reviewer agent id.
- WHEN the reviewer agent rejects a draft THE SYSTEM SHALL keep it `draft`/`rejected` with a reason trace and SHALL NOT publish.
- WHEN `Tenant.RequireOrchestrationApproval = true` THE SYSTEM SHALL stop high-risk actions (publish / ad spend / outbound customer message) at a pending-approval gate.
- WHEN the chat-agent produces a reply in an autonomous flow THE SYSTEM SHALL send it via `IChannelAdapter.SendAsync` through `OutboundMessageSafetyService` (today it persists but never sends).
- WHEN a sub-agent finishes THE SYSTEM SHALL return a structured result to the orchestrator, which SHALL compose a human-readable summary for the user.

### Phase 3 — Tracking / observability UI
- THE SYSTEM SHALL render the plan as a structured DAG/step view; raw-JSON editing SHALL be behind an advanced toggle (keep etag concurrency).
- THE SYSTEM SHALL render an agent graph: one node per sub-agent showing **use count**, **current task**, and status; edges = `dependsOn`.
- THE orchestration run state SHALL survive F5 (already via `?sessionId=`) AND the UI SHALL provide a recent/in-flight run list (URL-independent).
- WHEN a run reaches a terminal state (completed/failed/cancelled) OR needs approval THE SYSTEM SHALL publish a notification via `INotificationPublisher` targeted at the initiating user.
- THE notification bell (`useNotificationsRealtime`) SHALL be mounted in the global shell so push updates surface on every page.
- WHEN planning completes THE orchestrator SHALL post a human-readable summary trace + notification to the user.

### Phase 4 — Autonomy loop
- THE SYSTEM SHALL drive autonomous runs from `AgentSchedules` (existing `AgentScheduleWorker`) into tool-capable agents.
- THE SYSTEM SHALL support a dry-run mode that previews tool actions without executing side effects.
- THE SYSTEM SHALL apply cost-guard, PII redaction, and per-tool RBAC on every autonomous action.

## 4. API Contracts / Data Models

**New / changed schema (ADR-009 → add `deploy/migrations/00XX_*.sql` + EF Fluent):**
- `agent_sessions.user_id` (nullable Guid) — initiating user, for targeted notifications. Migration + `AgentSession.Start/CreatePlan` signatures.
- `agent_definitions.allowed_tools_json` — already exists; **start reading it** (catalog SELECT + worker). No schema change, behavior change only.

**Config:**
- `Llm:HttpTimeoutSeconds` (default 120) — `ChatModule` named HttpClient.
- `Content:Publisher` Endpoint + Token — must be configured for real publishing (else `publisher_not_configured`).

**Tools (Phase 1 registry — wrap existing capabilities first):**
- `content.generate`, `content.approve`, `content.schedule`, `content.publish`
- `channel.send_reply` (`PancakeChannelAdapter.SendAsync`)
- `social.publish` — **publish via Pancake** (`PancakeChannelAdapter`). ⚠️ current Pancake outbound = `reply_inbox` (reply to a thread), NOT a new feed post; publishing a fresh post needs the Pancake post/publish endpoint — verify it exists, else add a `PostAsync` path on the adapter.
- `ads.apply` / `ads.lookalike` / `ads.remarketing` (via `AdsAgent`)
- `report.snapshot` / `report.anomaly` / `report.forecast` (via `ReportAgentRunner`)

**API:**
- `POST /api/orchestration/v2/agents` (UpsertAgent) — extend DTO with `allowedTools`, `inputSchema`.
- `GET /api/orchestration/{id}` / `/trace` — extend `OrchestrationTaskDto` with per-session `useCount` + `currentTaskId`.
- Reuse: `Tenant.RequireOrchestrationApproval` toggle (Admin endpoint already exists), `INotificationPublisher`, `AgentSchedules`.

## 5. Technical Constraints

- Tool loop = **ReAct/JSON-action on `CompleteAsync`** first (provider-agnostic); native Anthropic tool-use (extend `IClaudeChatClient`) is a later option.
- LLM only called from `AgentService` (CLAUDE.md §9). Tools that touch domain go through existing adapters/services, not Domain/Application.
- Reuse static `AgentAdapters` (content/ads/saleassist/docs/research/chat) as tool handlers — do not re-implement capabilities.
- gRPC agents stay thin; orchestration logic in `Clawbot.Agents.Core`.
- Autonomous trigger path = OrchestrationV2 (`AgentSchedules` → `AutonomousOrchestrator`), NOT Hangfire (agents cannot enqueue Hangfire jobs unless a dedicated tool wraps `IBackgroundJobClient`).
- All side-effecting tools enforce `OrchestratorCostGuard` + PII redaction (existing) + per-tool permission.

### 5.1 Pancake token model — VERIFIED from docs (developer.pancake.biz, 2026-06-27)
Two token types, **both passed as query param** (no Authorization header), **two API roots**:

| Token | Query param | API root | Used for | Lifespan |
|---|---|---|---|---|
| **User access token** | `access_token` | `https://pages.fm/api/v1/...` | account-level: list pages, mint page tokens | ≤90 days / until logout |
| **Page access token** | `page_access_token` | `https://pages.fm/api/public_api/v1` (and `/v2`) | **all** page ops: conversations, send message, statistics, posts, customers | never expires unless deleted/regenerated |

Key endpoints (verified):
- `GET https://pages.fm/api/v1/pages?access_token={USER}` — list pages (gives `page_id`).
- `POST https://pages.fm/api/v1/pages/{page_id}/generate_page_access_token?access_token={USER}` — mint/refresh page token (caller must be page admin; **invalidates previous token**).
- `GET /pages/{page_id}/conversations` · `GET /pages/{page_id}/conversations/{conversation_id}/messages` — read.
- `POST /pages/{page_id}/conversations/{conversation_id}/messages` — **Send message** (page token). ← the real send endpoint.
- `POST /pages/{page_id}/upload_contents` — upload media → attachment id.
- `GET /pages/{page_id}/posts` — **read** posts only. `GET /pages/{page_id}/statistics/*` — KPIs (could feed report-agent).
- Page token can also be **copied manually** from Pancake Settings → Tools (no mint needed if pasted directly).
- **Rate limit: 5 req/page/second → HTTP 429** (per `page_id`, not per tenant).

**⚠️ Pancake CANNOT create a new feed post.** Public API has no create-post endpoint — only read posts + reply within conversations/comments. So "đăng bài tuyển sinh" (a new feed post) **cannot go through Pancake**. → publishing needs **FB/Zalo Graph API directly** or a 3rd-party relay (existing `HttpSocialPublisher`/`Content:Publisher`). Pancake covers inbound + conversation/comment replies, not feed publishing. **Decision needed (Q in §10).**

**Current code defects vs verified docs:**
- Wrong host+path: code base `https://pancake.vn/api/v1` + `/pages/{page_id}/conversations/{thread_id}/messages` ([PancakeConfigResolver L15-16](../../../src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeConfigResolver.cs#L15)). Correct page-op root = `https://pages.fm/api/public_api/v1`. Verify pancake.vn proxies, else fix.
- Token: query named `page_access_token` ([adapter L128](../../../src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs#L128)) but `InboxEndpoints` feeds the **user** token; no mint flow exists → 401 with a real user token.
- Send body `{action:"reply_inbox", message}` — verify against the docs **Send message** schema (Message/InboxMessage/PrivateReply/ReplyComment); likely needs adjustment.
- `PancakeConfigs` = one token+PageId/tenant → no multi-page.
- Rate limiter = 120/min/tenant (wrong dimension); docs = 5/sec/**page**.

**Required upgrade (EARS):**
- THE SYSTEM SHALL store/resolve a **page access token per `page_id`** — either minted via `generate_page_access_token` from the user token (cache + refresh on 401) or pasted directly — in a per-tenant `page_id → page_access_token` map (new `pancake_pages` table; migration per ADR-009).
- THE SYSTEM SHALL call page ops against `https://pages.fm/api/public_api/v1` using `page_access_token`, never the raw user token.
- THE SYSTEM SHALL rate-limit outbound at **5 req/sec per `page_id`** and back off on 429.
- THE SYSTEM SHALL route channel replies to `POST .../conversations/{id}/messages`; feed publishing goes to a **non-Pancake** publisher (FB/Zalo Graph or relay) — NOT Pancake.

### 5.2 Channel & token configuration module (required)
A per-tenant admin module (BE + FE) to manage all channel credentials — extend existing `PancakeConfigs` + Admin inbox endpoints, do not invent a parallel system.

**Data model (encrypted at rest, per ADR — reuse `IEncryptor`):**
- Pancake: user access token, per-page map `pancake_pages(page_id, name, platform, page_access_token, is_active)`, webhook secret, base URLs. New `pancake_pages` table (migration per ADR-009).
- FB publishing: app id/secret, page id ↔ page access token, granted scopes.
- Zalo OA publishing: OA id, OA access token + refresh token, app secret.
- Optional relay (`Content:Publisher`): endpoint + token (already config-bound).

**EARS:**
- THE SYSTEM SHALL provide admin endpoints (perm-gated, e.g. `admin:channels`) to CRUD channel credentials per tenant; all secrets encrypted, never returned in plaintext (masked).
- THE SYSTEM SHALL provide a "Connect / Test" action per channel that validates a token (e.g. Pancake `GET /pages`, FB `GET /me/accounts`) and reports OK/expired before save.
- WHEN a Pancake user token is saved THE SYSTEM SHALL list its pages and let the admin pick which pages to manage, minting/storing each `page_access_token`.
- WHEN a token is expired/invalid (401) THE SYSTEM SHALL surface a re-auth prompt and SHALL NOT silently fail an autonomous run.
- THE FE SHALL show connection status per channel/page (connected / expired / not configured) on a "Cấu hình kênh" settings page.

## 6. Out of Scope

- Native Anthropic/OpenAI tool-use protocol (Phase 1 uses ReAct).
- Real lookalike/remarketing audience building (Meta + TikTok connectors are stubbed — separate spec).
- Agent-triggered Hangfire job scheduling (`IBackgroundJobClient` tool) — defer unless needed.
- Unifying the two agent models (`agent_configs` V1 vs `agent_definitions` V2) — track as tech-debt, mark V1 deprecated.
- SSE/WebSocket streaming of orchestration progress (keep 3s poll for V3; SignalR for notifications only).

## 7. NFR

- Long generation: a single agent LLM call up to 120s must not fail by client timeout (streaming preferred where available).
- A completed/failed run notification reaches the initiating user p95 < 5s after terminal state.
- Cost: every autonomous side-effect counted against the existing per-tenant budget cap.
- PII: any persisted derived text (drafts, summaries, traces) redacted (existing rule).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| LLM timeout (transient) | HttpClient timeout / OperationCanceled | "đang chạy lâu" heartbeat | retry same task ×2 backoff; no replan |
| LLM 5xx/429 | status code | none until exhausted | retry same task ×2; then mark failed → replan |
| Tool not allow-listed | registry check | trace "tool blocked" | skip tool, continue/replan |
| Publisher not configured | `publisher_not_configured` | "chưa cấu hình kênh đăng" notification | hold schedule as pending; alert admin |
| Approval required (toggle on) | `RequireOrchestrationApproval` | "chờ phê duyệt" + notification | pause at gate; resume on Approve |
| Agent shadow downgrade | def code == adapter name | n/a (fixed) | delegate to real adapter |
| Outbound safety block | `OutboundMessageSafetyService` | "tin bị chặn an toàn" | do not send; trace + notify |

## 9. Phase Sequencing (goal → verify)

1. **Phase 0** → verify: goal "đăng bài 30 ngày" runs the full DAG (no `max_rounds`); timeout config honored; transient retry does not consume replan rounds. _Unit test: transient vs logical classification._
2. **Phase 1** → verify: a data-defined agent with `allowedTools=["report.snapshot"]` actually calls the tool and returns structured data; allow-list rejection traced; shadow case delegates. _Test: tool loop + allow-list._
3. **Phase 2** → verify: content run lands a `ContentItem(draft)` in `content_items`; with toggle off, system-actor approve→schedule→publish completes; chat reply physically sent via adapter. _Test: full draft→publish state machine via tools._
4. **Phase 3** → verify: plan renders as DAG; agent graph shows use-count + current task; terminal run fires a user-targeted notification; bell updates on any page.
5. **Phase 4** → verify: a schedule fires a tool-capable autonomous run; dry-run previews actions without side effects; cost/RBAC enforced.

## 10. Open Questions

| Item | Owner | Status |
|---|---|---|
| Approval actor → **DECIDED**: a generated/reused **reviewer (lead-type) agent** approves drafts; `ContentItem.Approve` accepts agent actor; audit shows reviewer agent id | P? | resolved |
| Per-tool RBAC → **DECIDED**: reuse existing permission codes (`content:write`, `ads:write`, ...); agent inherits them, no new `tool:*` codes | P? | resolved |
| Publishing channel → **DECIDED**: feed posts via **FB/Zalo Graph API directly** (Pancake has no create-post). New work: a publishing connector implementing `ISocialPublisher` that calls FB Graph `POST /{page_id}/feed` + Zalo OA post API with the **page token**; requires platform app + permissions (FB `pages_manage_posts`, Zalo OA). Pancake stays for inbound + conversation/comment replies. | P? | resolved |
| ReAct loop iteration cap + cost ceiling per agent task | P? | open |
| Streaming vs 120s timeout — does the configured provider (DeepSeek/Anthropic) support stream + usage accounting? | P? | open ([[openai-sdk-streaming-usage]]) |
