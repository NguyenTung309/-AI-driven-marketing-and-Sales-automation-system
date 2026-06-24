---
phase: planning
title: Dynamic Agent Orchestration v2 — Project Planning & Task Breakdown
description: Implementation-ready task breakdown for autonomous scheduled A2A orchestration
feature: dynamic-agent-orchestration-v2
date: 2026-06-24
status: draft
---

# Dynamic Agent Orchestration v2 — Project Planning & Task Breakdown

## Context Quality Assessment

- V1 orchestration foundation exists; V2 should extend, not replace, `AgentSession`, `agent_traces`, LLM config, RBAC, and cost guard.
- Scope is broad. Implement in vertical slices so each milestone is testable.
- Demo seed path must never place plaintext keys in repo.

## Executive Summary

Build V2 in five milestones: data model, LLM demo seed, autonomous A2A runtime, scheduler, and UI/demo documentation. Each milestone leaves a runnable/testable path behind.

## Milestones

- [x] **M1 — Data contracts:** add sub-agent, A2A message, schedule, and schedule-run models/migrations.
- [x] **M2 — Local LLM demo seed:** encrypted OpenAI-compatible config seed and orchestrator binding.
- [x] **M3 — Autonomous runtime:** SK coordinator loop + A2A mailbox + data-defined sub-agent execution.
- [ ] **M4 — Scheduler:** daily/weekly/monthly/quarterly firing with timezone/idempotency/overlap policy.
- [ ] **M5 — API/UI/demo readiness:** endpoints, frontend panels, trace display, docs, smoke path.

## Task Breakdown

### Phase 1: Foundation data model

- [x] Create domain entities: `AgentDefinition`, `AgentA2AMessage`, `AgentSchedule`, `AgentScheduleRun`.
- [x] Add EF configurations with tenant filters and indexes.
- [x] Add SQL migrations without `GO`.
- [x] Add repository/query methods or DbContext sets only where needed.
- [x] Seed initial sub-agent definitions from the existing 8 agents plus reviewer/reporter defaults.

> Phase 1 done 2026-06-24. Files: `AgentDefinition/AgentA2AMessage/AgentSchedule/AgentScheduleRun.cs`, `AppDbContext` DbSets, `DomainModelConfigurations.cs` configs, migrations `0031`/`0032`, seed `deploy/seed/agent-definitions.sql`, tests `AgentOrchestrationPhase1Tests` (5/5) + `SeedSqlSafetyTests` (6/6). Build 0/0. Migration not yet applied to live DB.

### Phase 2: Local LLM config seed

- [x] Add seed option for local OpenAI-compatible config.
- [x] Read key from env (`CLAWBOT_DEMO_LLM_API_KEY`), not source.
- [x] Encrypt key through existing encryption service before DB upsert.
- [x] Upsert provider: `openai-compatible`, model `cx/gpt-5.5`, base URL `http://localhost:20128/v1`.
- [x] Bind provider to `orchestrator` and seeded V2 sub-agents.
- [x] Add dry-run output that shows whether seed will run without printing key.

> Phase 2 done 2026-06-24. `DemoLlmConfigSeeder` reads `CLAWBOT_DEMO_LLM_API_KEY`, encrypts through `IEncryptor`, upserts local OpenAI-compatible provider, and binds unbound agent configs/definitions. Startup calls seeder from API dev flow; missing key logs skip without printing secret. Tests: `DemoLlmConfigSeederTests` 3/3.

### Phase 3: Autonomous A2A runtime

- [x] Add `A2AMailbox` for send/claim/complete/fail message lifecycle.
- [x] Add `AgentDefinitionCatalog` for data-defined sub-agents.
- [x] Add `AutonomousOrchestrator` coordinator loop: plan, delegate, wait, review, replan, finalize.
- [x] Cap max rounds, runtime, concurrency, and cost.
- [x] Persist all handoffs to `agent_a2a_messages` and `agent_traces`.
- [x] Reject structural plan edits while running; allow steering/pause/cancel.

