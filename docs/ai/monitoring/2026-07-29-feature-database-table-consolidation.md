---
phase: monitoring
title: Monitoring & Observability
description: Define monitoring strategy, metrics, alerts, and incident response
---

# Monitoring & Observability

## Key Metrics

### Migration metrics

- Presence and timestamp of ledger row `0094_database_table_consolidation.sql`.
- Migration, repair and verification duration.
- Source counts reported for `user_roles`, `channel_tokens`, `pancake_pages` and `conversation_read_state`.
- Fifteen explicit schema flags from `verify_database_table_consolidation.sql`.
- `dbo` and total table counts as informational trend data, not an exact permanent gate.

### Functional metrics

- Login, refresh and permission-denied changes after deployment.
- Pancake token resolution, decryption, mint-lock, polling and send failures.
- Per-platform polling failure event 5011 and page-token lock failure code `pancake_page_token_lock_failed`.
- Ambiguous inbox identity log event 6003.
- Labels/notes endpoint SQL errors.
- Startup failures from migration, repair or verification.

## Monitoring Tools

- `dbo.schema_migrations` for migration state.
- `dbo.system_logs` for startup, API and job errors.
- SQL Server catalog views used by the verification script.
- Existing health checks and application logs.

## Logging Strategy

- Log counts, object names and safe error codes only.
- Never log raw or encrypted tokens, encryption keys, SQL passwords, email addresses or customer content.
- Keep migration errors specific enough for reconciliation without including row values.
- Use current system-log retention policy.

## Alerts

### Critical

- Migration rollback due to role mismatch or ambiguous role mapping.
- Any evidence of cross-tenant role or credential assignment.
- Canonical credential loss after a committed migration.

### High

- Any of the four legacy tables exists after startup verification.
- An active collaboration table is missing or malformed.
- Authenticated ciphertext cannot be decrypted with the configured key.
- Repeated ambiguous active inbox identity errors.
- Repeated page-token lock timeouts or per-inbox polling failures.
- Sudden increase in login/permission or Pancake authentication failures after deployment.

### Warning

- Table counts drift without a corresponding reviewed migration.
- Migration or repair lock duration exceeds the maintenance-window expectation.
- A required index is found disabled and rebuilt by repair.

## Incident Response

1. Stop deployment/startup when verification or migration fails.
2. Check the 0094 ledger row and the 15 schema flags.
3. Compare role-assignment and credential-presence counts with the pre-deploy snapshot.
4. For role conflicts, reconcile canonical Identity assignments manually; do not auto-copy stale legacy roles.
5. For key-drift errors, restore the correct encryption key or follow the approved key-rotation process; do not re-encrypt unknown ciphertext.
6. If migration did not commit, correct the conflict and rerun.
7. If migration committed and canonical data is wrong, restore/extract from the protected backup according to the deployment guide.
8. Record a post-mortem for any authorization regression, credential loss or channel interruption.

## Health Checks

- Four legacy tables absent.
- Three active collaboration tables present.
- Canonical inbox credential columns have exact nullable types.
- PK/FK mappings are enabled and trusted.
- Required indexes are enabled and have exact key/filter definitions.
- Login, permission resolution, Pancake token resolve/send, labels and notes smoke tests pass.

## Baseline

The verified local baseline is `111111111111111|91|102`. Future table-count changes are acceptable only when the 15 object-contract flags remain valid and the new schema is covered by a reviewed migration.
