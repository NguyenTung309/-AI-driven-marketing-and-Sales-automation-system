# PLAN — SPEC-16 Agent-to-Agent Autonomy (Dynamic Orchestration V3)

> Task breakdown for [SPEC-16](SPEC.md). Order = top-down. Each task: files to touch · migration (ADR-009) · test · DoD. `[BE]`/`[FE]`/`[DB]` tags. Effort: S(<½d) M(~1d) L(2-3d).

Legend: ⛔ blocker · ⭐ core value · 🔁 reuses existing infra.

---

## Phase 0 — Hotfix (unblock `max_rounds` / timeout) — ~1 sprint-day ✅ DONE 2026-06-28 (incl. P0-3)

| # | Task | Files | Test | Effort |
|---|---|---|---|---|
| P0-1 ⛔ | Make LLM HTTP timeout configurable, default 120s | `ChatModule.cs` (L19-22) — bind `Llm:HttpTimeoutSeconds`; `appsettings.json` | unit: option bound; integ: long call >30s succeeds | S |
| P0-2 ⭐ | Classify transient (timeout/5xx/429) vs logical failure; transient → retry **same task** (backoff, cap 2), no replan | `AutonomousOrchestrator.cs` (`ExecuteTaskAsync` L163-211, round loop L111-160) | unit: transient retries task & doesn't burn replan; logical → replan | M |
| P0-3 | Heartbeat trace when LLM call >15s | `GenericLlmAgentWorker.cs` / `AutonomousRunSink` | unit: heartbeat trace emitted | S | ✅ `CallLlmWithHeartbeatAsync` emits a `heartbeat` trace via the run sink (15s threshold, fire-and-forget Task.Run, no-op when sink absent); worker gained optional `runSink`+`clock`+`WorkerRunContext` |
| P0-4 | Sanity: confirm `MaxRounds=3` still right after P0-2; expose via options/config | `AutonomousRunContracts.cs` (L20) | — | S |

**Verify:** goal "đăng bài 30 ngày" runs full DAG; no `max_rounds`; transient retry ≠ replan round.
**DoD:** tests green, `dotnet build` 0 warn, no schema change.

---

## Phase 1 — Tool layer (agents act) ⭐ — ~1 sprint ✅ SPINE DONE 2026-06-28

| # | Task | Files | Test | Effort | Status |
|---|---|---|---|---|---|
| P1-1 ⭐ | `IAgentTool` abstraction: name + JSON input schema + handler + required permission | new `Clawbot.Agents.Core/Orchestrator/Tools/IAgentTool.cs` | unit: descriptor shape | S | ✅ |
| P1-2 ⭐ 🔁 | Tool registry; wrap existing capabilities as tools (no re-impl): the 6 `AgentAdapters` (content/ads/saleassist/docs/research/chat), `ReportAgentRunner` | new `ToolRegistry.cs`; reuse `AgentAdapters.cs`, `AgentTaskInput` | unit: registry resolves + arg-binds | M | ✅ (8 adapters wrapped via `ToolRegistryFactory.Build(IEnumerable<IAgent>)`; perm map reuses RbacSeeder codes) |
| P1-3 ⭐ | ReAct/JSON-action loop replacing/ wrapping text-only worker (provider-agnostic, on `CompleteAsync`); iteration cap + cost ceiling | `GenericLlmAgentWorker.cs` | unit: model emits action → tool runs → observation fed back → final | L | ✅ (cap=5, cost recorded per reply; empty allow-list → text-only fallback) |
| P1-4 | Read `AllowedToolsJson` (catalog currently DROPS it) + enforce allow-list at worker | `AgentDefinitionCatalog.cs`, `AgentDefinitionCatalogEntry.cs`, worker | unit: out-of-list tool rejected + traced | M | ✅ (entry gained optional `AllowedToolsJson`; SELECT projects it; worker enforces) |
| P1-5 | Fix shadowing: data-defined agent may **delegate** to static adapter instead of downgrading to text | `AutonomousOrchestrator.ResolveAgent` (L213) | unit: def code==adapter name → adapter runs | M | ✅ (resolved via tool-capable worker: allowedTools containing adapter name → ReAct delegates to it) |
| P1-6 | `AgentResult` structured channel (tool results/observations) through DAG | `IAgent.cs` (AgentResult), worker, sink | unit: structured output round-trips | M | ✅ `EmitToolTraceAsync` persists each tool action/observation as a `tool_executed`/`tool_failed`/`tool_blocked`/`tool_error` trace via the run sink; final answer flows through DAG |
| P1-7 | `UpsertAgent` API + DTO accept `allowedTools`, `inputSchema` (currently omitted) | `OrchestrationV2Endpoints.cs` (L138), request DTO, FE agent form | integ: POST sets AllowedToolsJson | S | ✅ DONE 2026-06-28 — BE DTO + UpsertAgent validate/wire (incl. P4-3 admin-perm check); list DTO returns allowedTools/inputSchema/personaPrompt; FE `OrchestrationV2Page` "Sửa tools" modal edits allowedTools via `upsertOrchestrationV2Agent`. 15 validation + 5 allowed-tools-perm tests |

