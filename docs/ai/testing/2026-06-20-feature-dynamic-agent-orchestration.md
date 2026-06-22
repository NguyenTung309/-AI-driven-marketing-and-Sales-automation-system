---
phase: testing
title: Dynamic Agent Orchestration — Testing Strategy
description: Define testing approach, test cases, and quality assurance
feature: dynamic-agent-orchestration
date: 2026-06-20
last_reviewed: 2026-06-21
---

# Dynamic Agent Orchestration — Testing Strategy

## Test Coverage Goals
**What level of testing do we aim for?**

- Unit test coverage target: 100% of new planner, DAG executor, plan validation/redaction, re-plan, cost guard, state-machine, and adapter branches.
- Integration test scope: gRPC lifecycle (`Submit/GetPlan/UpdatePlan/Approve/Control/Trace`), API route permissions/tenancy, persisted `AgentSession`/`AgentTrace`, re-plan persistence, and cost-cap behavior.
- End-to-end test scenarios: user submits a natural-language goal, receives an editable DAG, runs/pauses/resumes/cancels, sees trace, and cost guard blocks runaway execution.
- Alignment with [requirements](../requirements/2026-06-20-feature-dynamic-agent-orchestration.md) and [design](../design/2026-06-20-feature-dynamic-agent-orchestration.md): SK planner, editable plan, auto-run with approval toggle, parallel DAG, bounded re-plan, PII redaction, RBAC, tenancy, and atomic cost-surprise guard.

## Unit Tests
**What individual components need testing?**

### Plan schema and validation
- [x] Valid DAG accepted; empty plan rejected — [OrchestrationPlanValidatorTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/OrchestrationPlanValidatorTests.cs).
- [x] Unknown agent rejected using catalog code/shortName/agentType aliases.
- [x] Duplicate task id, dangling dependency, cycle, too many tasks, and oversized input rejected.
- [ ] Plan output/error normalization on user-edited plans.
- [ ] JSON round-trip compatibility for `version`, `input`, `dependsOn`, `status`, `output`, and `error`.

### Planner and SK adapter
- [x] `SemanticKernelPlanGenerator` normalizes fenced JSON, validates generated plan, and rejects invalid planner output — [SemanticKernelPlanGeneratorTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/SemanticKernelPlanGeneratorTests.cs).
- [x] `ClawbotChatCompletionService` maps SK `ChatHistory` to `IClaudeChatClient` and records cost — [ClawbotChatCompletionServiceTests.cs](../../tests/Clawbot.Agents.Tests/Chat/ClawbotChatCompletionServiceTests.cs).
- [x] `SemanticKernelOrchestrator` redacts goal/task content, snapshots approval mode, and blocks auto-run on pre-flight estimate — [SemanticKernelOrchestratorTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/SemanticKernelOrchestratorTests.cs).
- [ ] Planner cost ledger entry under agentCode `orchestrator` through real `ILlmCallScope` integration.

### DAG executor
- [x] Dependencies run before dependents — [ParallelDagExecutorTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/ParallelDagExecutorTests.cs).
- [x] Independent tasks obey concurrency cap.
- [x] Failed task marks dependents skipped.
- [x] EF-backed agents serialize through `SerializingAgent`.
- [x] Re-plan attempts are bounded; `cost_cap_midrun` does not trigger re-plan — [ParallelDagExecutorReplanTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/ParallelDagExecutorReplanTests.cs).
- [ ] Cancellation token stops in-flight/runnable work and leaves persisted state consistent.
- [ ] Progress callbacks persist failed/skipped/cancelled phases exactly once.

### Re-plan logic
- [x] Completed tasks are preserved during merge — [OrchestrationReplanTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/OrchestrationReplanTests.cs).
- [x] Regenerated tasks receive re-plan attempt prefix and reset to pending.
- [x] Failure context included in re-plan goal.
- [ ] Service-level re-plan persists `ReplanCount`, `re-planned` trace, and patched `PlanJson`.

### Cost-surprise guard
- [x] Pre-flight estimate over cap returns `cost_cap_preflight` — [OrchestratorCostGuardTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/OrchestratorCostGuardTests.cs).
- [x] Mid-run reserve allows/denies based on remaining cap.
- [x] Release/adjust reservations work.
- [x] Concurrent reservations serialize per tenant/month.
- [ ] Full orchestration race: N independent runnable tasks near cap start together; only allowed subset runs and session fails safely.

### State machine and tenancy models
- [x] `AgentSession` transitions: approve, pause, resume, cancel, edit lock after run — [AgentSessionOrchestrationTests.cs](../../tests/Clawbot.Domain.Tests/Agents/AgentSessionOrchestrationTests.cs).
- [x] `Tenant.RequireOrchestrationApproval` toggles and snapshots — [TenantOrchestrationTests.cs](../../tests/Clawbot.Domain.Tests/Tenants/TenantOrchestrationTests.cs).
- [ ] `RowVersion`/optimistic concurrency at persistence level for competing plan edits/task completions.

### Agent adapters and catalog
- [x] Catalog alias map resolves code/shortName/agentType to adapters — [OrchestrationAgentsTests.cs](../../tests/Clawbot.Agents.Tests/Orchestrator/OrchestrationAgentsTests.cs).
- [x] Lead/report orchestration adapters map `AgentTask.Input` into runner calls — [OrchestrationAgentAdaptersTests.cs](../../tests/Clawbot.AgentService.Tests/Services/OrchestrationAgentAdaptersTests.cs).
- [ ] Chat, content, research, docs, ads, and sale-assist adapter input contract edge cases.

