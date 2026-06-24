---
phase: testing
title: Dynamic Agent Orchestration v2 — Testing Strategy
description: Test plan for autonomous scheduled A2A orchestration
feature: dynamic-agent-orchestration-v2
date: 2026-06-24
status: draft
---

# Dynamic Agent Orchestration v2 — Testing Strategy

## Context Quality Assessment

- New behavior spans domain, EF persistence, background worker, API, and frontend.
- Highest-risk paths: recurrence/idempotency, cost cap, tenant isolation, encrypted seed, cancellation.
- Tests should favor small domain/service tests plus a few integration tests over brittle UI-only coverage.

## Executive Summary

V2 requires unit tests for schedule calculation and entity transitions, integration tests for persistence/API/RBAC, and one local smoke path for encrypted LLM seed plus a scheduled A2A run.

## Test Coverage Goals

- 80%+ coverage on new orchestration domain/service logic.
- 100% branch coverage for recurrence calculation, overlap policy, A2A state transitions, and key seed validation.
- Integration coverage for API auth, tenant isolation, DB idempotency, and cost cap.

## Unit Tests

### RecurrenceCalculator

- [x] Daily schedule computes next tenant-local day and stores UTC (`RecurrenceCalculatorTests`).
- [x] Weekly schedule has stable ISO week window keys (`RecurrenceCalculatorTests`).
- [x] Monthly schedule handles shorter months by clamping to last valid day (`RecurrenceCalculatorTests`).
- [x] Quarterly schedule advances by three months and preserves local time where valid (`RecurrenceCalculatorTests`).
- [ ] DST transition does not produce duplicate window keys.

### AgentSchedule

- [x] `skip` overlap policy marks run `skipped_overlap` when previous run active (`AgentScheduleRunnerTests`).
- [ ] inactive schedule does not fire.
- [x] manual `run-now` uses unique manual window key (`AgentScheduleRunner.RunNowAsync`; API exposure deferred to Phase 5).

### A2AMailbox

- [ ] send creates pending message with tenant/session IDs.
- [ ] claim moves one pending message to processing.
- [ ] complete stores safe result payload.
- [ ] fail stores safe error without throwing away original trace correlation.

### AutonomousOrchestrator

- [ ] creates sub-agent definitions or resolves existing definitions.
- [ ] stops at max rounds.
- [ ] stops when cost cap denied.
- [ ] stops on cancellation before claiming new message.
- [ ] writes trace for delegate/result/critique/finalize.

### DemoLlmConfigSeeder

- [ ] missing key skips or fails with safe error depending mode.
- [ ] provided key is encrypted before DB persistence.
- [ ] dry-run never prints key.
- [ ] provider/model/baseUrl are set to `openai-compatible`, `cx/gpt-5.5`, `http://localhost:20128/v1`.

## Integration Tests

- [ ] API rejects schedule creation without `orchestration:manage`.
- [ ] API rejects run creation without `orchestration:run`.
- [ ] Tenant A cannot read Tenant B schedules, messages, or runs.
- [x] Duplicate schedule window insert is prevented by DB uniqueness and runner existing-window check (`AgentScheduleRunnerTests`).
- [ ] Cost cap stops multi-agent run and records terminal failed state.
- [ ] Cancel endpoint stops future A2A message claims.

## End-to-End Tests

- [ ] Local demo seed configures encrypted LLM provider.
- [ ] Create daily schedule, run now, observe new session + A2A timeline + trace.
- [ ] Pause/cancel scheduled run from UI or API.
- [ ] Confirm demo document wording matches observed capability.

## Test Data

- Tenant: `demo`.
- Local model: `cx/gpt-5.5`.
- Base URL: `http://localhost:20128/v1`.
- API key: injected through env/CLI only; test assertions must never include plaintext.
- Seed sub-agents: daily lead triage, weekly content planner, monthly performance analyst, quarterly strategy reviewer, reviewer, reporter.

## Test Reporting & Coverage

- Run .NET unit/integration tests with existing solution test command.
- Run frontend tests only if UI changes land.
- Record smoke result in PR/test plan: seed status, schedule run ID, session ID, trace status.

## Manual Testing

- Verify run trace makes A2A easy to explain in demo.
- Verify no raw API key appears in UI, logs, SQL output, docs, or terminal dry-run.
- Verify schedule timezone label is clear.

## Performance Testing

- Run 10 due schedules for one tenant and ensure no duplicate window rows.
- Run one multi-agent session with concurrency cap and verify wall-clock improves over sequential while staying under cap.

## Bug Tracking

- Critical: secret leak, tenant leak, runaway loop, duplicate schedule execution.
- High: cost cap bypass, cancel ignored, A2A message stuck processing.
- Medium: UI trace confusion, missed schedule without trace.

## Acceptance Criteria

- Required test cases pass before implementation review.
- Smoke path proves local LLM seed + scheduled A2A run.

## Traceability Matrix

| Requirement | Tests |
|---|---|
| SK coordinator | AutonomousOrchestrator unit/integration |
| Input sources | API run creation tests |
| Sub-agents as data | catalog/sub-agent tests |
| A2A collaboration | mailbox + trace tests |
| Schedules | recurrence/idempotency tests |
| LLM seed | seed encryption tests |
| Guardrails | RBAC/cost/cancel/tenant tests |

## Review Notes

- Use fake/in-memory LLM for most automated tests.
- Only manual smoke should hit local model endpoint.
