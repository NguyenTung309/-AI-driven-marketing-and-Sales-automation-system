---
phase: implementation
title: Implementation Guide
description: Technical implementation notes, patterns, and code guidelines
---

# Implementation Guide

## Development Setup

- Start Docker Desktop and ensure `clawbot-sqlserver` is healthy.
- Use database `clawbot` on local port 11433.
- Apply migrations through `deploy/apply-migrations.ps1` or the transactional `migrate-local.ps1` runner.
- Do not add `GO`; every migration is one SQL batch inside an external transaction.
- Preserve the pre-0094 backup before testing destructive paths.

## Code Structure

- `deploy/migrations/0094_database_table_consolidation.sql`: one-shot data/schema consolidation.
- `deploy/repair_inbox_collaboration_tables.sql`: repeatable repair for labels and notes schema.
- `deploy/verify_database_table_consolidation.sql`: repeatable contract gate.
- `run-all.bat`: credential-column preflight, migration, runtime repair and verification.
- `migrate-local.ps1`: transactional ledger replay followed by runtime-column repair, collaboration repair and verification.
- `AppDbContext.cs`, `DomainModelConfigurations.cs`, `Inbox.cs`: canonical inbox model without legacy entities.
- Pancake token services: canonical identity, reconnect behavior, encryption-key failure and concurrent update handling.

## Implementation Notes

### Role consolidation

1. Acquire `TABLOCKX, HOLDLOCK` on `user_roles` for the migration transaction.
2. Map only same-tenant system roles whose normalized name resolves to exactly one Identity role.
3. Treat `AspNetUserRoles` as authoritative and require exact set equality for every user represented in legacy data, including users with zero canonical roles.
4. Abort on ambiguity or disagreement; do not automatically re-grant a stale legacy role.
5. Drop `user_roles` only after validation.

### Pancake page consolidation

1. Validate the normalized platform fits the canonical 32-character contract; never truncate it silently.
2. Match by `(tenant_id, platform, external_page_id)`.
3. Preserve existing canonical credentials when already present.
4. Treat an inactive or soft-deleted canonical inbox as authoritative: copy the credential without creating or reactivating another inbox.
5. Create a new active inbox only when no exact canonical identity exists.
6. Acquire `TABLOCKX, HOLDLOCK` on `pancake_pages` until commit.

### Channel token consolidation

1. Widen `encrypted_access_token` to nullable `NVARCHAR(MAX)` and add nullable refresh, webhook and expiry metadata.
2. Join `channel_tokens.inbox_id` directly to `inboxes.id`.
3. Fill only empty target fields and preserve inactive legacy status when the copied token is the canonical source.
4. Acquire `TABLOCKX, HOLDLOCK` before copy and drop so a legacy writer cannot race the migration.
5. Never decrypt or print credential values.

### Derived read state

`conversation_read_state` has no canonical destination. Migration 0094 acquires `TABLOCKX, HOLDLOCK`, requires the table to be empty and aborts if any row exists. This prevents both silent data loss and the count/drop race.

### Active schema repair and verification

The repeatable repair creates or repairs `labels`, `conversation_labels` and `conversation_notes`, enforces the unique active-inbox identity `(tenant_id, platform, external_page_id)`, validates exact FK/index definitions and rebuilds disabled expected indexes. Verification checks Identity role-table presence, exact column contracts, PK/FK mappings, enabled/trusted constraints, enabled indexes, exact filtered-index definitions, and legacy-table absence. It returns `15 flags|dbo count|total count` and throws unless all flags are `1`; counts remain informational.

### Credential runtime hardening

- Authenticated ciphertext encrypted with a different key now fails closed instead of being sent as a raw token.
- Startup plaintext migration uses a compare-and-update statement so a concurrent token rotation cannot be overwritten.
- Pancake token storage normalizes the full canonical identity, prefers the existing active row over historical duplicates, and reconnects an inactive row only when no active canonical row exists.
- Mint and direct bootstrap storage acquire a transaction-owned SQL Server application lock before the external/non-idempotent operation and hold it through commit.
- Page-token minting has timeout and circuit-breaker protection but no automatic retry.
- Credential-bearing Pancake HTTP clients suppress framework request logging; OpenTelemetry 1.9 retains its default query-value redaction.
- Ambiguous active page IDs across platforms fail closed during resolution.
- Polling excludes soft-deleted and unsupported inbox platforms, isolates failures per inbox and preserves the actual inbox platform through deduplication, publication, markers and trace output.

## Patterns & Best Practices

- Schema-qualified object names and exact metadata checks.
- `SET XACT_ABORT ON`, explicit transaction wrappers and migration ledger writes in the same transaction.
- Transaction-duration source locks before destructive consolidation.
- Dynamic SQL only where same-batch column compilation requires it.
- Fail-closed authorization and credential behavior.
- No plaintext credential operations or secret-bearing output.
- Historical migrations remain unchanged.

## Integration Points

- Supported runners repair the canonical access-token column before migration 0094 compiles.
- Runtime repair and verification run before build/service startup.
- Both runners repair runtime columns before verifying. The ledger makes 0094 a one-shot: once it is recorded, any column it added is never replayed, so a database that took an earlier revision of 0094 can only be reconciled by `deploy/repair_inbox_runtime_columns.sql`. Without that step the verification gate fails with no self-healing path.
- `JWT_SIGNING_KEY` and `ENCRYPTION_BASE64_KEY` are read consistently from `.env`; Compose forwards both to the API, while local launchers forward the shared keys to their services.
- API and AgentService consume the same canonical inbox model.
- `run-all.bat` forwards optional `PANCAKE_PAGE_ACCESS_TOKEN`, `PANCAKE_USER_ACCESS_TOKEN` and `PANCAKE_PAGE_ID` values from `deploy/.env` to AgentService; the current Docker Compose stack does not launch AgentService, so Compose alone does not run the bootstrap seeder.

## Verified Result

- Live contract: `111111111111111|91|102`.
- Happy fixture: `legacy_tables=0|identity_admin=1|disconnected_rows=1|disconnected_active=0|disconnected_token=1|channel_inactive=1|ledger=1`.
- Revoked-role ambiguity rolls back with `legacy_tables=4|ledger=0|canonical_roles=0|legacy_roles=1`.
- Concurrent read-state insertion rolls back with `read_state_rows=1|legacy_tables=4|ledger=0`.
- Concurrent legacy channel-token insertion is copied before the source table is dropped.
- Oversized platform values abort without truncation.
- Disabled required indexes fail verification and are rebuilt by repair; malformed same-name indexes and missing Identity role tables fail closed.
- Conflicting nonempty channel or Pancake credentials roll back without dropping source tables or writing the 0094 ledger row.
- Real SQL Server concurrency verification confirms one in-flight mint and one final active canonical inbox.
- `migrate-local.ps1` replays the full ledger against the live database, repairs, and reports `[OK] Database consolidation verified: 111111111111111|91|102`.
- Build completed with 0 warnings/errors; the full solution run is 186 tests — 185 pass and the SQL Server concurrency test is skipped unless `CLAWBOT_SQLSERVER_TEST_CONNECTION` is set.