**Verify:** agent with `allowedTools=["report.snapshot"]` actually calls tool + returns structured data; allow-list rejection traced; shadow delegates.
**DoD:** ReAct loop cost-capped; tests ≥80% on touched core.

---

## Phase 2 — Close action loop + reporting ⭐ — ~1-1.5 sprint

### 2A. Pancake token model fix (⛔ for any real send) — verified §5.1 ✅ DONE 2026-06-28
| # | Task | Files | Migration | Effort | Status |
|---|---|---|---|---|---|
| P2-1 ⛔ [DB] | `pancake_pages(tenant_id, page_id, name, platform, page_access_token_enc, is_active)` map | EF config + `deploy/migrations/00XX_pancake_pages.sql` | new table | M | ✅ `PancakePage` entity + `PancakePageConfiguration` + [0037_pancake_pages.sql](deploy/migrations/0037_pancake_pages.sql) + `PancakePages` DbSet |
| P2-2 ⛔ [BE] | Token resolver: user token → mint page token via `POST pages.fm/api/v1/pages/{id}/generate_page_access_token`; cache; refresh on 401 | `PancakeConfigResolver.cs`, new `PancakePageTokenService.cs` | — | M | ✅ `IPancakePageTokenResolver`+`PancakePageTokenResolver` (read) + `IPancakePageTokenService`+`PancakePageTokenService` (mint+store encrypted) + `IPageTokenMintGateway`+`HttpPancakePageTokenMintGateway` (HTTP mint). Refresh-on-401 deferred (mint invalidates prior token; 401 → re-mint wired in Module M-4 connect flow) |
| P2-3 ⛔ [BE] | Fix host+path to `https://pages.fm/api/public_api/v1`; send `POST /pages/{id}/conversations/{cid}/messages` w/ `page_access_token`; verify send body schema vs docs | `PancakeChannelAdapter.cs` (L99-140), `PancakeConfigResolver` defaults (L15-16) | — | M | ✅ default host fixed (`pancake.vn/api/v1`→`pages.fm/api/public_api/v1`) + appsettings v2→v1 + adapter resolves per-page token before send. **Send body schema (`reply_inbox`/`message`) NOT re-verified vs live docs** — left as-is; verify against real account before prod send (open item) |
| P2-4 [BE] | Rate limit → 5 req/s **per page_id** (now 120/min/tenant) | `PancakeChannelAdapter.cs` (L19-29) | — | S | ✅ `PartitionedRateLimiter<string>` keyed by page_id (5/s), tenant fallback |

**Verify:** unit tests green (15 new: 3 service + 4 resolver + 2 adapter page-token + 6 existing adapter regression). Full Infrastructure.Tests 166/166, sln build 0/0. **End-to-end mint/send NOT run against a real Pancake account** — needs the user's creds (the live JWT in appsettings). Code path implemented + unit-tested; integration verification deferred to Module M-4 connect flow with real creds.

