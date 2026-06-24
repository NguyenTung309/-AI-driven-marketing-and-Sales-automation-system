---
phase: requirements
title: Dynamic Agent Orchestration v2 — Requirements & Problem Understanding
description: Define autonomous scheduled agent-to-agent orchestration over Semantic Kernel
feature: dynamic-agent-orchestration-v2
date: 2026-06-24
status: draft
---

# Dynamic Agent Orchestration v2 — Requirements & Problem Understanding

## Context Quality Assessment

- Existing context is strong for V1: catalog-backed planning, `AgentSession`/`agent_traces`, RBAC/cost guard, and LLM provider config docs already exist.
- Existing context is weak for V2: scheduled A2A and dynamic sub-agents are intended product direction, not current shipped behavior.
- Reuse confirmed conventions: no plaintext secrets in repo, SQL migrations without `GO`, PII-redact derived content, LLM provider key stored encrypted.

## Executive Summary

ClawBot needs Dynamic Agent Orchestration v2: a Semantic Kernel coordinator that accepts goals from chat, documents, or manual input; creates or selects sub-agents as data; lets those agents collaborate through agent-to-agent messages; and runs those workflows automatically on daily, weekly, monthly, and quarterly schedules. V2 upgrades orchestration from a plan executor into an autonomous but bounded agent network.

## Problem Statement

ClawBot's orchestration foundation can plan and run tasks, but it still behaves like a coordinator over known agents. The intended product flow is stronger: a user provides a goal through chat, document upload, or manual input; the system understands the work, creates a plan, creates/chooses specialized sub-agents, lets those agents collaborate, and reruns the workflow on schedules.

Affected users:

- Managers want recurring business workflows without manually invoking agents.
- Sales/marketing leads want agents that coordinate campaign, lead, content, report, and review work.
- Admins need cost, permission, trace, and LLM-provider control for autonomous runs.

Current workaround: seed static demo traces or manually run individual agents. That does not show real agent-to-agent autonomy.

## Goals

1. Enable autonomous Semantic Kernel coordination across multiple sub-agents.
2. Accept orchestration input from manual text, chat-derived context, and document-derived context.
3. Represent sub-agents as data so new personas/workers can be created without codegen or redeploy.
4. Support scheduled orchestration cadences: daily, weekly, monthly, quarterly presets, plus optional cron for advanced schedules.
5. Seed local OpenAI-compatible LLM config for demo/test runs with encrypted DB storage.
6. Preserve guardrails: RBAC, tenant isolation, cost caps, approval gates, cancellation, traceability.

## Non-Goals

- Runtime code generation or deploying new binaries from prompts.
- Unbounded autonomous loops.
- Running destructive external actions without explicit approval.
- Committing plaintext API keys to source, docs, or seed SQL.

## Functional Requirements

1. **Autonomous Semantic Kernel coordinator** — SK coordinates planning, sub-agent selection/creation, A2A messages, review, bounded re-plan, and stop decisions.
2. **Input sources** — accept goals from manual form, chat conversation summary, or uploaded/parsed document content reference.
3. **Sub-agents as data** — create or resolve sub-agent definitions without codegen or redeploy. Each sub-agent has persona, allowed tools/skills, model binding, budget, memory scope, and schedule eligibility.
4. **Agent-to-agent collaboration** — use an A2A message envelope so coordinator, worker, reviewer, and reporter agents can exchange tasks, artifacts, results, critiques, and decisions.
5. **Recurring schedules** — support daily, weekly, monthly, and quarterly presets plus optional cron expression, with timezone, next-run calculation, missed-run policy, and no overlapping runs by default.
6. **Demo LLM seed** — seed an OpenAI-compatible local provider into `llm_configs` for test/demo runs, storing the key encrypted in DB and binding it to `orchestrator` and seeded sub-agents.
7. **Guardrails** — enforce tenant scope, RBAC, cost cap, concurrency cap, cancellation, approval gates, max rounds, and trace/audit persistence.

## Non-Functional Requirements

- Planning target: one coordinator planning call under normal path; avoid model waterfalls before first plan is visible.
- Execution target: bounded max rounds and max concurrency per tenant.
- Reliability: scheduled runs must be idempotent per schedule window.
- Security: never log plaintext API keys, prompts with raw PII, or raw document content beyond allowed retention.
- Observability: every run has session, trace events, A2A messages, cost ledger entries, and final status.

