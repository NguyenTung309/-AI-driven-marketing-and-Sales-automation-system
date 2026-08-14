---
phase: monitoring
title: Background Job Execution Tracking Monitoring Plan
description: Observability, alerts and operating procedure for tracked recurring job executions
feature: background-job-execution-tracking
status: approved
created: 2026-08-14
---

# Background Job Execution Tracking Monitoring Plan

## Key Metrics

### Lifecycle and reliability

- `recurring_execution_requested_total` by definition/source.
- `recurring_execution_enqueued_total` and `recurring_execution_enqueue_failed_total` by definition.
- `recurring_execution_started_total`, `succeeded_total`, `failed_total`, `cancelled_total`, `skipped_total` by definition/source.
- `recurring_execution_retrying_total` and attempt count distribution by definition.
- Scheduled correlation reuse/duplicate-conflict count by definition; retries should reuse one `HangfireBackgroundJobId` under one logical execution.
- Applied-finalization success/failure count; an execution failure terminalization must occur only after Hangfire applies final `FailedState`.
- Duration histogram from queued-to-started and started-to-finished by definition/status.
- Count/age of `requested`, `queued`, `running` and `retrying` executions beyond definition-specific expected windows.
- Dispatcher correlation/transition conflict count and stale reconciliation outcome count.

### Data and UI health

- Execution/attempt row growth, retention cleanup deletion count/failure and indexed query latency.
- Detail/history API latency, error rate, authorization denial rate and cursor validation rejection count.
- Admin detail active-poll volume and realtime update delivery failures.
- Count of redaction calls/failures; no metric label may contain user input, raw output, exception text or IDs with sensitive meaning.

### Business/operational signals

- Manual trigger and manual retry volume by definition (with approved initiator audit available only in protected data, not metric labels).
- Difference between Hangfire diagnostic failure and tracked final failure, investigated as correlation gap.
- Agent schedule run-now outcomes: started/not-found/skipped-overlap and completion link availability.

## Logging Strategy

Use structured logs with safe operational correlation fields only:

- `executionId`, `attemptId`, `definitionId`, `source`, `status`, `hangfireBackgroundJobId`, `retryCount`, `attemptNumber`, duration and exception type/classification if approved.
- Log lifecycle transition failure, enqueue persistence/attachment failure, stale reconciliation, applied-finalization persistence failure and final notification publish failure at warning/error with correlation IDs.
- Do not log result summary, progress notes, raw exception text, stack trace in user-visible channel, payload JSON, job arguments, PII or credentials.
- Protected application logs can retain standard server exception information under existing retention/access controls, but API/UI/notification projection always comes from redacted bounded fields.

## Alerts

### Critical

| Condition | Action |
|---|---|
| Tracking persistence errors prevent dispatcher lifecycle updates for an enabled definition | Page/on-call; pause affected definition if correctness/side-effect risk is high; reconcile in-flight executions. |
| Unexpected duplicate execution/correlation conflicts exceed baseline | Stop further definition rollout; disable tracked registration for affected definition; investigate idempotency/concurrency. |
| Suspected raw error/output leakage through API/UI/notification | Disable affected display/notification path; start security incident process; rotate secrets if exposure is confirmed. |

### Warning

| Condition | Action |
|---|---|
| Enqueue-failed or stale-request count > 0 beyond approved grace window | Investigate API/Hangfire availability; run reconciliation and validate request IDs. |
| Final failure/retry rate exceeds definition baseline | Inspect safe execution timeline, then correlate protected server logs by execution/hangfire ID. |
| Active execution exceeds its expected duration | Check lock/worker health, cancellation and business dependency; do not retry blindly. |
| Retention cleanup fails or execution tables exceed capacity projection | Restore cleanup, index/review policy and defer further high-frequency definition rollout. |
| Admin execution detail API latency/error rate breaches SLO | Inspect query plans/indexes and polling/realtime load. |

Thresholds are definition-specific and must be set from a baseline after vertical-slice rollout; do not hardcode generic durations for heterogeneous jobs.

## Dashboard Design

The Admin Jobs screen is the operator-level dashboard. It should show a concise status summary for each definition plus a detail timeline; it is not a replacement for Hangfire’s technical dashboard.

Operational dashboard panels:

1. Executions by terminal status and source over time.
2. Queue-to-start and run duration percentiles by definition.
3. Retry/final-failure rate by definition.
4. Stale active/requested execution count and age.
5. Enqueue/correlation/notification persistence failures.
6. Table growth and retention cleanup health.

Use semantic labels and text status. Do not use color alone or include sensitive strings in graph legends/tooltips.

## Incident Response

### Triage workflow

1. Identify the `trackingId` from Admin Jobs and determine whether it is only `requested/queued` or has an actual attempt.
2. Inspect definition, source, timestamps, retry timeline and redacted final error in execution detail.
3. Correlate protected runtime logs with `executionId`, `attemptId` and `hangfireJobId`; use normal service log locations, not stale E2E logs.
4. Check Hangfire worker/queue health and recurring registration metadata only as diagnostics.
5. For `requested`/`enqueue_failed`, investigate API-to-Hangfire enqueue path and reconciliation before manual retry.
6. For `retrying`, allow configured retry policy to complete unless an incident owner decides to pause the definition.
7. For terminal failure, evaluate safe **Chạy lại** only after confirming idempotency/side-effect safety for that definition.
8. Record outcome, root cause and any registry/threshold adjustments.

### Recovery rules

- Never change a historical execution to succeeded to silence an alert.
- Never delete attempt history during incident response.
- Never rerun a high-impact definition solely because the UI remains queued; first inspect correlation and worker health.
- If an application rollback is needed, disable dispatcher registration for the affected definition but keep additive tracking data for reconciliation.

## Retention and Health Checks

- Retention policy is approved: retain detail/attempt data for 180 days, then use documented batch cleanup with preserved aggregate metrics.
- Cleanup must itself be observable, batch-limited and must not create unbounded self-tracking noise.
- Health checks should verify database reachability, Hangfire storage reachability where existing health checks permit, and optionally count stale executions without exposing their contents.
- Run daily checks during initial rollout to compare tracked terminal outcome counts with expected Hangfire executions by enabled definition.
