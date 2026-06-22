---
phase: planning
title: Dynamic Agent Orchestration — Project Planning & Task Breakdown
feature: dynamic-agent-orchestration
date: 2026-06-21
status: implemented
---

# Dynamic Agent Orchestration — Project Planning & Task Breakdown

> Inputs: [requirements](../requirements/2026-06-20-feature-dynamic-agent-orchestration.md), [design](../design/2026-06-20-feature-dynamic-agent-orchestration.md), workflow verification `w0fln1db0` (7/8 verifiers completed; cost guard completed manually from source). Scope = **planning only**; implementation/commit by owner.

## Milestones
**What are the major checkpoints?**

- [x] **M1 — RFC + dependency gate:** confirm/supersede RFC-001; add `Microsoft.SemanticKernel` via central package management; build/audit clean.
- [x] **M2 — Persistence + permissions:** extend `AgentSession`/`Tenant`, seed orchestration RBAC, verify endpoint filters.
- [x] **M3 — Planner foundation:** SK chat adapter, `DbAgentCatalog`, structured plan validator, lifecycle state machine.
- [x] **M4 — Execution core:** parallel DAG executor, 6/8 live `IAgent` adapters (lead/report stubbed), cost guard, PII redaction, bounded re-plan.
- [x] **M5 — API + UI:** extend `orchestrator.proto`, API endpoints, minimal panel in `AgentDashboardPage`.
- [x] **M6 — Verification:** unit/component + SQLite integration tests, concurrency/cost race tests, build 0/0. (Docker-based Integration.Tests not run.)

## Task Breakdown
**What specific work needs to be done?**

### Phase 0: RFC + dependency gate

- [x] **T0.1 — Update RFC-001 for SK dependency** *(S)* — done 2026-06-21: RFC accepted, SK 1.77.0 CPM entry + Agents.Core reference added; restore/audit remains under T6.2.
  - Confirm Option B: SK = planner/plugin host only; direct `ScopedLlmChatClient` remains chat path.
  - Record package: `Microsoft.SemanticKernel` **1.77.0** in [Directory.Packages.props](../../Directory.Packages.props) (central package management).
  - Explicitly reject `SemanticKernel.Connectors.Anthropic` preview/community connector.
  - Verification: `dotnet restore` + `dotnet list package --vulnerable --include-transitive` (or repo equivalent NuGetAudit build).
  - Files: [.sdd/rfcs/001-semantic-kernel-vs-direct-anthropic.md](../../.sdd/rfcs/001-semantic-kernel-vs-direct-anthropic.md), [Directory.Packages.props](../../Directory.Packages.props), [Clawbot.Agents.Core.csproj](../../src/agents/Clawbot.Agents.Core/Clawbot.Agents.Core.csproj).

### Phase 1: Domain + schema + RBAC foundation

- [x] **T1.1 — Extend `AgentSession` state model** *(M)* — done 2026-06-21: RED/GREEN domain tests added; `RequiresApproval`, `ReplanCount`, `RowVersion`, status constants, and core transition methods implemented.
  - Add `RequiresApproval`, `ReplanCount`, `RowVersion` (`byte[]?`) to [AgentSession.cs](../../src/shared/Clawbot.Domain/Agents/AgentSession.cs).
  - Add transition methods: `MarkPlanned`, `Approve`, `Start`, `Pause`, `Resume`, `Cancel`, `Fail`, `Finish`, `IncrementReplan`.
  - Guard illegal transitions; preserve existing `Start`/`Finish` callers or provide compatibility overloads.
  - Tests: state transition unit tests, illegal transition tests, legacy chat session path still works.

- [x] **T1.2 — Add tenant autonomy toggle** *(S)* — done 2026-06-21: `Tenant.RequireOrchestrationApproval` + setter + RED/GREEN tests.
  - Add `RequireOrchestrationApproval` to `Tenant` (default `false`).
  - Snapshot into `AgentSession.RequiresApproval` at plan creation.
  - Tests: tenant toggle default + snapshot behavior.