## Integration Tests
**How do we test component interactions?**

- [x] gRPC lifecycle happy paths — [OrchestratorGrpcServiceTests.cs](../../tests/Clawbot.AgentService.Tests/Services/OrchestratorGrpcServiceTests.cs): submit, pending approval, approve, pause/resume, cancel paused session, not found, stale/missing ETag.
- [x] Trace basics: planned/started/completed persisted and streamed; no duplicate terminal completed rows.
- [x] PII redaction: goal, task output, and edited plan descriptions/inputs.
- [x] Mid-run cost cap: session fails and task carries `cost_cap_midrun`.
- [ ] Pre-flight cost cap: submit should remain pending/blocked and no adapter should start.
- [ ] Re-plan persistence: first task fails, replanner patches plan, `ReplanCount` increments, traces include `failed` + `re-planned`, final state matches policy.
- [ ] Background/offloaded execution path: `IServiceScopeFactory` branch in `OrchestratorGrpcService.StartExecutionAsync` succeeds and failure marks session failed.
- [ ] API permission/tenancy tests for `/api/orchestration/*`: missing permission → 403, allowed roles succeed, tenant A cannot read/update/approve/pause/resume/cancel/trace tenant B.
- [ ] Trace unhappy paths: `failed`, `skipped`, `re-planned`, `cancelled`, pause/resume phases are persisted/streamed exactly once.

## End-to-End Tests
**What user flows need validation?**

- [ ] Operator submits goal with auto-run tenant → plan runs to completion, task statuses update, trace visible.
- [ ] Tenant requires approval → submit stops at `pending_approval`; user edits JSON plan, saves with ETag, approves, and execution starts.
- [ ] User pauses/resumes running plan; pending tasks continue after resume.
- [ ] User cancels running plan; session ends `cancelled` and no new tasks start.
- [ ] Cost cap blocks auto-run and surfaces clear warning.
- [ ] Browser/UI smoke in `OrchestrationPanel`: submit, refresh, edit plan, approve, pause/resume/cancel, load trace.

## Test Data
**What data do we use for testing?**

- Minimal `OrchestrationPlanDocument` fixtures:
  - one-task plan for happy path.
  - two-task dependent plan for ordering/pause/resume.
  - independent three-task plan for concurrency.
  - failing plan for re-plan/skipped paths.
  - PII-containing goal/plan for redaction.
- `FixedChatClient` and `ClawbotChatCompletionService` for deterministic planner JSON.
- `FakeCatalog` with `content-agent` and aliases; add rows for all 8 agents when testing adapter matrix.
- `AgentServiceTestAppDb` SQLite fixture for service persistence.
- `SequencedTracker`/`FixedTracker` for cost-cap paths.
- `PausingContentAdapter`, `FailingAgent`, `DelayedAgent`, and `RecordingAgent` stubs for executor behavior.

## Test Reporting & Coverage
**How do we verify and communicate test results?**

- Targeted command used 2026-06-21:
  - `dotnet test "tests/Clawbot.AgentService.Tests/Clawbot.AgentService.Tests.csproj" --no-restore --filter OrchestratorGrpcServiceTests --logger "console;verbosity=minimal"` — 21 passed.
- Coverage command:
  - `dotnet test tests --collect:"XPlat Code Coverage"`
- Recommended report step:
  - merge coverage outputs with `reportgenerator` and publish line/branch coverage for `Clawbot.Agents.Core.Orchestrator`, `Clawbot.AgentService`, and `Clawbot.Api`.
- Coverage gaps blocking 100%: background scope-factory branch, service-level re-plan persistence, pre-flight cost blocking at gRPC/API layer, parallel cost race at orchestration level, API RBAC/tenancy, unhappy-path traces, frontend tests.

## Manual Testing
**What requires human validation?**

- Orchestration panel UX: plan JSON is understandable and editable; ETag conflict message is clear.
- Trace readability for non-developers.
- Pause/cancel behavior under a long-running live task.
- Cost-block warning copy and tenant approval toggle behavior.
- Accessibility for plan editor buttons and status pills.

## Performance Testing
**How do we validate performance?**

- Planner p95 target: <5s for one LLM call with catalog prompt.
- DAG wall-clock: independent tasks should finish near critical path, not sum of durations, while cap=3 is respected.
- Cost guard contention: concurrent reservations should serialize without deadlock or high tail latency.
- Trace write volume: many small tasks should not cause excessive EF save contention.

## Bug Tracking
**How do we manage issues?**

- CRITICAL: cross-tenant plan access, PII persisted unredacted, parallel cost cap exceeded, lost task status from concurrent writes.
- HIGH: re-plan loop/cost runaway, illegal state transition succeeds, background execution failure not persisted.
- MEDIUM: missing trace phase, flaky long-running task cancellation, missing API RBAC coverage.
- LOW: UI refresh/polling polish, trace ordering display, manual-only browser coverage.

## Deferred Follow-ups

1. Add pre-flight cost-block integration test proving no adapter starts.
2. Add parallel near-cap orchestration race test.
3. Add service-level re-plan persistence/trace test.
4. Add background `_scopeFactory` execution branch tests.
5. Add `/api/orchestration/*` RBAC + cross-tenant endpoint tests.
6. Add frontend unit/E2E baseline for `OrchestrationPanel`.