## User Journeys

### Journey 1 — Manual goal to autonomous run

1. Manager enters: “Launch HSK4 campaign next quarter.”
2. Coordinator creates/chooses research, content, ads, reviewer, and report sub-agents.
3. Agents exchange A2A messages, produce artifacts, reviewer critiques output, reporter summarizes final result.
4. Manager sees trace, costs, outputs, and stop/replan decisions.

### Journey 2 — Daily lead triage schedule

1. Sales lead creates daily schedule at tenant timezone morning window.
2. Scheduler fires once per day, creates a new orchestration session, and skips if previous run overlaps.
3. Lead triage sub-agent reviews hot/warm leads, reviewer checks recommendations, report agent writes summary.

### Journey 3 — Local LLM demo seed

1. Developer runs seed with local OpenAI-compatible config.
2. Seed encrypts key before inserting/updating `llm_configs`.
3. Orchestrator and seeded sub-agents bind to model `cx/gpt-5.5` at `http://localhost:20128/v1`.
4. Demo orchestration can call local model without external vendor credentials.

## Acceptance Criteria

- Submit manual/chat/document goal → coordinator creates a session, sub-agent definitions or references, A2A messages, and a bounded execution loop.
- Daily/weekly/monthly/quarterly schedules create new session runs at expected times and do not duplicate the same schedule window.
- Seeded local OpenAI-compatible LLM config is encrypted in DB, active, and bound to `orchestrator` plus seeded sub-agents.
- Trace shows coordinator → sub-agent → reviewer/reporter handoffs.
- RBAC denies unauthorized schedule/run/approve/manage operations.
- Cost guard stops autonomous work before exceeding tenant cap.
- Tests cover schedule calculation, overlap prevention, A2A handoff, cost cap, cancel, and encrypted seed path.

## Success Criteria

Same as acceptance criteria; completion requires automated tests plus demo script update.

## Constraints

- Reuse `AgentSession`/`agent_traces` for run state and trace where possible.
- Add schedule/A2A/sub-agent tables only where V1 models cannot represent the runtime contract.
- SQL migrations must not use `GO`; indexes on newly added columns get their own migration file when needed.
- LLM config uses existing encryption and resolver flow.
- Local demo key may be disposable, but source control must not contain plaintext secret material.
- Recurrence uses tenant timezone, not server local time.
- Default overlap policy: skip new run when previous run for the same schedule window is still running.

## Out of Scope

- Vendor publisher integrations.
- Runtime shell/code execution by agents.
- Cross-tenant agent sharing.
- Production cron scale-out tuning beyond single-cluster locking semantics.

## Assumptions

- V1 orchestration foundation remains available and can be extended.
- Existing LLM encryption service can be reused by demo seed code.
- Existing agent catalog can seed initial sub-agent definitions.
- Local OpenAI-compatible endpoint behaves like `/v1/chat/completions`.

## Open Questions

Resolved during requirements review 2026-06-24:

- **UI entrypoint:** start with a dedicated Orchestration page for schedule/run management; later embed summary cards into Agent Dashboard.
- **Document input:** use document/content references and extracted summaries, not raw uploaded text, as orchestration input.
- **High-risk approval:** use tenant policy to decide which action classes require approval even when auto-run is enabled.
- **Cadence representation:** support daily/weekly/monthly/quarterly presets plus optional cron for advanced schedules.

No blocking requirements questions remain for design review.

## Traceability Matrix

| Requirement | Acceptance Criteria |
|---|---|
| SK coordinator | Goal creates bounded A2A run |
| Input sources | Manual/chat/document goal accepted |
| Sub-agents as data | New sub-agent definition works without redeploy |
| A2A collaboration | Trace shows coordinator/sub-agent/reviewer handoffs |
| Schedules | Daily/weekly/monthly/quarterly run once per window |
| LLM seed | Encrypted local provider config bound to agents |
| Guardrails | RBAC/cost/cancel tests pass |

## Review Notes

- Requires `/review-requirements` before implementation.
- Requires `/review-design` after design doc is filled.
- Key decision from user: full autonomous A2A direction, not only scheduler over fixed agents.

## Next Steps

1. Review requirements with `/review-requirements`.
2. Review design with `/review-design` after requirements pass.
3. Execute plan only after both reviews pass.