- [x] **T1.3 — DDL migrations + EF config** *(M)* — done 2026-06-21: EF mapping updated; DDL files `0028`, `0029`, `0030` added (no GO).
  - Add `agent_sessions.requires_approval BIT NOT NULL DEFAULT 0`.
  - Add `agent_sessions.replan_count INT NOT NULL DEFAULT 0`.
  - Add `agent_sessions.row_version ROWVERSION`.
  - Add `tenants.require_orchestration_approval BIT NOT NULL DEFAULT 0`.
  - Optional separate index file: `(tenant_id, status, started_at)` for orchestration listing.
  - Follow [[clawbot-migration-no-go]]: **no `GO`**, one `SqlCommand`/file; index after ALTER-added column in its own file.
  - EF: [DomainModelConfigurations.cs](../../src/shared/Clawbot.Infrastructure/Persistence/Configurations/DomainModelConfigurations.cs) with `.IsRowVersion()`.
  - Tests: migration/EF mapping test if existing pattern supports it.

- [x] **T1.4 — Seed orchestration RBAC** *(S)* — done 2026-06-21: `orchestration:view/run/approve/manage` seeded; `orchestrator` AgentConfig seed added.
  - Add to `RbacSeeder.Matrix`: `orchestration:view`, `orchestration:run`, `orchestration:approve`, `orchestration:manage`.
  - Roles: `run` + `view` = Admin/SalesLead/Marketer; `approve` = Admin/SalesLead; `manage` = Admin.
  - Endpoint enforcement uses `RequirePermission("...")` (no policy registration needed).
  - Tests: RbacSeeder creates permissions + role links; endpoint 403 for missing perm.

### Phase 2: SK planning foundation

- [x] **T2.1 — Implement `ClawbotChatCompletionService` adapter** *(M)* — done 2026-06-21: adapter + RED/GREEN tests (3/3) implemented; LLM-scope wrapper remains for orchestrator integration in T2.4.
  - Implements SK `IChatCompletionService` over `ScopedLlmChatClient`.
  - Map SK `ChatHistory` → existing `ChatTurn` list + system/user prompt.
  - Non-streaming planner path only; streaming SK calls either buffer or throw `NotSupportedException` with safe message.
  - Begin LLM scope explicitly: `ILlmCallScope.Begin(tenantId, "orchestrator")`.
  - Seed `orchestrator` `AgentConfig` (`Agent-Orchestrator`, `AgentType="planner"`) so resolver can bind LLM config.
  - Tests: history mapping, missing binding → `LlmConfigNotConfiguredException`, scope propagation through SK call.

- [x] **T2.2 — Implement `IAgentCatalog` + `DbAgentCatalog`** *(M)* — done 2026-06-21: catalog interface + SQLite-backed tests (3/3) implemented.
  - Replace static `DefaultAgentRegistry` as source of truth.
  - Read tenant-scoped `AgentConfig` rows; expose code/displayName/agentType/model/status/description/inputSchema/orchestratable.
  - Map old short names (`content`, `research`) ↔ DB codes (`content-agent`, `research-agent`) where needed.
  - All 8 are `orchestratable=true` for iteration 1.
  - Tests: catalog reads seeded agents, disabled/missing agents excluded or flagged per design, name mapping works.

- [x] **T2.3 — Structured plan schema + validator** *(M)* — done 2026-06-21: immutable plan records + DAG validator + tests; validates non-empty, unique task ids, known/orchestratable agent, dangling deps, cycles, max task count, and max input size; PII redaction integration remains in planner/runtime.
  - Define immutable records for `OrchestrationPlanDocument`, `OrchestrationPlanTask` matching `PlanJson`.
  - Validate: non-empty tasks, unique task ids, agent exists & orchestratable, no dangling dependencies, no cycles, max task count, max input size.
  - Redact goal + task descriptions before persist via `IPiiRedactor`.
  - Tests: empty goal, unknown agent, cyclic DAG, duplicate id, PII redaction.