### 2B. Content persist + autonomous approve ✅ DONE 2026-06-28
| # | Task | Files | Migration | Effort | Status |
|---|---|---|---|---|---|
| P2-5 ⭐ | content-agent tool persists `ContentItem(draft)` via `ContentAgentGrpcService` path; drop `content` no-op stub | `DefaultAgentRegistry.cs`, content tool, `ContentAgentGrpcService.cs` | — | M | ✅ `ContentGenerateTool` (AgentService) calls ContentAgent + persists ContentItem(draft); registered as explicit IAgentTool overriding the text-only adapter-wrapped content-agent. gRPC persist path unchanged |
| P2-6 ⭐ | Reviewer (lead-type) agent + `content.approve` tool; `ContentItem.Approve` accepts **agent actor** (not only human userId); reject → reason trace | `ContentItem.cs`, content tools, seed reviewer agent_definition | `0038_content_items_approved_by_agent.sql` | M | ✅ `ContentItem.ApproveByAgent(agentDefinitionId)` + `approved_by_agent_id` col + [0038](deploy/migrations/0038_content_items_approved_by_agent.sql); `ContentApproveTool` (approve/reject, requires ctx.AgentDefinitionId, surfaces reason); DevDataSeeder seeds reviewer-agent with allowedTools `["content.approve"]` + content-agent with `["content-agent"]` |
| P2-7 | `content.schedule` / `content.publish` tools; gate publish behind risk approval (D2) | content tools, `ContentSchedule` | — | M | ✅ `ContentScheduleTool` (approved→scheduled + ContentSchedule row) + `ContentPublishTool` (publish via ISocialPublisher, marks published, High-risk for P4-4 gate). Risk-gate done in P4-4 |

### 2C. Feed publishing (FB/Zalo Graph — Pancake can't post) ✅ SCAFFOLDED 2026-06-28
| # | Task | Files | Migration | Effort | Status |
|---|---|---|---|---|---|
| P2-8 ⭐ [BE] | `ISocialPublisher` impl calling **FB Graph `POST /{page_id}/feed`** + **Zalo OA post API** w/ page token (replaces `publisher_not_configured`) | new `GraphSocialPublisher.cs`, DI in `DependencyInjection.cs` | — | L | ✅ `GraphSocialPublisher` (FB /feed form-encoded + Zalo OA article, both w/ page/OA token) + `GraphPublisherOptions` + DI selects Graph when `Facebook:Enabled`/`Zalo:Enabled`, else legacy HttpSocialPublisher. **Scaffolded + unit-tested vs fake HTTP; real publish needs FB app id/secret + pages_manage_posts (app review) + Zalo OA token — user must provision (external blocker)** |
| P2-9 | Wire `social.publish` tool → GraphSocialPublisher; `ContentPublishJob` keeps driving schedules | content tools, `ContentPublishJob.cs` | — | S | ✅ `ContentPublishTool` (name "content.publish") calls ISocialPublisher, marks item published + schedule posted on success; ContentPublishJob unchanged (already drives ISocialPublisher). publisher-agent seeded with allowedTools `["content.schedule","content.publish"]` |

### 2D. Outbound reply + reporting ✅ DONE 2026-06-28
| # | Task | Files | Effort | Status |
|---|---|---|---|---|
| P2-10 | AI chat reply actually sent: after persist, call `IChannelAdapter.SendAsync` through `OutboundMessageSafetyService` | `ChatAgentGrpcService.cs` (L111) | M | ✅ ChatAgentGrpcService gained optional `IChannelAdapter?`; sends unblocked reply to `conversation.ExternalThreadId` after persist (best-effort, blocked→skip, send_failed trace on error). `OutboundMessageSafetyService` lives in Api layer (ADR-001) so AgentService calls IChannelAdapter directly; safety = reply.Blocked gate (toxicity/spam already enforced in ChatAgent) |
| P2-11 ⭐ | Agent-to-agent reporting: structured sub-agent results → orchestrator composes human-readable summary | orchestrator, sink | M | ✅ `EmitRunSummaryAsync` + `BuildRunSummary` post a `run_summary` trace (Vietnamese prose: "Hoàn thành N/M công việc... [agent] desc — xong/lỗi"); `BuildPlanSummary` posts `plan_summary` after planning (P3-7). Both capped 1200 chars |

**Verify:** content run → `ContentItem(draft)` in DB → reviewer approves → schedule → **real FB/Zalo post**; AI reply physically sent; orchestrator posts a readable summary.

---

## Phase 3 — Tracking / observability UI — ~1 sprint ✅ DONE 2026-06-28

