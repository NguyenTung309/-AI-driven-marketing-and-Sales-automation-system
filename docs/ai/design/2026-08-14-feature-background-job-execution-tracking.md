---
phase: design
title: Background Job Execution Tracking Design
description: Dedicated tracking lifecycle for Hangfire recurring job executions and attempt history
feature: background-job-execution-tracking
status: approved
created: 2026-08-14
---

# Background Job Execution Tracking Design

## Architecture Overview

Use a dedicated, admin-only execution model for recurring Hangfire workloads. It is deliberately separate from tenant/user `BackgroundJob`: generic jobs retry by reusing one row, whereas recurring operations need a durable logical execution and immutable physical attempt history.

```mermaid
sequenceDiagram
  participant Admin as Admin Jobs Tab
  participant API as Admin Jobs API
  participant DB as App DB
  participant HF as Hangfire
  participant D as RecurringJobDispatcher
  participant E as Definition Executor
  participant N as Notification/Realtime

  Admin->>API: POST recurring/{definitionId}/trigger
  API->>DB: Create manual execution (requested)
  API->>HF: Enqueue dispatcher(executionId)
  HF-->>API: hangfireJobId
  API->>DB: Attach ID; mark queued
  API-->>Admin: 202 trackingId + statusUrl
  HF->>D: Run execution + PerformContext.BackgroundJob.Id
  D->>DB: Create-or-get execution by Hangfire ID; create attempt
  D->>E: Execute with tracking context
  E->>D: progress / safe result
  D->>DB: Persist safe progress / completion
  D->>N: Admin realtime update
  alt exception on any attempt
    D->>DB: Mark attempt failed; execution retrying
    D-->>HF: rethrow unchanged
    HF->>D: Retry with same BackgroundJob.Id
  else final FailedState applied after retry exhaustion
    HF->>D: IApplyStateFilter finalizer
    D->>DB: Mark execution failed exactly once
    D->>N: Publish one safe warning
  end
```

### Components and responsibilities

| Component | Responsibility |
|---|---|
| `RecurringJobDefinitionRegistry` | Allowlisted metadata/executor lookup: definition ID, queue, cron/timezone, description, agent label, manual-run policy, retry/concurrency requirements. Replaces duplicated `JobMeta` as the source of application metadata. |
| `RecurringJobDispatcher` | The Hangfire target for converted recurring registrations. Correlates execution, creates attempts, drives lifecycle, invokes executor, preserves Hangfire retry semantics. |
| `IRecurringJobExecutor` | Definition-specific adapter around existing concrete workloads. Exposes only safe result/progress through a tracking context. |
| `RecurringJobExecutionService` | Transactional persistence, transition validation/idempotency, enqueue correlation, history/detail queries and stale-request reconciliation. |
| `TrackedRecurringJobStateFilter` | Final failure/cancellation handling only. It terminalizes the logical execution once Hangfire applies final state; it is not the primary source for correlation/output/progress. |
| `AdminJobsEndpoints` | `system:config` boundary; manual enqueue, detail/history/retry endpoints, and Agent schedule manual-run proxy. |
| `AdminJobsTab` | Trigger acknowledgement, execution detail/timeline, active polling, safe terminal output/error, retry mutation. |

## Data Models

### `RecurringJobExecution`

One row per logical invocation. It does **not** implement `ITenantOwned`, because a registered recurring definition may process many tenants or be global.

| Field | Type / constraint | Purpose |
|---|---|---|
| `Id` | `Guid`, PK | Public tracking ID. |
| `DefinitionId` | bounded string, indexed | Stable registry/Hangfire definition key. |
| `Source` | enum/string: `scheduled`, `manual`, `manual_retry` | How the logical run originated. |
| `Status` | state vocabulary in requirements | Logical outcome; terminal transition is one-way. |
| `RequestedByUserId` | nullable `Guid` | Audit identity for manual actions. |
| `RequestedTenantId` | nullable `Guid` | Request audit context only; never access scope. |
| `RetryOfExecutionId` | nullable FK | Parent of explicit manual retry. |
| `RequestKey` | nullable bounded unique key | Idempotency key for manual request duplicate delivery. |
| `HangfireBackgroundJobId` | nullable bounded string | Stable correlation ID for the Hangfire background job. A scheduled job and all automatic retries reuse it; a manual execution stores the ID returned by `IBackgroundJobClient.Enqueue`. |
| `ProgressPercent` | nullable integer 0–100 | Progress only when executor reports it. |
| `ProgressNote` | nullable bounded/redacted string | Safe current phase summary. |
| `ResultSummary` | nullable bounded/redacted string | Safe completed output. |
| `ResultLink` | nullable validated relative link | Safe first-party detail link only. |
| `Error` | nullable bounded/redacted string | Final safe diagnostic. |
| `RequestedAt`, `EnqueuedAt`, `StartedAt`, `FinishedAt` | `DateTimeOffset` / nullable | Lifecycle timestamps. |
| audit fields | project standard | Entity audit metadata. |