- [x] **T2.4 — Implement single-shot SK planner** *(L)* — done 2026-06-21: `SemanticKernelPlanGenerator` (JSON/markdown-fence normalization/validation) + `SemanticKernelOrchestrator` glue (catalog prompt context → generate → PII-redact goal+task descriptions → autonomy snapshot → cost pre-flight → `AgentSession.CreatePlan`). Cost-blocked auto-run falls back to `PendingApproval`. Iteration 1 uses SK chat planner + catalog prompt context; direct SK KernelFunction execution deferred by design update.
  - Build SK planner prompt with catalog entries, descriptions, and input schemas (direct KernelFunction execution deferred).
  - Prompt returns strict JSON DAG (no direct tool execution during planning).
  - Persist `AgentSession` with `Goal`, `Status` = `Running` if auto-run else `PendingApproval`, `PlanJson`, `RequiresApproval` snapshot.
  - Pre-flight cost estimate before auto-run (see T3.3).
  - Tests: stable JSON parse, invalid JSON handled, empty goal handled, auto-run vs approval mode.

### Phase 3: Execution core + adapters

- [x] **T3.1 — Parallel DAG executor** *(L)* — done 2026-06-21: wave-based executor, concurrency cap=3, dep ordering, failure→skip dependents, bounded re-plan hook, per-task progress callback. AgentService persists `PlanJson` + `AgentTrace` after each task and checks pause/cancel between tasks. Tests cover dependency ordering, concurrency, failure/skip, re-plan, per-task trace persistence, paused-between-tasks guard.
  - Execute ready tasks concurrently, cap = **3**.
  - Respect dependencies; failed task triggers bounded re-plan (T3.4) or skip policy.
  - Persist per-task status in `PlanJson` with `RowVersion` retry; append `AgentTrace` on every phase.
  - Pause/resume/cancel checked between tasks and passed via `CancellationToken` into running tasks.
  - Tests: parallel independent tasks, dependency ordering, pause/cancel, concurrency conflict retry.

- [x] **T3.2 — Implement all 8 `IAgent` adapters** *(L)* — done 2026-06-21: 8 live adapters. Core adapters cover chat/content/research/docs/ads/sale_assist; AgentService adapters cover lead/report via shared `LeadAgentRunner`/`ReportAgentRunner`. Base contract + lead/report adapter tests cover JSON output and error mapping; alias map covers code/shortname/agenttype.
  - **chat:** `ChatAgent.ReplyAsync`; input keys: `tenant_id`, `user_text`, optional `conversation_id`, `history` JSON, `kb_module_code`, `sender_handle`, `source_platform`, `matched_scenario_template`.
  - **content:** `ContentAgent.GenerateAsync`; keys: `tenant_id`, `platform`, `brief`, optional `brief_id`, `kb_module_code`.
  - **research:** `ResearchAgent.ScanAsync`; keys: `tenant_id`, `geo`, `keywords` JSON/CSV.
  - **lead:** split operation in input (`score` vs `create`); current logic lives in [LeadAgentGrpcService.cs](../../src/agents/Clawbot.AgentService/Services/LeadAgentGrpcService.cs) → extract reusable core service or adapter with DB dependencies.
  - **docs:** `DocsAgent.RenderAsync`; decide adapter also persists via `IDocumentStorage` + `GeneratedDocument`, because gRPC service currently handles persistence.
  - **ads:** `AdsAgent` methods: evaluate/apply/lookalike/remarketing; connector-missing = graceful skipped result.
  - **sale_assist:** `DraftAsync`, `SummarizeAsync`, `SuggestUpsellAsync`, `AutoSummaryAsync`; adapter begins LLM scope; redact persisted summaries.
  - **report:** logic currently lives only in [ReportAgentGrpcService.cs](../../src/agents/Clawbot.AgentService/Services/ReportAgentGrpcService.cs) → extract core `ReportAgent` service or adapter with `AppDbContext` + skills.
  - Tests: one adapter test per operation; input validation; JSON output serialization; failure mapping to `AgentResult.Error`.