> Phase 3 done 2026-06-24. Core: `IA2AMailbox`, `IAgentDefinitionCatalog`+`AgentDefinitionCatalogEntry`, `IAutonomousPlanner`, `IAutonomousRunSink`, `AutonomousOrchestrator` (bounded loop, sequential dep-ordered execution, max rounds/cost/cancel). Impl: `EfA2AMailbox`, `AgentDefinitionCatalog` (Infrastructure), `AutonomousPlanner`, `AutonomousRunSink` (AgentService), DI wired. Tests `AutonomousOrchestratorTests` 4/4 (happy, max_rounds, cost_cap_preflight, cancel). ponytail ceiling: sequential execution + non-atomic mailbox claim — fine for single-worker V2; upgrade to parallel/UPDLOCK if throughput needs it. Structural-edit-while-running guard = existing `AgentSession.UpdatePlan` state guard (only Draft/PendingApproval editable).

### Phase 4: Scheduler

- [x] Add `RecurrenceCalculator` for daily/weekly/monthly/quarterly in tenant timezone.
- [x] Add `AgentScheduleWorker` due-schedule scanner.
- [x] Enforce idempotency by `schedule_id + window_key`.
- [x] Default overlap policy: skip and trace `skipped_overlap`.
- [x] Add manual `run-now` path.

> Phase 4 done 2026-06-24. Core: `RecurrenceCalculator` (daily/weekly/monthly/quarterly local windows), `AgentScheduleRunner` (idempotent due/manual runs, session creation, overlap skip), `AgentScheduleWorker` (1-minute due scanner, batch 10). Tests: `RecurrenceCalculatorTests` 5/5, `AgentScheduleRunnerTests` 3/3, registration coverage. ponytail ceiling: single-worker scheduler + DB unique `(schedule_id, window_key)` for idempotency; upgrade to SQL lock/queue if multi-worker throughput matters.

### Phase 5: API/UI/docs

- [x] Add V2 orchestration endpoints for runs, agents, schedules, control, and trace summary.
- [x] Add frontend schedule/run panels or extend Agent Dashboard minimally.
- [x] Update demo script to show V1 foundation and V2 intended/live path clearly.
- [x] Add smoke command for local LLM config + one schedule run.

> Phase 5 done 2026-06-24. API: `/api/orchestration/v2` covers runs, run detail with trace/A2A, control, agents, schedules, and schedule `run-now` with existing RBAC. UI: dedicated `/orchestration` page lists sub-agents, schedules, runs, trace, and A2A messages. Demo docs include V2 smoke path. Tests: endpoint permission source test + frontend typecheck/build + solution build.

## Dependencies

- Existing LLM provider config encryption/resolution.
- Existing `orchestration:*` RBAC permissions.
- Existing `AgentSession` and `agent_traces` persistence.
- SQL Server local/staging schema migration flow.
- Semantic Kernel package already accepted by V1 design; verify NuGetAudit before implementation.

## Timeline & Estimates

- M1: 1–2 days.
- M2: 0.5–1 day.
- M3: 3–5 days.
- M4: 1–2 days.
- M5: 1–2 days.

Total: 7–12 focused engineering days, excluding design review feedback.

## Risks & Mitigation

- **Scope creep:** ship vertical slices; no codegen, no external destructive actions in V2 baseline.
- **Autonomous loop runaway:** max rounds/runtime/cost/concurrency are hard stops.
- **Duplicate scheduled runs:** enforce `window_key` uniqueness.
- **Secret leakage:** seed reads key from env/CLI only; docs and SQL contain placeholders only.
- **Tenant data leak:** all new tables are tenant-scoped and covered by tests.

## Resources Needed

- Local OpenAI-compatible endpoint at `http://localhost:20128/v1`.
- Disposable local API key supplied via env/CLI.
- SQL Server local DB.
- Existing test harness for API/AgentService.

## Acceptance Criteria

- Every milestone has automated tests.
- Demo seed creates encrypted local LLM config and agent bindings.
- A scheduled daily run creates one session and A2A handoff trace.
- Unauthorized user cannot create/manage schedules.
- Cost cap/cancel stops autonomous execution safely.

## Traceability Matrix

| Requirement | Planned Phase |
|---|---|
| SK coordinator | Phase 3 |
| Input sources | Phase 5 |
| Sub-agents as data | Phase 1, Phase 3 |
| A2A collaboration | Phase 3 |
| Schedules | Phase 4 |
| LLM seed | Phase 2 |
| Guardrails | All phases, especially 3–5 |

## Review Notes

- Requires `/review-requirements` and `/review-design` before `/execute-plan`.
- Implementation plan should split M3 if review finds it too large.

## Next Steps

1. Run `/review-requirements`.
2. Run `/review-design`.
3. If both pass, run `/execute-plan` or implement from the superpowers plan.