### `RecurringJobExecutionAttempt`

One immutable retry-slot row per physical `PerformContext` execution. Automatic retries reuse `PerformContext.BackgroundJob.Id`, so every attempt under one logical execution may intentionally carry the same `HangfireBackgroundJobId`; they differ by persisted `RetryCount` and `AttemptNumber = RetryCount + 1`. Rows are never overwritten.

| Field | Type / constraint | Purpose |
|---|---|---|
| `Id` | `Guid`, PK | Attempt identity. |
| `ExecutionId` | FK, indexed | Logical parent. |
| `HangfireBackgroundJobId` | bounded string, indexed, non-unique across attempts | Stable Hangfire correlation copied from parent execution; retries intentionally reuse it. |
| `RetryCount` | non-negative int | Value read from Hangfire at this perform. |
| `AttemptNumber` | `RetryCount + 1`; unique with `ExecutionId` | Immutable ordinal. |
| `Status` | `queued`, `running`, `succeeded`, `failed`, `cancelled` | Attempt state. |
| `QueuedAt`, `StartedAt`, `FinishedAt` | timestamps | Attempt timing. |
| `Error` | nullable bounded/redacted string | Safe failed-attempt reason. |
| `WorkerId` | optional bounded safe diagnostic | Only if confirmed non-sensitive and operationally useful. |

### Persistence rules and indexes

- Use explicit EF configuration classes and add `DbSet`s to `AppDbContext`.
- Use project-compatible SQL Server migration plus SQLite test-provider shims where the existing model requires them.
- Required indexes:
  - `(definition_id, requested_at DESC)` for per-definition history;
  - unique `(definition_id, hangfire_background_job_id)` when the Hangfire ID is present, so scheduled first perform and automatic retries resolve one logical execution;
  - `(status, requested_at DESC)` for active/stale execution reconciliation;
  - `(retry_of_execution_id)` for linked manual retries;
  - unique `(execution_id, attempt_number)`;
  - non-unique attempt `(hangfire_background_job_id)` correlation index because every retry attempt intentionally carries the same ID;
  - appropriately scoped uniqueness for manual `request_key`. 
- Every displayed/persisted error, progress note and result summary passes `IPiiRedactor` and per-field length bounding. Do not store exception objects or raw payload JSON.
- State transition methods live on domain entities and reject terminal rewrites. Persistence transitions use conditional/concurrency-safe updates so duplicate dispatcher delivery is harmless.

## Execution Lifecycle

### Scheduled recurrence

1. `HangfireModule` registers a definition-specific dispatcher wrapper instead of a workload’s direct method. The wrapper must retain the source target’s effective queue, cron, timezone, `AutomaticRetry`, `DisableConcurrentExecution` and custom-filter behavior; a plain generic dispatcher method would lose method/type attributes.
2. Dispatcher receives `PerformContext`; it calls `CreateOrGetScheduled(definitionId, perform.BackgroundJob.Id)`. The unique `(DefinitionId, HangfireBackgroundJobId)` lookup creates one scheduled execution on its first perform and reuses it for all automatic retries because Hangfire retains the background-job ID.
3. It reads `RetryCount`, creates the immutable attempt with `AttemptNumber = RetryCount + 1`, marks attempt/execution `running`, invokes the definition executor with `RecurringJobExecutionContext`, and persists reported progress.
4. On success it terminalizes the attempt/execution as `succeeded` with a safe result.
5. On **every** exception it records a failed attempt, marks execution `retrying`, and rethrows unchanged. Dispatcher never uses `RetryCount` to guess finality; Hangfire owns automatic retry policy.
6. `TrackedRecurringExecutionFinalStateFilter : IApplyStateFilter` observes only an actually applied `FailedState` for the dispatcher after `AutomaticRetry` exhaustion. It looks up by `context.BackgroundJob.Id`, conditionally terminalizes an active execution with the already-redacted latest-attempt error, then publishes one safe warning only if that conditional transition succeeds. Filter persistence failures are logged/reconciled and never alter Hangfire state behavior.

### Manual trigger

