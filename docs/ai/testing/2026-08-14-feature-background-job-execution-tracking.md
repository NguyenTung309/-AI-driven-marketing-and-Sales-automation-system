---
phase: testing
title: Background Job Execution Tracking Test Strategy
description: Test plan for durable recurring execution tracking, retries and the Admin Jobs experience
feature: background-job-execution-tracking
status: approved
created: 2026-08-14
---

# Background Job Execution Tracking Test Strategy

## Test Coverage Goals

- 80% minimum coverage for newly added/changed backend and frontend code; exercise every lifecycle terminal branch.
- Unit tests for domain state, persistence and safe data handling.
- API/integration tests for permissions, Hangfire correlation and Agent schedule manual-run behavior.
- Playwright coverage for the critical admin workflow and accessibility at 320, 768, 1024 and 1440 widths.
- Preserve regression coverage for generic tenant `BackgroundJob` / Job Center flows.

## Unit and Persistence Tests

### Execution aggregate

- [ ] Creates manual execution as `requested`, scheduled execution with approved source, and preserves requester audit identity.
- [ ] Rejects invalid state transitions and terminal-state rewrites.
- [ ] `MarkQueued`, `MarkRunning`, `MarkRetrying`, completion, final failure, cancellation, skipped and enqueue failure set only the correct timestamps/fields.
- [ ] Manual retry creates a new execution linked to original terminal execution and does not mutate original values.
- [ ] Duplicate equivalent manual request key returns existing active execution; incompatible reuse is rejected.

### Attempt aggregate and service

- [ ] Derives monotonic immutable attempt numbers as `RetryCount + 1`, enforces unique `(ExecutionId, AttemptNumber)`, and allows all retry slots to retain the same Hangfire background-job ID.
- [ ] Creates/reuses a scheduled execution by `(DefinitionId, PerformContext.BackgroundJob.Id)` so retry does not create a second logical execution.
- [ ] Preserves failed retry attempt when the next attempt starts.
- [ ] Does not run/complete an already terminal logical execution on duplicate delivery.
- [ ] Leaves execution `retrying` after retryable exception and only marks `failed` through finalization.
- [ ] Reconciliation transitions stale `requested` records truthfully and emits the expected operational signal.

### Safe data paths

- [ ] Progress percentage clamps; blank/unreported progress remains null rather than fabricated.
- [ ] Progress note, result summary and error are redacted and bounded before persistence.
- [ ] Result links reject external, malformed and unsafe protocol values.
- [ ] Final notification uses stored safe error/summary, never raw exception text.

## Dispatcher and Hangfire Integration Tests

- [ ] Scheduled dispatcher invocation resolves `health-check` registry definition and creates one `scheduled` execution using `PerformContext.BackgroundJob.Id` plus one attempt.
- [ ] Manual enqueue passes persisted tracking ID to dispatcher, associates returned Hangfire job ID, and verifies it against `PerformContext.BackgroundJob.Id`.
- [ ] Selected `health-check` vertical-slice executor succeeds: `queued → running → succeeded`, timing and safe database-responsive result are visible.
- [ ] Test-only retryable executor failure creates a failed attempt, leaves execution retrying and rethrows for Hangfire; production health-check is never intentionally failed.
- [ ] Automatic retry with the same `BackgroundJob.Id` creates a second retry-slot attempt; final success terminalizes parent succeeded without losing failure evidence.
- [ ] `IApplyStateFilter` ignores retryable candidate failure, then finalizes parent failed exactly once only after Hangfire has applied final `FailedState`; notification uses persisted redacted attempt error.
- [ ] Cancellation/skipped paths preserve approved semantic and do not fabricate result success.
- [ ] Dispatcher wrapper registration retains original recurring job ID, queue, cron, timezone and effective retry, concurrency lock and custom filters.
- [ ] Legacy `JobFailureNotificationFilter` does not duplicate or expose raw failure for dispatcher wrapper executions.

## API Tests

