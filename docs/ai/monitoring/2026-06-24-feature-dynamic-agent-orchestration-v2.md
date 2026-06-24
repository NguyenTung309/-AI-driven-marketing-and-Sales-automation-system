---
phase: monitoring
title: Dynamic Agent Orchestration v2 — Monitoring & Observability
description: Metrics, logs, alerts, and dashboards for scheduled A2A orchestration
feature: dynamic-agent-orchestration-v2
date: 2026-06-24
status: draft
---

# Dynamic Agent Orchestration v2 — Monitoring & Observability

## Context Quality Assessment

- Existing traces and cost ledger give a base for AI observability.
- V2 adds recurring automation, so duplicate runs, stuck messages, and runaway cost become first-class signals.

## Executive Summary

Monitor V2 by schedule health, A2A message lifecycle, coordinator loop status, cost, RBAC failures, and user-facing run outcomes. Every scheduled run should have a session, A2A timeline, trace, cost ledger, and terminal status.

## Key Metrics

### Performance Metrics

- Coordinator planning latency.
- A2A message claim-to-complete latency.
- Schedule due-to-session-created latency.
- Run wall-clock duration by cadence.
- Worker concurrency utilization.

### Business Metrics

- Runs created by cadence: daily, weekly, monthly, quarterly.
- Successful vs failed scheduled runs.
- Most-used sub-agent definitions.
- Artifacts generated per run.
- Human interventions: approvals, pauses, cancels, steering messages.

### Error Metrics

- Failed runs by reason: `llm_unavailable`, `cost_cap`, `cancelled`, `max_rounds`, `agent_failed`, `rbac_denied`.
- A2A messages stuck in `processing` beyond threshold.
- Duplicate schedule-window attempts blocked.
- LLM config resolution failures.

## Monitoring Tools

- Existing app structured logs.
- `agent_sessions` and `agent_traces` for run timeline.
- `agent_a2a_messages` for collaboration timeline.
- `claude_cost_ledger` for spend.
- Dashboard widgets under Agents/Orchestration.

## Logging Strategy

Log structured fields:

- `tenantId`
- `sessionId`
- `scheduleId`
- `scheduleRunId`
- `windowKey`
- `fromAgent`
- `toAgent`
- `taskId`
- `phase`
- `costReservationId`

Never log:

- plaintext API key
- raw customer PII
- raw uploaded document body
- full prompt if it contains unredacted customer data

## Alerts & Notifications

### Critical Alerts

- Cost cap reached for tenant → notify admin.
- Schedule creates duplicate window attempts repeatedly → inspect worker/idempotency.
- A2A messages stuck processing > threshold → inspect worker crash or deadlock.
- Tenant isolation/RBAC violation attempt spike → security review.

### Warning Alerts

- LLM local/staging endpoint unavailable.
- Scheduled run skipped due overlap multiple times.
- Max rounds reached frequently.
- Replan rate high for one sub-agent.

## Dashboards

- **Orchestration health:** due schedules, active runs, failed runs, stuck messages.
- **A2A timeline:** per session coordinator/sub-agent/reviewer/reporter handoffs.
- **Cost:** spend by tenant, schedule, sub-agent, model.
- **Quality:** success rate, intervention rate, max-round stops, reviewer rejection rate.

## Incident Response

1. Disable scheduler worker or tenant schedules if autonomous runs misbehave.
2. Cancel active bad sessions.
3. Inspect session trace, A2A messages, cost ledger, and LLM config.
4. Patch definition/schedule/model binding.
5. Re-run manually before re-enabling automation.

## Health Checks

- LLM provider test connection for active orchestrator config.
- Schedule worker can query due schedules.
- A2A mailbox can send/claim/complete test message in non-prod.
- Cost guard reservation path succeeds/fails predictably.

## Acceptance Criteria

- Every scheduled run is observable from schedule → session → A2A messages → trace → cost → terminal status.
- Alerts exist for cost cap, stuck messages, repeated overlaps, and LLM unavailable.
- Logs mask secrets and PII.

## Traceability Matrix

| Requirement | Monitoring Signal |
|---|---|
| SK coordinator | planning latency, max rounds, replan count |
| Sub-agents as data | usage/errors by agent definition |
| A2A collaboration | message lifecycle dashboard |
| Schedules | due/run/skipped/failed counters |
| LLM seed | config resolution/test status |
| Guardrails | RBAC/cost/cancel/approval events |

## Review Notes

- Prefer dashboard from existing DB tables first; avoid adding telemetry infra before signal proves useful.