1. API validates `definitionId` against registry and `system:config` authorization, then accepts a client-generated UUID `Idempotency-Key` header for transport retries of that one user action.
2. `RecurringJobExecutionService` creates/reuses an idempotent `manual` execution in `requested` state.
3. API calls `IBackgroundJobClient.Enqueue` for the definition-specific dispatcher wrapper carrying `executionId`.
4. On confirmed enqueue, service stores returned `HangfireBackgroundJobId`, changes to `queued`, and dispatcher verifies that it matches `perform.BackgroundJob.Id` when performed.
5. If enqueue fails, record is `enqueue_failed` with a safe operational reason and API returns an explicit failure; it never becomes `succeeded`.
6. A reconciliation worker finds stale `requested` records and safely marks/retries according to approved policy.

The API must not use `IRecurringJobManager.Trigger` for migrated manual runs: that path cannot assign a caller-created execution ID to the underlying occurrence before enqueue.

### Manual retry

- Only a permitted terminal execution can be retried.
- `POST .../executions/{id}/retry` creates a **new** `manual_retry` execution with `RetryOfExecutionId` set, then follows manual enqueue flow.
- It never reopens the original execution or deletes failed attempt data.

### Enqueue consistency

EF and Hangfire cannot be atomically committed together. The design uses durable-first persistence, idempotency keys, dispatcher idempotence and reconciliation. A durable outbox may be added later if stronger delivery guarantees are required; the release-one contract must explicitly expose `enqueue_failed` / stale `requested`, rather than masking the gap.

## API Design

All routes below require authentication, general rate limiting and `system:config`.

### Manual recurring trigger

`POST /api/admin/jobs/recurring/{definitionId}/trigger`

- Request: mandatory client-generated UUID `Idempotency-Key` header for one manual action and its transport retries.
- `202 Accepted`:

```json
{
  "definitionId": "content-publish-due",
  "trackingId": "5e4e83aa-1b31-4461-9d1f-6a30a261d89a",
  "status": "queued",
  "statusUrl": "/api/admin/jobs/executions/5e4e83aa-1b31-4461-9d1f-6a30a261d89a"
}
```

- `404`: definition does not exist in the allowlisted registry.
- `409`: definition does not permit manual execution or idempotency key conflicts with incompatible request.
- `503`/safe problem response: enqueue cannot be confirmed; persisted record is `enqueue_failed`.

### Execution read APIs

- `GET /api/admin/jobs/executions/{trackingId}`: lifecycle fields, safe output/error, source/initiator, linked retry origin and ordered attempts.
- `GET /api/admin/jobs/recurring/{definitionId}/executions?cursor=&pageSize=`: cursor-paginated history for one definition.
- Optional release-one list: `GET /api/admin/jobs/executions?definitionId=&status=&cursor=&pageSize=` for cross-definition operations.
- `POST /api/admin/jobs/executions/{trackingId}/retry`: accepts a terminal allowed run, returns the same accepted shape for its new execution.

Response DTOs must not expose raw payloads, stack traces, DB exception messages, arbitrary Hangfire job arguments or tenant details owned by a global job.

### Admin overview

Existing `/api/admin/jobs` retains Hangfire schedule fields (`lastExecution`, `lastState`) as **diagnostics**, not application outcome. Add a latest tracked execution summary per definition when query cost is bounded. UI must label these separately: for example, “Hangfire diagnostic” versus “Lần chạy đã theo dõi gần nhất”.

### Agent schedule run-now correction

`POST /api/admin/jobs/schedules/{scheduleId}/run-now` must call existing Orchestrator gRPC manual-run behavior instead of mutating `NextRunAt`.

- Extend `ManualRunResult` and gRPC `RunScheduleResponse` to return real `AgentScheduleRun.Id` plus optional `SessionId`; current contracts only surface session information.
- Success returns `202` with `trackingId`/`scheduleRunId`, `status`, status URL, and optional linked `sessionId`.
- `not_found` maps to `404`.
- `skipped_overlap` maps to `409`.
- Add `GET /api/admin/jobs/schedule-runs/{runId}` under `system:config`, with explicit current-tenant predicate. It returns run/schedule identity, derived source (`manual` when `WindowKey` begins `manual:`), status, started/heartbeat/finished timestamps, safe error, optional session ID/deep link, and optional `/content` link for trend-scan results.
- This endpoint is the primary Admin detail surface because `[trend-scan]` runs correctly have no `AgentSession`. When a session exists, UI exposes **Mở phiên điều phối** as a secondary action. Agent schedule details stay in their existing run/session model; this API does not force them into `RecurringJobExecution`.

## Component Breakdown

### Backend

