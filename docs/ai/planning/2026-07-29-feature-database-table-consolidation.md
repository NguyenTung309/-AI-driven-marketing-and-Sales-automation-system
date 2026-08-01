---
phase: planning
title: Project Planning & Task Breakdown
description: Break down work into actionable tasks and estimate timeline
---

# Project Planning & Task Breakdown

## Milestones

- [x] Milestone 1: Inventory live database and map code usage.
- [x] Milestone 2: Confirm canonical stores and identify four removable tables.
- [x] Milestone 3: Implement migration, repair, model cleanup, runner hardening and verification.
- [x] Milestone 4: Run local migration fixtures, live verification, build and automated tests.
- [ ] Milestone 5: Commit/push and production deployment. Không thuộc phạm vi lần cập nhật này.

## Task Breakdown

### Phase 1: Audit

- [x] Query table count, row counts, storage, foreign keys and index usage.
- [x] Map EF `DbSet`/configuration and raw runtime references.
- [x] Separate active infrastructure, active optional modules, legacy tables and missing active tables.
- [x] Confirm obsolete set is exactly `user_roles`, `channel_tokens`, `conversation_read_state`, `pancake_pages`.
- [x] Confirm `labels`, `conversation_labels`, `conversation_notes` are active collaboration tables that must be restored.
- [x] Reclassify table count as informational rather than a fixed acceptance gate.

### Phase 2: Migration and repair

- [x] Add `0094_database_table_consolidation.sql` with a transaction-required guard.
- [x] Add canonical credential columns to `inboxes` and widen access-token storage to `NVARCHAR(MAX)`.
- [x] Implement fail-closed exact-equality Identity role reconciliation, including zero-role revocation.
- [x] Preserve disconnected canonical inbox state as authoritative for Pancake.
- [x] Merge `channel_tokens` into `inboxes` without reactivating an inactive canonical channel.
- [x] Check `conversation_read_state` under `TABLOCKX, HOLDLOCK` and rollback if any row exists.
- [x] Drop `user_roles`, `channel_tokens`, `conversation_read_state`, `pancake_pages` only after all guards pass.
- [x] Add transactional, idempotent repair for `labels`, `conversation_labels`, `conversation_notes`.

### Phase 3: Runtime/model/config cleanup

- [x] Remove stale `DbSet` properties, entities and EF configurations.
- [x] Remove legacy-table recreation from `DevDataSeeder` and runtime repair paths.
- [x] Remove obsolete baseline repair references from `run-all.bat`.
- [x] Update manual cleanup paths so they do not reference dropped tables.
- [x] Add schema contract verification before service startup.
- [x] Make `migrate-local.ps1` write migration content and ledger row in one transaction.
- [x] Align `JWT_SIGNING_KEY` and `ENCRYPTION_BASE64_KEY` defaults/overrides.
- [x] Forward shared keys through local runner and Docker Compose to the services that consume them.
- [x] Forward optional Pancake bootstrap variables from `deploy/.env` to AgentService in `run-all.bat`; document that the current Compose stack does not launch AgentService.
- [x] Remove automatic retry and framework request logging from credential-bearing Pancake HTTP clients.
- [x] Serialize canonical page-token mint/storage with a transaction-owned SQL Server application lock.
- [x] Isolate polling failures per inbox and preserve the actual inbox platform end to end.

### Phase 4: Verification

- [x] Apply pending migration against local SQL Server.
- [x] Verify four legacy tables are absent and three collaboration tables satisfy their contracts.
- [x] Verify live result `111111111111111|91|102`; treat both counts as informational.
- [x] Verify happy fixture: `legacy_tables=0 identity_admin=1 disconnected_rows=1 disconnected_active=0 disconnected_token=1 channel_inactive=1 ledger=1`.
- [x] Verify revoked-role rollback: `legacy_tables=4 ledger=0 canonical_roles=0 legacy_roles=1`.
- [x] Verify concurrent read-state rollback: `read_state_rows=1 legacy_tables=4 ledger=0`.
- [x] Re-run migration and repair paths to prove idempotency.
- [x] Run `dotnet build Clawbot.sln --no-restore`: 0 warnings, 0 errors.
- [x] Run automated tests: all 154 passed with the SQL Server integration connection configured.
- [x] Verify channel/Pancake credential-conflict rollback and inactive-page merge fixtures.
- [x] Verify real SQL Server concurrent mint serialization and final canonical token state.
- [x] Run `run-all.bat --dry-run` after Pancake bootstrap forwarding.
- [x] Run `docker compose config`.
- [ ] Run production backup, deployment and post-deploy smoke tests.

## Dependencies

- SQL Server container `clawbot-sqlserver` must be healthy for local verification.
- `deploy/apply-migrations.ps1` and `migrate-local.ps1` must preserve the same transaction-plus-ledger contract.
- Migration 0094 must run after 0093 and before schema verification.
- Runtime/model cleanup must land together with migration so removed tables are not recreated or queried.
- API/Gateway must share `JWT_SIGNING_KEY`; API/AgentService must share `ENCRYPTION_BASE64_KEY`.

## Timeline & Estimates

- Audit and design: completed.
- Migration, repair and model cleanup: completed.
- Runner/config hardening: completed.
- Local integration fixtures, build and tests: completed.
- Commit/push and production rollout: not performed by this task.

## Risks & Mitigation

- **Unmappable or ambiguous legacy role:** fail and rollback; ledger remains absent.
- **Privilege restoration after revocation:** require exact set equality, including intentional zero-role state.
- **Duplicate active Pancake inbox:** canonical disconnected state wins; do not create or reactivate an active duplicate.
- **Credential truncation:** widen encrypted access token storage before copy.
- **Concurrent read-state writer:** hold `TABLOCKX, HOLDLOCK` from emptiness check through drop/commit.
- **Partial local migration ledger:** both migration runners commit schema/data and ledger atomically.
- **Schema recreated later:** remove all runtime/model references and enforce pre-start verification.
- **Misleading count drift:** alert on failed contract flags; report table counts only as context.
- **Cryptographic key drift:** use consistent environment keys and Compose forwarding.
- **Destructive rollback after commit:** require database backup/snapshot before production application.

## Resources Needed

- Local Docker SQL Server and `sqlcmd`.
- .NET SDK pinned by the repository.
- Existing migration runners, `run-all.bat` and Docker Compose.
- SQL fixture harness used for happy, revoked-role and concurrent read-state scenarios.
- Production backup/snapshot and maintenance window when rollout is scheduled.
