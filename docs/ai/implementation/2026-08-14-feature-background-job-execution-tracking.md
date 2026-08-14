---
phase: implementation
title: Background Job Execution Tracking Implementation Guide
description: Implementation guardrails for tracked recurring Hangfire execution lifecycle
feature: background-job-execution-tracking
status: approved
created: 2026-08-14
---

# Background Job Execution Tracking Implementation Guide

## Development Setup

- Work in the existing API, Infrastructure, AgentService and `clawbot-web` projects; do not introduce a second scheduler or a second generic job center.
- Read the approved requirements, design and plan before each phase. The critical semantic rule is: **enqueue acknowledgement is never execution success**.
- Inspect the active migration ledger before creating SQL. The deployment runner applies one file in a transaction and does not accept `GO`.
- Use the established `IPiiRedactor`, `IClock`, EF configuration, rate-limiting, authorization and immutable TypeScript API-client patterns.

## Code Structure

| Concern | Location / pattern |
|---|---|
| Execution aggregate and attempts | New cohesive files under `src/shared/Clawbot.Domain/Jobs/` with private setters and invariant transition methods. |
| Persistence and orchestration | `src/shared/Clawbot.Infrastructure/Jobs/`: execution service, registry, dispatcher, executor adapters and final-state filter. |
| Database model | `AppDbContext` plus explicit entity configurations; additive migration in `deploy/migrations`. |
| Admin boundary | `AdminJobsEndpoints.cs`: typed DTOs, permission checks, manual enqueue/read/retry endpoints. |
| Agent schedules | Existing `AgentScheduleRunner` and gRPC service/contracts; preserve the runner’s locking and overlap behavior. |
| Admin UI | `shared/api/admin.ts` and `features/admin/AdminJobsTab.tsx`; keep generic Job Center separate. |

Prefer focused interfaces:

- `IRecurringJobExecutor`: runs exactly one allowlisted definition.
- `RecurringJobExecutionContext`: exposes only execution identity, cancellation, safe progress and safe result reporting.
- `RecurringJobExecutionService`: owns state transitions and query projections.
- `RecurringJobDefinitionRegistry`: maps stable definition ID to registration/executor metadata.

## Implementation Notes

### Persistence and transitions

- Implement `RecurringJobExecution` and `RecurringJobExecutionAttempt` as separate aggregates/entities. Attempt rows are append-only after terminalization.
- Persist manual execution as `requested` before calling Hangfire. Persist `queued` only after enqueue returns a Hangfire background-job ID.
- Require a client-generated UUID `Idempotency-Key` header. Return/reuse the same execution only for a duplicate equivalent transport request; reject incompatible reuse.
- For scheduled execution, correlate first perform by `(DefinitionId, PerformContext.BackgroundJob.Id)`. Persist that ID in `HangfireBackgroundJobId`; all Hangfire retries reuse it and therefore resolve the same logical execution.
- Persist `RetryCount` and immutable `AttemptNumber = RetryCount + 1`; attempts may share the same Hangfire background-job ID and must be unique only by `(ExecutionId, AttemptNumber)`.
- Treat a stale `requested` record honestly; reconciliation must move it through an explicit recovery or `enqueue_failed` path, never infer `succeeded`.
- Make dispatch transitions idempotent. A duplicate perform may correlate to the existing active execution/attempt but must not re-run a completed business execution.
- Allow only `failed`, `cancelled` or other explicitly approved terminal states for manual retry. Create a new row with `RetryOfExecutionId`; never reset the original.

### Dispatcher and Hangfire behavior