| # | Task | Files | Effort | Status |
|---|---|---|---|---|
| P3-1 | Structured DAG/step plan view; raw-JSON edit behind advanced toggle (keep etag) | `OrchestrationPanel.tsx` (L243-266) | M | ✅ raw JSON moved behind "Chỉnh sửa JSON nâng cao" toggle; structured task list is primary; etag preserved on save |
| P3-2 ⭐ | Agent graph: node/agent + **use count** + **current task** + status; edges=`dependsOn` | `AgentDashboardPage.tsx`, `OrchestrationPanel.tsx`; `OrchestrationTaskDto` (useCount, currentTaskId) | L | ✅ BE derives UseCount+CurrentTaskId; FE panel renders tasks as a topological DAG view (`tasksByDepth` orders root→leaf + depth indentation + ↳ connector) with `×useCount` badge, "đang chạy" current-task pill, status pill, dependsOn edges. Dashboard orchestrator-node unchanged |
| P3-3 [DB] | `agent_sessions.user_id` | `AgentSession.cs`, migrations | S | ✅ domain + [0039](deploy/migrations/0039_agent_session_user_id.sql) + [0040](deploy/migrations/0040_agent_session_user_id_index.sql) + wired proto→API→gRPC→session |
| P3-4 ⭐ 🔁 | Fire notification on terminal/approval | `AutonomousRunSink.cs` | M | ✅ optional `INotificationPublisher` publishes completed/failed/approval to `session.UserId` (best-effort) |
| P3-5 🔁 | Mount `useNotificationsRealtime` global + toast | `Topbar.tsx` | S | ✅ mounted in Topbar (auth-gated) + transient toast on new notification (5s auto-dismiss) via `onNotification` callback |
| P3-6 | Recent/in-flight run list (URL-independent) | `OrchestrationPanel.tsx`, `orchestration.ts` | M | ✅ `listOrchestrationRuns` API + `?mine=true`; panel "Phiên gần đây" list click-to-load, polls every 3s |
| P3-7 | Orchestrator readable summary | orchestrator | S | ✅ done in 2D |

**Verify:** FE `tsc --noEmit` clean + `vite build` green. BE full sln 783/783, migrations 0037-0040 applied to local clawbot-sqlserver.

**Verify:** plan renders as DAG; agent nodes show use-count + current task; terminal run fires user-targeted notification; bell live on any page.

---

## Phase 4 — Autonomy loop — ~0.5-1 sprint ✅ DONE 2026-06-28

| # | Task | Files | Effort | Status |
|---|---|---|---|---|
| P4-1 🔁 | Drive autonomous runs from `AgentSchedules`→`AgentScheduleWorker` into tool-capable agents | `AgentScheduleRunner.cs` | S | ✅ verified — AgentScheduleRunner calls the same DI-scoped `AutonomousOrchestrator` which now has ToolRegistry + approval resolver injected, so scheduled runs automatically use tool-capable agents |
| P4-2 | Dry-run mode: preview tool actions, no side effects | tool registry, worker | M | ✅ `ToolContext.DryRun` + `AutonomousRunRequest.DryRun`; AdapterTool + all 4 content tools short-circuit with a `[dry-run] would …` preview; worker passes flag through |
| P4-3 | Per-tool RBAC (reuse existing perm codes: `content:write`, `ads:write`...) + cost-guard + PII on every action | tool registry, `OrchestratorCostGuard` | M | ✅ tools carry `RequiredPermission` metadata (RbacSeeder codes); `ToolRegistryFactory.KnownTools` exposes the catalog; `OrchestrationV2Endpoints.ValidateAllowedToolsAsync` rejects unknown tool names + denies granting a tool whose `RequiredPermission` the admin lacks (5 tests). Agent allow-list enforced at worker (P1-4); cost-guard wraps run; PII redaction in sink |
| P4-4 | Risk gate honors `Tenant.RequireOrchestrationApproval`: high-risk tools (publish/ad-spend/customer msg) pause at approval when toggle on | tools, orchestrator approval path | M | ✅ `ToolRiskLevel` (Low/High) on IAgentTool; content.publish + ads-agent + chat-agent marked High; `IOrchestrationApprovalResolver`/`EfOrchestrationApprovalResolver` read `Tenant.RequireOrchestrationApproval`; orchestrator loads per-run + passes to worker; worker refuses High-risk tools with a needs-approval observation when toggle on |

**Verify:** 2 risk-gate tests (refused when toggle on, allowed when off) + full sln 788/788.