- [x] **T3.3 — Implement cost-surprise guard** *(L)* — done 2026-06-21; hardened 2026-06-22: `OrchestratorCostGuard` with `CanStartAsync` pre-flight plus tracker-backed `TryReserveAsync`/`ReleaseReservationAsync`. `DbClaudeCostTracker` now implements shared DB reservation rows inside serializable transactions, so the cap check is process-shared instead of singleton-local. Wired into planner pre-flight and each runtime task via `RuntimeGuardedAgent`; mid-run cap hit fails task with `cost_cap_midrun`. Tests cover pre-flight, concurrent overspend, release, adjust, DB reservation/release, and service-level mid-run failure.
  - `CanStartAsync(tenant, estimate)` blocks auto-run if `MonthToDate + estimate > 200`.
  - `TryReserveAsync(tenant, estimatedUsd)` persists an id-addressable reservation ledger row; `Release/Adjust` clears that exact row after observed agent spend is recorded, making duplicate release a no-op.
  - Runtime captures the reservation timestamp once and reuses the reservation id for release/adjust, so month-boundary task runs do not strand prior-month budget.
  - Reporting endpoints exclude reservation rows from user-facing usage totals.
  - Tests: three concurrent tasks cannot overspend cap; estimate blocks auto-run; mid-run cap hit → `Failed(cost_cap)` trace; DB reservation rows reserve/release month-to-date budget idempotently.

- [x] **T3.4 — Bounded LLM re-plan** *(M)* — done 2026-06-21: executor re-plan callback (cap=2); `OrchestrationReplan.Merge` preserves completed tasks + prefixes regenerated tasks; `BuildReplanGoal` injects failure context; service replanner calls SK planner under orchestrator scope + `IncrementReplan`. Tests: executor replan-once/stop-after-max (2/2), merge/goal (2/2).
  - On adapter failure, if `ReplanCount < 2`, call SK planner with failure context + completed outputs; patch remaining DAG.
  - Else mark task/plan failed or skipped per policy; trace `replan_limit_reached`.
  - Tests: one failure re-plans, third failure stops, no infinite loop.

### Phase 4: gRPC/API lifecycle

- [x] **T4.1 — Extend `orchestrator.proto` + regenerate contracts** *(M)* — done 2026-06-21: added `Submit`/`GetPlan`/`UpdatePlan`/`Approve`/`Control` RPCs + `SubmitRequest`/`SessionRef`/`UpdatePlanRequest`/`ControlRequest`/`SessionResponse`; extended `PlannedTask` with status/output/error; kept legacy `Plan`/`Trace`. Contracts regenerate + build clean.
  - Add RPCs: `Submit`, `GetPlan`, `UpdatePlan`, `Approve`, `Control`; keep existing `Plan`/`Trace` for compatibility or mark old `Plan` as alias.
  - Extend `PlannedTask`: `status`, `output`, `error`.
  - Add message types for update/control.
  - Build `Clawbot.Agents.Contracts` to regenerate stubs.
  - Tests: compile-time contract tests / service tests.

- [x] **T4.2 — Extend `OrchestratorGrpcService`** *(M)* — done 2026-06-21: lifecycle RPCs wired to `SemanticKernelOrchestrator` + inline DAG execution (orchestrator LLM scope, per-task trace append/PlanJson persist, PII-redacted output/error, mid-run cost guard, pause/cancel checks, RecordRun, Finish/Fail). Error mapping: validation→InvalidArgument, state→FailedPrecondition, missing→NotFound. DI registered in AgentService. Tests 11/11 (auto-run, approval gate, approve→run, cancel, PII redaction goal/output, dependent DAG, mid-run cost cap, pause-between-tasks, trace persistence, not-found).
  - Wire lifecycle RPCs to `SemanticKernelOrchestrator`.
  - Map validation errors → `InvalidArgument`, state conflicts → `FailedPrecondition`, missing plans → `NotFound`.
  - Stream trace from `agent_traces` / in-memory bridge as needed.
  - Tests: RPC happy paths + error mapping.