- `src/shared/Clawbot.Domain/Jobs/`: dedicated execution/attempt aggregates, statuses and transition invariants.
- `src/shared/Clawbot.Infrastructure/Jobs/`: execution service, registry, dispatcher, executor adapters, EF configuration, Hangfire final-state filter and safe notification integration.
- `src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs`: new DB sets/configuration integration.
- `src/api/Clawbot.Api/Endpoints/AdminJobsEndpoints.cs`: typed admin contracts and endpoints; remove fake completed `BackgroundJob` trigger record.
- `src/agents/Clawbot.AgentService/Services/AgentScheduleRunner.cs` and gRPC contracts: expose real manual-run tracking ID.
- `deploy/migrations/`: one compatible additive migration after checking applied ledger/name collisions.

### Frontend

- `src/frontend/clawbot-web/src/shared/api/admin.ts`: immutable types and API calls that return accepted/execution DTOs instead of `void`.
- `src/frontend/clawbot-web/src/features/admin/AdminJobsTab.tsx`: mutation state, announcement and execution detail surface/history/retry control.
- Reuse existing query client patterns. Poll only detail executions with active state at roughly 3 seconds; stop at terminal state and separately invalidate overview.
- Keep the generic `JobCenterDialog` / `shared/api/jobs.ts` unchanged except for verified shared utilities: it is not the system execution console.

## Design Decisions

1. **Dedicated entity, not `BackgroundJob`.** Different scopes and retry history semantics make reuse misleading.
2. **Logical execution plus immutable attempts.** Preserves automation retry evidence and only terminalizes final outcome when Hangfire does.
3. **Dispatcher is primary lifecycle owner; applied-state filter is finalizer.** Business correlation and output exist before exception/final-state observation. Dispatcher always rethrows failure; only `IApplyStateFilter` after `AutomaticRetry` exhaustion may transition execution to final `failed`.
4. **`PerformContext.BackgroundJob.Id` correlates scheduled execution.** The recurring definition identifies an infinite schedule, not an occurrence; the Hangfire background-job ID identifies one occurrence and its retries.
5. **Definition-specific wrappers retain policy.** A generic dispatcher target drops original target attributes, so wrappers or an equivalent filter provider must carry effective retry, concurrency lock and custom-filter behavior.
6. **Registry is an allowlist.** Admin API never dispatches arbitrary recurring IDs or stored Hangfire expressions.
7. **Safe output only.** Redaction/bounding happens before persistence and public Admin API output; logs may retain operational correlation but not sensitive input/output.
8. **Agent schedules remain separate.** Correct their tracking path while honoring their existing DB locks, overlap behavior, heartbeats and sessions. A tenant-scoped schedule-run detail endpoint is primary because trend scans have no session.
9. **At-least-once-aware over false certainty.** The UI/API expose pending/enqueue failures and reconciliation instead of falsely claiming success.

## Non-Functional Requirements

### Security

- Permission `system:config` guards all system execution reads/actions.
- Treat `definitionId`, status filter, cursor/page size, retry ID and result links as boundary input; validate against registry/enums/bounds/first-party relative link rules.
- Do not present raw Hangfire exception text. Existing global failure notification filtering must skip tracked dispatcher jobs to prevent raw leakage and duplicate alerts.
- Do not add global job records to tenant-scoped query filters or Job Center.

### Reliability

- Lifecycle methods are idempotent under duplicate delivery and retry.
- Final failure notification happens once only after a conditional terminal persistence by the applied-state finalizer; it uses persisted redacted attempt data rather than `FailedState.Exception`.
- Preserve each job’s configured automatic retry count, queue, cron, timezone, lock and concurrency behavior during conversion. Assert effective Hangfire filters as well as schedule metadata because direct-target attributes do not flow automatically to a generic dispatcher.
- Monitor stale `requested`, stale `queued`, tracking persistence errors and dispatcher correlation failures.

### Performance

- Cursor pagination with bounded page size; default history page ≤ 50.
- Index active/history queries and avoid querying every execution detail in overview refresh.
- Progress writes are rate-limited by executor contract when high-frequency workloads are adapted; no unbounded realtime fan-out.
- Execution details poll only while active, stop on terminal states, and avoid duplicating generic job watcher subscriptions.

### Accessibility and UX

- Detail surface has accessible name, initial focus, keyboard close/return focus, `aria-live` status updates and text status in addition to color.
- Acknowledgement message says “Đã xếp hàng” / “Yêu cầu đã được nhận”, never “Thành công”, until terminal success.
- Attempt timeline declares retry count and timestamps in readable local time; error is visibly redacted/safe.