- Convert registrations one definition at a time from direct concrete-method targets to definition-specific `RecurringJobDispatcher` wrapper targets.
- Preserve definition ID, cron, queue, timezone, `AutomaticRetry`, locking/concurrency behavior and any custom filter already attached to the workload. Direct target method/type attributes do not flow to a generic dispatcher: wrappers must carry equivalent `[AutomaticRetry]`, `[DisableConcurrentExecution]` and custom filters, or an explicit verified filter provider must apply them.
- Registry is an allowlist. Do not take a job type, method name, queue or arbitrary Hangfire expression from the HTTP request.
- Dispatcher receives `PerformContext`, resolves scheduled execution by `perform.BackgroundJob.Id`, reads `RetryCount`, creates the retry-slot attempt, and marks logical execution/attempt running together as far as the local transaction permits.
- On **every** exception: redact/bound error, close the attempt as failed, set parent `retrying`, then rethrow unchanged. Never use `RetryCount` to decide whether the exception is final.
- `TrackedRecurringExecutionFinalStateFilter : IApplyStateFilter` handles only actually applied `FailedState` for dispatcher wrappers, which happens after `AutomaticRetry` has exhausted retries. It conditionally transitions active execution found by `context.BackgroundJob.Id` to failed using the latest persisted redacted attempt error, then sends the one safe failure notification only if that transition succeeded. It logs persistence failure without throwing into Hangfire state processing.
- Explicitly exclude dispatcher wrapper jobs from legacy failure notification behavior; never use raw `Exception.Message` or `FailedState.Exception` to construct an admin/API/notification string.

### Safe progress and result data

- `ReportProgressAsync` clamps percent and limits/redacts notes before persistence/realtime publication. Do not encode arbitrary structured output as `ResultSummary`.
- Executors return a typed safe result: optional validated first-party link, bounded redacted summary and explicitly approved operational counters only.
- If the executor cannot report meaningful safe progress, leave progress null and render “Chưa báo cáo tiến độ”. Do not manufacture 100% before success.
- Validate result links as relative first-party paths; no external/protocol-relative URLs, javascript URLs or user-controlled redirects.

### Admin endpoint behavior

- Trigger response returns the logical execution tracking ID and its `/api/admin/jobs/executions/{id}` URL. `202` represents accepted/queued only.
- Explicitly map: unknown definition `404`; unavailable manual dispatch or incompatible idempotency `409`; unconfirmed enqueue to a safe error after persisting `enqueue_failed`.
- Enforce `system:config` before querying any execution. Use bounded filters/cursors and project safe DTOs—never return raw EF entities or payloads.
- Agent schedule Run Now calls the existing gRPC manual-run operation and propagates real `AgentScheduleRun.Id` plus optional session ID. Do not restore the old `NextRunAt = now` shortcut.
- Add tenant-constrained `GET /api/admin/jobs/schedule-runs/{runId}` as the primary detail surface. It must serve trend scans with null `SessionId`; offer `/agents/runs/{sessionId}` only as a secondary link for runs that have a session, and `/content` as the secondary trend-result link.

### Frontend implementation

- Keep API objects `readonly` and use immutable React Query cache updates/invalidation.
- Mutation success should store the returned tracking ID and open/focus execution detail. The initial message is “Đã xếp hàng”; it may not use a success badge.
- Poll only the selected active detail around every three seconds and stop automatically on a terminal status. Overview refresh remains independent.
- Add focus restoration, Escape close, accessible status name and `aria-live` updates; status must be conveyed with text, not color alone.
- Show retries as an ordered attempt timeline; safely distinguish the current logical status from Hangfire’s raw last-state diagnostic.

## Error Handling

- Fail closed on invalid definition, disallowed retry, missing execution and unauthorized reads/actions.
- Redact/bound all stored/displayed errors; retain raw exceptions only in protected server logs under existing logging policy.
- A notification publish failure cannot revert the already persisted execution terminal outcome; log correlation IDs and alert separately.
- Do not swallow tracking persistence exceptions. If Hangfire should retry work, rethrow after recording the safe attempt state where possible.
- Report stale or reconciliation-required execution states in operations metrics rather than presenting a fabricated terminal result.

## Performance Considerations

- Use cursor pagination and index `(DefinitionId, RequestedAt DESC)`; never load all history with the overview.
- Limit detail page size, error/output lengths and progress update frequency.
- Avoid a query per recurring definition to find latest status; use a set-based/latest projection backed by indexes if overview includes this data.
- Keep realtime payloads small and only emit updates after a state/progress change that can be observed safely.

## Security Notes

- Require `system:config` on all system execution endpoints and validate ID/cursor/page-size/status at the API boundary.
- Never add global execution records to `ITenantOwned` tables or tenant Job Center queries.
- Audit manual actions using request identity and tracking ID, without storing raw result/payload text.
- Test that exceptions, stack traces, SQL, paths, token-like strings, Hangfire arguments and tenant payloads cannot surface through execution DTOs, UI or notifications.
- Do not add secrets/configuration. Use existing dependency injection and redaction services.