- [x] **T4.3 — Add API gRPC client + `OrchestrationEndpoints`** *(M)* — done 2026-06-21: registered `Orchestrator.OrchestratorClient`; added `OrchestrationEndpoints` (submit/get/plan-update/approve/pause/resume/cancel) with per-route `RequirePermission` (run/view/approve/manage) + RpcException→HTTP mapping. `MapOrchestration()` wired. API builds clean. RBAC alignment fix 2026-06-22: `orchestration:manage` restricted to Admin only per D10.
  - Register `Orchestrator.OrchestratorClient` in [Clawbot.Api/Program.cs](../../src/api/Clawbot.Api/Program.cs); existing agent clients are registered but orchestrator client is missing.
  - Add [OrchestrationEndpoints.cs](../../src/api/Clawbot.Api/Endpoints/OrchestrationEndpoints.cs).
  - Routes: submit/get/update/approve/pause/resume/cancel/trace.
  - Apply `RequirePermission` per route.
  - Tests: endpoint auth, validation, gRPC error mapping.

### Phase 5: Frontend minimal panel

- [x] **T5.1 — Add typed API client** *(S)* — done 2026-06-21: `shared/api/orchestration.ts` (submit/get/updatePlan/approve/control) + `OrchestrationSessionDto`/`OrchestrationTaskDto` types. tsc clean.
  - New [shared/api/orchestration.ts](../../src/frontend/clawbot-web/src/shared/api/orchestration.ts) (pattern: `agents.ts`).
  - Types: `PlanDto`, `PlanTaskDto`, `TraceDto`, control actions.
  - Tests: type/contract compile.

- [x] **T5.2 — Add panel to `AgentDashboardPage`** *(M)* — done 2026-06-21: `OrchestrationPanel` (goal textarea, submit, DAG task list w/ status, approve/pause/resume/cancel, cost/requiresApproval warnings) perm-gated via `useAuthStore` (`orchestration:run/approve/manage`); mounted in `AgentDashboardPage`. tsc clean.
  - Minimal UI: goal textarea, submit, plan DAG/task list, trace list, approve/pause/resume/cancel buttons.
  - Buttons perm-gated by `orchestration:*`; hidden/disabled based on plan `Status`.
  - Show cost/pre-flight warnings + `requiresApproval` status.
  - Tests: component render + state transitions if test infra exists; otherwise typecheck + manual screenshot via run skill later.

### Phase 6: End-to-end verification

- [x] **T6.1 — Integration tests** *(L)* — done 2026-06-21: end-to-end via `OrchestratorGrpcService` over SQLite AppDbContext — submit→persisted PlanJson + auto-run completion, tenant-toggle approval gate, approve→run, cancel, PII-redacted persisted goal/output, dependent DAG, per-task traces, pause-between-tasks, mid-run cost cap. Parallel/dep ordering (`ParallelDagExecutorTests`), cost-cap race/release/adjust (`OrchestratorCostGuardTests`), PII at planner (`SemanticKernelOrchestratorTests`).
  - Submit NL goal → persisted `AgentSession.PlanJson` with valid DAG.
  - Auto-run default path starts execution.
  - Tenant toggle path stops at `PendingApproval` until approve.
  - Parallel DAG with dependency constraints.
  - Cost cap race (3 concurrent tasks).
  - PII redaction before persist.

