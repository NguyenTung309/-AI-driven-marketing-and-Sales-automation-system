---
phase: deployment
title: Deployment Strategy
description: Define deployment process, infrastructure, and release procedures
---

# Deployment Strategy

## Infrastructure

- Development uses Docker SQL Server container `clawbot-sqlserver`.
- Staging and production use the same migration files and `dbo.schema_migrations` ledger.
- No new infrastructure service is required.

## Deployment Pipeline

1. Back up or snapshot the database.
2. Load environment overrides.
3. Repair the canonical inbox access-token column if schema drift removed or narrowed it.
4. Apply pending migrations transactionally.
5. Run repeatable collaboration-table repair.
6. Run the 15-flag consolidation contract gate.
7. Restore/build/test the application.
8. Start API, AgentService, Gateway and frontend.
9. Run authorization and channel smoke tests before opening traffic.

## CI/CD Gates

- Migration happy path, rollback path, lifecycle and concurrency fixtures pass.
- Standalone schema verification returns all 15 flags and exits zero.
- Repeatable repair is idempotent and restores disabled required indexes.
- .NET build and test suite pass.
- Task-scoped code and security review have no unresolved critical/high findings.

## Environment Configuration

- `JWT_SIGNING_KEY` is the external environment name shared by API and Gateway.
- `ENCRYPTION_BASE64_KEY` is the external environment name shared by API and AgentService.
- Docker Compose maps these to `Jwt__SigningKey` and `Encryption__Base64Key` inside .NET containers.
- `run-all.bat` forwards optional `PANCAKE_PAGE_ACCESS_TOKEN`, `PANCAKE_USER_ACCESS_TOKEN` and `PANCAKE_PAGE_ID` values from `deploy/.env` to AgentService for the bootstrap seeder.
- The current Docker Compose file does not launch AgentService; a Compose-only startup therefore does not run Pancake bootstrap and must not be documented as doing so.
- Development defaults are local-only and must be replaced before shared, staging or production use.
- Credentials are never embedded in migration SQL or printed by verification.

## Database Migration Contract

- Migration `0094_database_table_consolidation.sql` runs after `0093`.
- Every supported runner supplies `SET XACT_ABORT ON` and one external transaction containing migration DDL/DML plus ledger insert.
- Migration 0094 also rejects execution without an active transaction.
- Legacy source tables are locked for the transaction before copy/validation/drop.
- Historical migrations are unchanged.
- Repeatable repair is intentionally outside the migration ledger.

## Deployment Steps

1. Record source row counts for `user_roles`, `channel_tokens`, `pancake_pages` and `conversation_read_state` without selecting row contents.
2. Confirm `conversation_read_state` is empty; otherwise stop and reconcile it.
3. Confirm legacy and canonical role assignments agree exactly for every legacy user.
4. Stop old application writers or use the migration's source-table locking in a controlled maintenance window.
5. Apply the release through `run-all.bat` or the canonical migration runner.
6. Require output beginning with `111111111111111|`; table counts are audit information only.
7. Verify login/permissions, connected channels, labels and notes.
8. Monitor errors before completing rollout.

## Verified Local Deployment

- Final live verification: `111111111111111|91|102`.
- Migration and repair idempotency passed.
- Build passed with 0 warnings/errors; the full solution run passed all 154 tests with the SQL Server integration connection configured.
- `run-all.bat --dry-run` passed after Pancake bootstrap forwarding was added.
- Docker Compose configuration validation passed.
- No commit, push or production deployment was performed as part of this implementation session.

## Rollback Plan

### Before transaction commit

Any validation, DDL or copy failure rolls back automatically and leaves the 0094 ledger row absent. Reconcile the reported conflict and rerun.

### After transaction commit

There is no automatic down migration for dropped legacy tables:

1. Restore the protected backup into a separate database.
2. Compare canonical roles and inbox credentials by count/hash without exposing values.
3. Extract a specific legacy table only if canonical data is proven incorrect.
4. Revert application binaries only together with a database state they can safely read.
5. Restore production directly only after explicit incident approval.

The canonical sources after release are `AspNetUserRoles` and `inboxes`; do not recreate empty legacy tables.