- [ ] All execution/trigger/retry endpoints reject unauthenticated and missing-`system:config` callers.
- [ ] Trigger rejects unknown/non-manual registry IDs and malformed idempotency input.
- [ ] Trigger returns `202`, `queued`, tracking ID and canonical status URL; it creates no fake completed `BackgroundJob` record.
- [ ] Enqueue failure returns a safe error and leaves durable `enqueue_failed` record—not success.
- [ ] Detail only projects safe fields, ordered attempts and no raw payload/exception data.
- [ ] History is definition-scoped, cursor-paginated, bounded, newest-first and does not leak data to tenant/job-center APIs.
- [ ] Retry rejects non-terminal/disallowed statuses, creates correctly linked new execution when eligible and returns accepted tracking contract.
- [ ] Admin overview distinguishes diagnostic Hangfire state from latest tracked execution state.
- [ ] Schedule run-now calls gRPC/manual runner, returns real `AgentScheduleRun.Id` plus optional session ID; it maps missing schedule to `404` and overlap to `409`.
- [ ] Schedule-run detail endpoint enforces current-tenant + `system:config`, is usable for a trend scan with null session ID, and projects only safe run error/details plus approved session/content links.

## Frontend Tests

### Type/source-contract tests

- [ ] `triggerAdminRecurringJob` and schedule run-now return typed accepted payloads rather than `void`.
- [ ] Status union distinguishes acknowledgment (`requested`/`queued`) from terminal success.
- [ ] No mutable update of query-cache data; active polling stops at terminal state.

### Playwright route-mocked flows

- [ ] Click **Chạy ngay**: initial UI says “Đã xếp hàng” and opens/focuses execution detail; it does not show succeeded immediately.
- [ ] Poll transition: queued → running → succeeded updates timestamps, progress and safe summary.
- [ ] Retryable failure shows retrying and two immutable timeline attempts; final failure shows a redacted message only.
- [ ] **Chạy lại** returns and follows a new tracking ID while retaining prior attempt history/lineage.
- [ ] Agent schedule run-now displays tracking result; `409` overlap has clear non-success feedback.
- [ ] Keyboard: initial focus, Tab containment, Escape close, focus return and text/live announcements work.
- [ ] Responsive screens at 320/768/1024/1440 have no horizontal overflow and retain timeline/detail readability.

## Test Data and Failure Injection

- Use `health-check` as the low-risk real vertical slice, with deterministic approved safe database-responsive result.
- Use a test-only executor that fails a configured number of attempts to exercise retry/finalization without outbound side effects; never force failure of production health-check.
- Use redaction fixture strings including PII/token-like content, path-like content and stack trace fragments.
- Use admin and non-admin permission fixtures; include global execution records with distinct tenant-associated output to verify non-leakage.
- Use project SQLite `AppDbContext` test conventions plus a controlled Hangfire storage configuration where retry final-state tests require it.

## Automated Verification Commands

Run the repository’s documented targeted test projects first, then the configured backend and frontend build/lint/type-check commands. Record actual command output and coverage; do not claim green checks without running them.

Minimum pre-review gates:

- [ ] Targeted domain/infrastructure/API/AgentService tests pass.
- [ ] Full affected backend release builds pass with analyzer/audit gates.
- [ ] Frontend lint, type check and production build pass.
- [ ] Playwright Admin Jobs scenarios pass.
- [ ] Migration applies to clean and upgrade test databases through the real migration runner.
- [ ] Security review finds no Critical/High issue.

## Manual Testing

- [ ] In a non-production environment, run the chosen vertical-slice recurring job from its cron and from **Chạy ngay**; compare acknowledgment and final status.
- [ ] Force a retryable failure, confirm history includes attempts and a single final notification only after retry exhaustion.
- [ ] Confirm safe UI/API/notification content against raw server logs; no PII, stack trace, SQL, path or token appears.
- [ ] Compare each migrated Hangfire registration with pre-change cron/queue/timezone/retry/concurrency behavior.
- [ ] Confirm a generic `/api/jobs` user job still appears in Job Center and a system execution does not.
- [ ] Test accessibility with keyboard and a screen reader-supported browser announcement where available.