**Verify:** schedule fires tool-capable run; dry-run previews w/o side effects; toggle on → high-risk pauses for approval.

---

## Module — Channel & token configuration (SPEC §5.2) — ✅ DONE 2026-06-28 (2C e2e external)

| # | Task | Files | Migration | Effort | Status |
|---|---|---|---|---|---|
| M-1 [DB] | Credential storage (encrypted via `IEncryptor`): Pancake user token + `pancake_pages` (P2-1), FB app id/secret + page tokens + scopes, Zalo OA token/refresh + app secret | EF config + `deploy/migrations/00XX_channel_credentials.sql` | new/extended tables | M | ✅ Pancake user token on AppUser (0036) + pancake_pages (0037); FB/Zalo via `SocialCredential` entity (encrypted JSON blob per tenant+provider+page_id) + [0041_social_credentials.sql](deploy/migrations/0041_social_credentials.sql) + `EfSocialCredentialResolver`; `GraphSocialPublisher` resolves DB-first, falls back to options. Migration applied to local DB |
| M-2 [BE] | Admin endpoints (perm `channels:manage`) CRUD; secrets masked on read, never plaintext | `AdminChannelsEndpoints.cs` | — | M | ✅ [AdminChannelsEndpoints.cs](src/api/Clawbot.Api/Endpoints/AdminChannelsEndpoints.cs) — connect/mint/list, gated `channels:manage`, list never exposes token |
| M-3 [BE] | "Connect/Test" per channel: Pancake `GET /pages` | channel services | — | M | ✅ `IPageListGateway.ListAsync` + `/api/admin/channels/pancake/connect` (validates user token by listing pages; 401/expired → 502+detail) |
| M-4 [BE] | Save Pancake user token → list pages → admin selects pages → mint+store each page token | `PancakePageTokenService` (P2-2) | — | M | ✅ `/api/admin/channels/pancake/pages` mints+stores each selected page via `MintAndStoreAsync`; per-page connected/failed status |
| M-5 [FE] | "Cấu hình kênh" settings page: per channel/page status, connect/test, page picker | new FE feature page + `routes.tsx` | — | L | ✅ [ChannelManagementPage.tsx](src/frontend/clawbot-web/src/features/admin/ChannelManagementPage.tsx) — Pancake connect section: paste user token → list pages (checkbox picker) → mint+store selected; connected-pages status list (never token). `tsc` + `vite build` clean |
| M-6 | 401/expired → re-auth prompt; autonomous run surfaces re-auth need | resolver, run sink | — | S | ✅ connect endpoint surfaces 401 as 502+detail; `AutonomousRunSink.BuildFailBody` detects 401/unauthorized/expired/token in failure reason and adds a re-auth hint to the notification body |

**Verify:** admin connects Pancake (token validated, pages picked, page tokens minted) + FB/Zalo app creds; status shown; expired token blocks run with clear prompt.

---

## Sequencing & dependencies
```
Phase 0 ──► Phase 1 ──► Phase 2 ──► Phase 3 ──► Phase 4
                          ▲
   Module (M-1..M-6) ─────┘  (needed before P2 real sends/posts; M-1/P2-1 shared)
```
- **Do first:** Phase 0 (unblocks everything), then P1-1..P1-3 (tool spine).
- **Module + 2A** gate any real outbound (token correctness).
- P2-8 (Graph publisher) needs platform app + permissions → start app-review early (long lead time).

## Cross-cutting DoD (per CLAUDE.md §10)
- TDD, ≥80% coverage on touched Domain/Application.
- EF mapping + `deploy/migrations/00XX_*.sql` for every schema change (ADR-009); enums as string (ADR-003); secrets encrypted, never in `appsettings.json`.
- EARS comment above business logic; trace to `SPEC-16` criteria.
- `dotnet format` clean, `dotnet build` 0 warnings.

## Open items to confirm before coding (SPEC §10)
- System/reviewer-agent actor identity for audit attribution.
- ReAct loop iteration cap + per-task cost ceiling values.
- Streaming vs 120s timeout for the configured provider (DeepSeek/Anthropic) ([[openai-sdk-streaming-usage]]).
- FB/Zalo app review + required scopes (`pages_manage_posts`, Zalo OA) — long lead.
- Verify Pancake send-message body schema vs docs (Message/InboxMessage/PrivateReply/ReplyComment).