- [x] **T6.2 — Build/test gates** *(M)* — done 2026-06-21: `dotnet build Clawbot.sln` 18 projects 0 warn/0 err (NuGetAudit + CA pass as build is error-gated). Tests: Domain 59, Application 3, Agents 207, AgentService 31, Api 104, Infrastructure 124 — all pass. Frontend `tsc -b` clean. (Docker-backed Integration.Tests require Testcontainers daemon.)
  - `dotnet build ClawBot.sln` → 0 warnings/0 errors.
  - Relevant test projects: Agents.Core, AgentService, Api, Infrastructure.
  - Frontend: typecheck/build for touched FE.
  - NuGetAudit clean after SK add.

### Phase 7: Alignment follow-up fixes

> Added 2026-06-21 from `/check-implementation` final alignment review. Earlier phases remain implemented, but these gaps keep the feature **in-progress**.

- [x] **T7.1 — Return full editable DAG payload through proto/API** *(M)*
  - Add task `input` to `PlannedTask` and/or expose full `plan_json` in lifecycle responses.
  - Ensure submit/get/update/approve/control responses carry enough structured data for review/edit/run.
  - Update API DTOs and tests.

- [x] **T7.2 — Stream persisted dynamic traces from `Trace` RPC** *(M)*
  - Replace legacy in-memory trace-only path for dynamic sessions with `agent_traces` reads.
  - Keep legacy compatibility if needed, but submitted sessions must return persisted traces.
  - Add submit→trace gRPC test.

- [x] **T7.3 — Persist all required trace phases** *(M)*
  - Emit `planned`, `started`, `completed`, `failed`, `re-planned`, and `skipped` where applicable.
  - Extend tests for pre-run, start, failure, replan, and skip audit rows.

- [x] **T7.4 — Guard cancel state transitions** *(S)*
  - Restrict cancel to valid running/paused states per state machine.
  - Illegal cancel on pending/completed/failed returns 409/FailedPrecondition.
  - Add illegal-transition tests.

- [x] **T7.5 — Complete frontend review/edit/trace flow** *(M)*
  - Wire plan update/edit UI to existing API client.
  - Add trace fetch/render support to shared client and panel.
  - Preserve permission/status gating for approve/pause/resume/cancel.

- [x] **T7.6 — Make catalog metadata data-resolved** *(M)*
  - Store/resolve agent description, input schema, and orchestratable flag from data rather than synthetic defaults.
  - Seed current 8 agents with planner-ready metadata.
  - Preserve SC-7: adding an agent row makes it planner-visible without orchestrator code changes.

- [x] **T7.7 — Fix human-override RBAC semantics** *(S)*
  - Allow operators who can run orchestration to pause/cancel their bad run, or split manage actions if needed.
  - Keep admin-only controls only where truly administrative.
  - Add endpoint authorization tests for SalesLead/Marketer override behavior.

- [x] **T7.8 — Record planner/replan LLM cost to ledger** *(M)*
  - Ensure `ClawbotChatCompletionService` or surrounding orchestrator path records planner/replan spend under `agentCode=orchestrator`.
  - Cost guard must preflight against complete month-to-date spend.
  - Add ledger assertion tests.

- [x] **T7.9 — Refresh cost guard month-to-date spend per reservation** *(S)*
  - Avoid stale cached month-to-date values after external ledger writes.
  - Preserve per-tenant/month lock semantics.
  - Add overspend regression test with spend between reservations.

- [x] **T7.10 — Feed real catalog descriptions/input schemas into planner prompt** *(S)*
  - Use data-resolved catalog metadata in `SemanticKernelPlanGenerator` prompt.
  - Test prompt context includes description and input schema, not only code/short name.

- [x] **T7.11 — Hard-stop on mid-run cost cap** *(M)*
  - Treat `cost_cap_midrun` as terminal plan failure, not ordinary task failure that can trigger replanning.
  - Ensure no replanner LLM call occurs after cap breach.
  - Add regression test.

