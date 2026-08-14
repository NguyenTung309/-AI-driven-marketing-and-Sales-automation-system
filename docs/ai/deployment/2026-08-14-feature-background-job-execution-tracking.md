---
phase: deployment
title: Background Job Execution Tracking Deployment Strategy
description: Safe schema rollout and controlled migration of recurring Hangfire registrations
feature: background-job-execution-tracking
status: approved
created: 2026-08-14
---

# Background Job Execution Tracking Deployment Strategy

## Infrastructure Scope

The feature changes API, Infrastructure, AgentService, Hangfire registration and the web Admin Jobs tab. It introduces additive database tables/indexes and changes the target of recurring Hangfire registrations. No new external service, secret or environment variable is required for release one.

## Release Strategy

Use a controlled, incremental rollout—never convert all recurring definitions blindly.

1. Deploy additive schema and read-safe code before changing registrations.
2. Enable the registry/definition-specific dispatcher wrapper for `health-check` first; it must retain its current 60-second concurrency lock and existing retry behavior.
3. Compare pre/post Hangfire registration metadata **and effective retry/concurrency/custom filters**, then observe several scheduled/manual runs.
4. Convert remaining definitions in small side-effect-aware batches, pausing after each batch for metrics and operations validation.
5. Enable the UI detail/retry controls only after backend read/API contract is live for the definitions shown.

A feature/configuration gate may select dispatcher registration per definition during rollout. The gate must default to the old direct registration only until that definition is explicitly validated; once tracked path is active, Admin trigger must use the correlated dispatcher route—not fake completion fallback.

## Database Migrations

- Before creating the migration, inspect the active deployment ledger (`schema_migrations`) and existing filenames. Prefixes in the checkout are not sufficient evidence that a number is free.
- Add only the new execution and attempt tables, FKs and indexes. Do not alter or repurpose `background_jobs`.
- Use a unique full migration filename accepted by `apply-migrations.ps1` and an idempotent SQL body compatible with its transaction-per-file behavior.
- Do not use `GO`.
- Verify clean-install and upgrade application on SQL Server-compatible environment and the SQLite test model/provider shims.
- Do not couple schema deployment to initial broad registration conversion; schema may remain unused safely until a definition is enabled.

## Pre-deployment Checklist

- [ ] Requirements/design reviews approved and all decisions that influence persisted/API behavior are closed.
- [ ] Domain, API, dispatcher, AgentService and frontend test suites are green; changed-code coverage is at least 80%.
- [ ] Security review validates `system:config`, ID/cursor/status validation, output/error redaction, link validation and notification ownership.
- [ ] Migration filename/ledger/API compatibility reviewed; no `GO`; SQL applied successfully in rehearsal.
- [ ] Baseline each recurring registration’s ID, cron, queue, timezone, retry setting, effective concurrency behavior and custom filters; source method/type attributes cannot be assumed to survive a generic dispatcher target.
- [ ] Operations runbook and dashboards/alerts for stale request, enqueue failure, final failure, tracking persistence error and growth are available.
- [ ] A rollback owner and maintenance window/communication path are identified for each rollout batch.

## Deployment Steps

1. Deploy schema migration through the existing migration runner and validate ledger entry.
2. Deploy backend capable of reading/writing execution records, with all registry entries initially disabled for tracked dispatch if a gate is used.
3. Verify API health, registration inventory and that generic `BackgroundJob`/Job Center flows remain unaffected.
4. Enable `health-check` vertical slice through definition-specific dispatcher wrapper; compare actual Hangfire recurring metadata and effective filters with baseline.
5. Execute one manual trigger in non-production or controlled production window. Confirm `202 queued`, tracking detail, start, terminal result, safe notification and no fake `BackgroundJob` success row.
6. Observe at least one cron occurrence and a controlled test-executor retry/failure; confirm retries reuse `PerformContext.BackgroundJob.Id` and final `FailedState` alone terminalizes the logical execution.
7. Deploy/enable frontend UI once API is serving typed contract; verify in browser with administrator credentials.
8. Convert the next approved batch only if telemetry and manual checks are healthy; record batch/definition status in release notes.

## Post-deployment Validation

- [ ] New execution and attempt records are created for enabled definitions with valid correlation.
- [ ] Admin trigger response and UI label are acknowledgement/queued, not success.
- [ ] At least one active execution transitions and finalizes; one safe failure path creates one notification.
- [ ] No duplicate recurring registration, change in cron, queue, timezone, effective retry/concurrency/custom filter or unexpected concurrent workload appears.
- [ ] System executions remain unavailable through tenant `/api/jobs` and Job Center.
- [ ] Agent schedule Run Now returns a real run ID plus optional session link, schedule-run detail works for trend scans with no session, and overlap conflict is respected.
- [ ] Table/index growth, stale states and errors stay within alert thresholds.

## Rollback Plan

### Application/registration rollback

1. Disable dispatcher registration for the affected definition and restore the exact baseline direct registration target/configuration.
2. Roll back application binaries only after ensuring old binaries tolerate the new additive tables (they should ignore them).
3. Stop exposing UI actions that call new endpoints if the backend contract is unavailable.
4. Do not mark in-flight tracked executions succeeded or delete their history. Leave them for operations reconciliation and document their state.

### Database rollback

- Do **not** drop new execution/attempt tables or indexes during a hot rollback. The migration is forward-compatible/additive; retain data for diagnosis.
- If migration itself fails before successful ledger record, follow the existing migration-runner incident process and restore from tested database backup only when necessary.

## Configuration and Secrets

- No new secret is introduced.
- Any rollout flag must be configuration-only, default-safe, documented, validated at startup and auditable.
- Do not place definition executor names, connection strings or raw Hangfire arguments in a user-editable configuration surface.

## Release Communication

Release notes and operator guidance must explicitly say:

- “Đã xếp hàng” means the request was accepted, not that the job succeeded.
- The tracked execution detail is the authoritative application outcome; Hangfire last-state is supporting diagnostic data.
- Use **Chạy lại** only for the displayed terminal tracked execution; this creates a new tracked run.
