---
phase: testing
title: Testing Strategy
description: Define testing approach, test cases, and quality assurance
---

# Testing Strategy

## Test Coverage Goals

Acceptance combines SQL Server integration fixtures, repeatable schema verification, unit tests for ciphertext handling, solution build/test, and startup-runner validation. Destructive cases run only against a temporary database restored from the retained pre-0094 backup.

## SQL Integration Tests

### Happy path

- [x] Ledger records `0094_database_table_consolidation.sql` once.
- [x] Four legacy tables are absent.
- [x] Three collaboration tables are present with exact column, PK, FK and index contracts.
- [x] Canonical inbox credential columns are nullable and use expected types.
- [x] Verification returns `111111111111111|91|102`.
- [x] Happy fixture returns `legacy_tables=0|identity_admin=1|disconnected_rows=1|disconnected_active=0|disconnected_token=1|channel_inactive=1|ledger=1`.

### Authorization rollback

- [x] Exact legacy/canonical role agreement succeeds without duplicating Identity roles.
- [x] A legacy role for a fully revoked user is rejected and rolled back.
- [x] Result: `legacy_tables=4|ledger=0|canonical_roles=0|legacy_roles=1`.
- [x] Ambiguous, custom or cross-tenant role mappings fail closed.
- [x] Role names are matched byte-exactly: the database collation is `SQL_Latin1_General_CP1_CI_AS`, so a case-only or trailing-space difference between a legacy row and an `AspNetRoles` row would silently compare equal without an explicit `Latin1_General_100_BIN2` collation on both operands. Three fixtures pin this — legacy-side padding, Identity-side padding, and Identity-side case drift — and each must fail closed rather than bind a user to a role nobody granted.

### Credential and lifecycle fixtures

- [x] Channel access/refresh/webhook/expiry values move to the matching inbox.
- [x] An inactive legacy channel token disables the target only when it supplies the canonical access token.
- [x] An active legacy Pancake page does not recreate or reactivate an intentionally disconnected canonical inbox.
- [x] An inactive legacy Pancake page merges into an existing active canonical inbox without creating a duplicate.
- [x] Conflicting nonempty channel credentials abort with the source row and all four legacy tables preserved; no 0094 ledger row is written.
- [x] Conflicting nonempty Pancake credentials abort with the source row and all four legacy tables preserved; no 0094 ledger row is written.
- [x] Existing canonical credentials are not overwritten.
- [x] Ciphertext longer than the historical 1024-character limit is not truncated.
- [x] A normalized platform longer than 32 characters aborts and preserves the source table.
- [x] Mint provenance carries over: a legacy page row with a known mint time populates `inboxes.page_token_minted_at` instead of leaving it null, so token-age reporting does not reset to "never minted" after consolidation.
- [x] A legacy page row whose token expiry is already in the past carries the stale expiry through unchanged rather than being silently refreshed.

### Destructive fixture matrix

Each fixture is applied to a fresh restore of the pre-0094 backup, then 0094 runs and the outcome is asserted. All 19 pass.

| Fixture | Expected outcome |
| --- | --- |
| `fixture-none` | Clean consolidation; ledger written |
| `fixture-role-exact` | Legacy role agrees with Identity; no duplicate grant |
| `fixture-role-revoked` | Reject + rollback; all four legacy tables preserved |
| `fixture-role-ambiguous-identity` | Fail closed |
| `fixture-role-cross-tenant` | Fail closed |
| `fixture-role-non-system` | Fail closed |
| `fixture-role-identity-name-case` | Fail closed (BIN2 comparison) |
| `fixture-role-identity-padded` | Fail closed (BIN2 comparison) |
| `fixture-role-legacy-padded` | Fail closed (BIN2 comparison) |
| `fixture-channel-credential-case` | Fail closed (BIN2 comparison) |
| `fixture-channel-credential-conflict` | Abort; source row and legacy tables preserved |
| `fixture-pancake-credential-conflict` | Abort; source row and legacy tables preserved |
| `fixture-pancake-duplicate-source` | Duplicate legacy sources resolve to one canonical inbox |
| `fixture-pancake-mint` | `page_token_minted_at` carried over |
| `fixture-pancake-stale-expiry` | Past expiry carried over unchanged |
| `fixture-inbox-duplicate-active` | Merge into existing active inbox; no duplicate |
| `fixture-inbox-platform-invalid` | Abort; source table preserved |
| `fixture-credential-column-drift` | Repair restores canonical credential columns |
| `fixture-read-state-not-empty` | Concurrent insert observed; rollback |

### Concurrency and rollback

- [x] A concurrent committed `conversation_read_state` insert is observed after the migration lock and causes rollback.
- [x] Result: `read_state_rows=1|legacy_tables=4|ledger=0`.
- [x] A concurrent legacy channel-token insert commits before the migration source lock is acquired, is copied, and the source table is then dropped.
- [x] Source locks are held for `user_roles`, `channel_tokens`, `pancake_pages` and `conversation_read_state` until the outer transaction commits.

### Repair and verification