- [x] **T7.12 — Record actual orchestrated agent LLM cost** *(M)*
  - Ensure content/sale-assist and other LLM-backed adapters expose or record actual cost, not only fixed estimate fallback.
  - Keep `RuntimeGuardedAgent` reservation adjust/release accurate.
  - Add tests where actual cost differs from estimate.

## Dependencies
**What needs to happen in what order?**

1. **T0.1 first** — SK dependency cannot land without RFC/audit.
2. **T1 before T2/T3** — state machine + `RowVersion` required before planner/executor persists state.
3. **T2.1 before T2.4** — SK planner needs chat adapter.
4. **T2.2 before T2.3/T2.4/T3.2** — catalog + agent mapping needed for validation/adapters.
5. **T3.3 before auto-run** — cost guard mandatory before parallel auto execution.
6. **T4 after T2/T3 core** — API should wrap working core, not stub lifecycle.
7. **T5 after T4** — FE needs stable endpoint contract.
8. **T6 last** — validates the integrated path.

External deps:
- Audit-clean `Microsoft.SemanticKernel 1.77.0`.
- `orchestrator` `AgentConfig` must be seeded + bound to an active `LlmConfig` per tenant before runtime use.

## Timeline & Estimates
**When will things be done?**

Rough effort (solo experienced dev):

| Phase | Estimate |
|---|---:|
| Phase 0 RFC/dependency | 0.5 day |
| Phase 1 domain/schema/RBAC | 1–1.5 days |
| Phase 2 SK planner foundation | 2–3 days |
| Phase 3 executor/adapters/cost guard | 4–6 days |
| Phase 4 proto/API | 1.5–2 days |
| Phase 5 minimal FE | 1.5–2 days |
| Phase 6 tests/build hardening | 2–3 days |

Total: **12–18 dev-days**, mainly because all 8 adapters + cost race safety are non-trivial.

## Risks & Mitigation
**What could go wrong?**

| Risk | Severity | Mitigation |
|---|---|---|
| SK package/audit breaks build | High | RFC + add only `Microsoft.SemanticKernel`, no connector; run restore/audit before implementation continues. |
| Parallel cost cap race | High | `IOrchestratorCostGuard` with per-tenant/month lock + tests; do not rely on current `DbClaudeCostTracker.RecordAsync` alone. |
| All-8 adapters balloon scope | High | Implement adapter interface + tests per agent; extract core services for Report/Lead where gRPC currently owns logic. If scope slips, cut UI before cutting guardrails. |
| `AgentSession.PlanJson` blob contention | Med | `RowVersion` + targeted retry; append traces independently; cap concurrency at 3. |
| SK `IChatCompletionService` API mismatch | Med | Add adapter tests immediately after package add; support required non-streaming methods first. |
| Missing orchestrator LLM binding | Med | Seed `orchestrator` AgentConfig; surface typed `llm_config_not_configured` error; document setup. |
| PII leaks through goal/plan | High | Redact before persist and before trace messages; tests assert raw phone/email absent. |
| RBAC typo strings | Med | Centralize constants for `orchestration:*`; tests seed + endpoint access. |

## Resources Needed
**What do we need to succeed?**

- SK docs/API reference for `Microsoft.SemanticKernel 1.77.0` (`IChatCompletionService`, `KernelFunction`, JSON structured output pattern).
- Active `llm_configs` row bound to `AgentConfig.Code = "orchestrator"` in local/dev tenant.
- Existing test stack: xUnit + FluentAssertions + API/AgentService test patterns.
- Local SQL Server migration workflow (DDL as source of truth; no EF-generated schema).

## Implementation Order Summary

1. RFC/dependency gate.
2. Domain/schema/RBAC.
3. SK chat adapter + orchestrator AgentConfig seed.
4. Catalog + plan schema/validator.
5. Planner + persistence + pre-flight guard.
6. Executor + cost guard + re-plan.
7. 8 adapters.
8. Proto/API.
9. Minimal FE.
10. Integration/build gates.
