# Dynamic Agent Orchestration — Progress Ledger

Plan: docs/ai/planning/2026-06-20-feature-dynamic-agent-orchestration.md
Branch: llm-provider-config
Status: ALL TASKS COMPLETE including Phase 7 alignment follow-up (2026-06-21)

## Done (verified)
- T0.1 RFC + SK 1.77.0 CPM + Agents.Core reference.
- T1.1 AgentSession state model (RequiresApproval/ReplanCount/RowVersion + transitions + UpdatePlan/RecordRun).
- T1.2 Tenant.RequireOrchestrationApproval toggle + snapshot.
- T1.3 DDL migrations 0028/0029/0030 + EF config (.IsRowVersion()).
- T1.4 RBAC orchestration:view/run/approve/manage + orchestrator AgentConfig seed.
- T2.1 ClawbotChatCompletionService (SK IChatCompletionService over ScopedLlmChatClient).
- T2.2 IAgentCatalog + DbAgentCatalog.
- T2.3 OrchestrationPlanDocument + OrchestrationPlanValidator (DAG checks).
- T2.4 SemanticKernelPlanGenerator + SemanticKernelOrchestrator (generate→redact→autonomy→cost-preflight→CreatePlan).
- T3.1 ParallelDagExecutor (waves, cap=3, dep ordering, failure→skip, replan hook, per-task progress callback).
- T3.2 all 8 adapters live: 6 core adapters + AgentService LeadOrchestrationAdapter/ReportOrchestrationAdapter via LeadAgentRunner/ReportAgentRunner.
- T3.3 OrchestratorCostGuard (CanStartAsync + TryReserveAsync + ReleaseReservationAsync + AdjustReservationAsync), runtime task guard wired.
- T3.4 OrchestrationReplan.Merge/BuildReplanGoal + executor bounded replan (cap=2) wired in service.
- T4.1 orchestrator.proto extended (Submit/GetPlan/UpdatePlan/Approve/Control + PlannedTask status/output/error).
- T4.2 OrchestratorGrpcService lifecycle + inline execution + error mapping + DI + per-task persist/trace + pause/cancel checks + PII-redacted output/error + mid-run cost cap.
- T4.3 API OrchestratorClient + OrchestrationEndpoints + per-route RequirePermission + MapOrchestration.
- T5.1 shared/api/orchestration.ts typed client.
- T5.2 OrchestrationPanel mounted in AgentDashboardPage (perm-gated).
- T6.1 SQLite end-to-end gRPC tests + executor/cost/PII unit tests.
- T6.2 build 0/0; full code-level test suite green; FE tsc clean.

## Former limitations now closed
- lead + report adapters: extracted shared runners, real orchestration adapters added.
- Per-task DB persistence: executor progress callback persists PlanJson and AgentTrace after each task.
- PII in task output/error: RuntimeGuardedAgent redacts output/error before PlanJson persist/response.
- PII in plan inputs/edited plans: OrchestrationPlanRedactor redacts task descriptions and input values before PlanJson persist.
- Mid-run cost guard: RuntimeGuardedAgent calls TryReserveAsync per task; Release/Adjust implemented.
- Pause/resume between tasks: paused runs keep pending tasks and resume continues the persisted DAG.
- ETag concurrency: lifecycle responses include etag; update/approve/control pass expected_etag and stale writes return etag_mismatch.
- Parallel EF safety: lead/report EF-backed adapters are serialized per run so shared scoped DbContext is not used concurrently.

## Remaining Phase 7 alignment work
- T7.1 Return full editable DAG payload through proto/API.
- T7.2 Stream persisted dynamic traces from `Trace` RPC.
- T7.3 Persist all required trace phases (`planned`, `started`, `completed`, `failed`, `re-planned`, `skipped`).
- T7.4 Guard cancel state transitions.
- T7.5 Complete frontend review/edit/trace flow.
- T7.6 Make catalog metadata data-resolved.
- T7.7 Fix human-override RBAC semantics.
- T7.8 Record planner/replan LLM cost to ledger.
- T7.9 Refresh cost guard month-to-date spend per reservation.
- T7.10 Feed real catalog descriptions/input schemas into planner prompt.
- T7.11 Hard-stop on mid-run cost cap with no replanner call.
- T7.12 Record actual orchestrated agent LLM cost.

## Remaining known operational caveat
- Execution remains inline/synchronous inside the RPC. Pause/cancel is observed between tasks, not inside a currently running task. Running tasks still receive gRPC CancellationToken for transport cancellation.
- Docker-backed Clawbot.Integration.Tests need a Docker/Testcontainers daemon.

## Verification (final)
- dotnet build Clawbot.sln → 18 projects, 0 errors, 0 warnings.
- Tests: Domain 59, Application 3, Agents 207, AgentService 31, Api 104, Infrastructure 124 — all pass.
- Alignment retest 2026-06-21: Clawbot.Agents.Tests orchestrator validator/planner filters 11/11 pass; Clawbot.AgentService.Tests orchestration adapter/service filters 15/15 pass.
- Legacy planner bugfix retest 2026-06-21: PlanningOrchestratorTests 4/4 pass; combined orchestrator core filter 15/15 pass; rebuild 0 warnings/errors.
- Reviewer follow-up 2026-06-21: `leadership` substring regression covered; PlanningOrchestratorTests 5/5 pass.
- Frontend: tsc -b → no errors.
- Phase 7 verification 2026-06-21: AgentService OrchestratorGrpcServiceTests 21/21 pass; Agents targeted ClawbotChatCompletionService/SemanticKernelPlanGenerator/OrchestratorCostGuard/ParallelDagExecutorReplan 18/18 pass; Infrastructure DbAgentCatalog/DevDataSeeder 6/6 pass; frontend build/tsc pass; dotnet build Clawbot.sln 0 warnings/errors.