- [x] Migration replay is idempotent through the ledger.
- [x] Collaboration repair runs twice without creating duplicate objects.
- [x] Runtime-column repair runs twice against the live database without error.
- [x] Ledger-recorded drift is self-healed rather than fatal. The live database had `0094` recorded but no `inboxes.page_token_minted_at`, because the ledger row was written by an earlier revision of the migration and a recorded migration is never replayed. Verification correctly failed with `111111111111111` minus flag 12 (`111111111110111|91|102`, exit 1). `run-all.bat` already ran the runtime-column repair before verifying; `migrate-local.ps1` did not, so it had no path back to a passing state. The repair is now part of both runners and the live database verifies clean.
- [x] Standalone verification exits non-zero when any contract flag is zero.
- [x] A disabled required index is rejected, rebuilt by repair, and then passes verification.
- [x] Exact table counts are reported but are not used as a permanent startup gate.

## Unit and Build Tests

- [x] `PancakeTokenCipher.HasAuthenticatedEnvelope` recognizes ciphertext produced with another key.
- [x] Raw token values are not misclassified as authenticated ciphertext.
- [x] `DecryptOrRaw` throws on authenticated ciphertext that cannot be decrypted with the configured key.

### Legacy AES-CBC ciphertext under a wrong key

Rows written before the authenticated-envelope change hold `[IV(16)][AES-CBC cipher]` with no tag, so a wrong key fails PKCS7 padding instead of failing authentication. The fixtures use a fixed all-zero IV: with a random IV, roughly 1 in 256 wrong-key decryptions produces valid-looking padding and returns garbage, which would make the tests intermittently green.

- [x] `DecryptOrRaw` throws on a legacy blob encrypted with another key — it must not fall through to the raw-token branch.
- [x] `DecryptOrRaw` still returns the plaintext when the key matches.
- [x] `IsEncrypted` reports false for a legacy blob under the wrong key.
- [x] `HasLegacyCiphertextEnvelope` is true for that blob and `HasAuthenticatedEnvelope` is false; this pair is what stops the migrator treating stored ciphertext as a raw token.
- [x] `HasLegacyCiphertextEnvelope` is false for a raw JWT-shaped token.
- [x] `InboxTokenEncryptionMigrator` throws `inbox_token_encryption_key_mismatch:{id}` and leaves the stored value byte-identical, for both legacy-CBC and authenticated ciphertext written under another key. Encrypting on top of an unreadable value would destroy the credential permanently.
- [x] The migrator still encrypts genuinely raw tokens in place, and a second pass is a no-op — no double encryption.

### API partial-commit

- [x] `AdminUsersEndpoints` validates the Pancake page binding before any Identity write. Previously `CreateAsync` and `UpdateAsync` committed the user, roles and lockout through `UserManager` and only then reached `ConnectPancakePageAsync`, which can return `inbox_not_found`; the caller saw HTTP 400 while the user and role changes had already persisted, and a retry failed on duplicate email. Verified by inspection and build; the endpoints have no integration harness, so this is not covered by an automated test.
- [x] Page-token minting sends exactly one provider request after an HTTP 503; the non-idempotent operation is not retried.
- [x] Credential-bearing Pancake typed clients emit no `IHttpClientFactory` request logs.
- [x] Token storage normalizes the canonical page identity and updates the active row when historical duplicates exist.
- [x] Polling isolates per-inbox decryption/provider failures, preserves host cancellation, and carries each inbox platform through deduplication, publication and persistence for Facebook, Instagram, TikTok, Pancake and Zalo.
- [x] A real SQL Server concurrency test proves the transaction-owned application lock permits only one in-flight mint, leaves one active canonical inbox and stores the final minted token.
- [x] `dotnet build Clawbot.sln --no-restore`: 0 warnings, 0 errors.
- [x] Full solution test run: 186 total across `Clawbot.Agents.Tests` (130), `Clawbot.Infrastructure.Tests` (48) and `Clawbot.Api.Tests` (8).
- [x] Without `CLAWBOT_SQLSERVER_TEST_CONNECTION`, the SQL Server concurrency test is explicitly skipped while the remaining 185 tests pass.
- [x] `run-all.bat --dry-run` succeeds.
- [x] `migrate-local.ps1` parses, replays the full ledger against the live database, repairs and reports `[OK] Database consolidation verified: 111111111111111|91|102` with exit 0.
- [x] The temporary `clawbot_consolidation_test` database and every `clawbot_token_lock_*` database are dropped; only `clawbot` remains.
- [x] `docker compose -f deploy/docker-compose.yml --env-file deploy/.env config --quiet` succeeds, and fails with `required variable JWT_SIGNING_KEY is missing a value` when that key is absent — the fallback secrets that previously masked this are gone.

## Test Data and Safety

- Backup: `/var/opt/mssql/data/clawbot-before-0094-20260729.bak`.
- Temporary database: `clawbot_consolidation_test` (dropped after verification).
- The SQL Server application-lock test creates and drops a unique `clawbot_token_lock_*` database per run.
- Fixtures use synthetic identifiers and synthetic encrypted-looking values only.
- Test output contains counts and equality flags, never production ciphertext, tokens, PII or customer content.

## Manual Follow-up

- Login and permission smoke test after deployment.
- Channel connect/reconnect and outbound send smoke test for each Pancake-backed platform.
- Labels and notes API smoke test.
- Monitor authorization and channel-authentication errors during the first deployment window.
