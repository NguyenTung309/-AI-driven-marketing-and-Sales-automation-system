---
phase: testing
title: Failed Message Retry Testing Strategy
description: Automated and manual validation for retry safety
---

# Failed Message Retry Testing Strategy

## Test Coverage Goals

- Cover every new status branch and authorization boundary.
- Exercise compare-and-set concurrency against SQL Server.
- Verify frontend visibility, keyed pending state, no automatic mutation retry, and realtime status patching.

## Unit Tests

### Delivery workflow

- [ ] Successful retry sends exact persisted content once and preserves message identity/content/time.
- [ ] Channel exception/cancellation restores `send_failed`.
- [ ] Safety rejection avoids adapter invocation.
- [ ] Ineligible direction/sender/status/external ID returns conflict.

### Realtime/UI contracts

- [ ] `messageStatus` targets tenant/inbox/assignee groups.
- [ ] API client uses retry route with no content body.
- [ ] Retry button condition and ID-keyed pending/error state are present.

## Integration Tests

- [ ] Two concurrent requests produce one claim/adapter call and one `409`.
- [ ] Missing permission `403`; foreign tenant/message `404`; outside inbox `403`.
- [ ] Audit entry records actor/resource/status transition without content.
- [ ] Success stores external ID and creates no new message row.

## End-to-End Tests

- [ ] Seed a failed AI message with a test adapter and click Retry.
- [ ] Observe `send_failed -> pending_send -> sent` in two browser sessions.
- [ ] Simulate failure and verify inline error plus retryable state.
- [ ] Verify no conversation list preview/timestamp regression.

## Test Data

- One tenant/inbox/conversation with `send_failed` AI message and no external ID.
- Foreign tenant and outside-inbox fixtures.
- Blocking adapter double for concurrency; throwing and successful adapter doubles.

## Test Reporting & Coverage

- Run targeted .NET filters plus full solution build.
- Run frontend lint/type/build.
- Record manual live-send result only after explicit authorization.

## Manual Testing

- Keyboard focus and accessible label/error announcement.
- Double-click protection and per-row loading behavior.
- Chrome responsive conversation surface; no Agent Hub action.

## Performance Testing

- Confirm one external call per successful claim and prompt `409` for the loser.

## Bug Tracking

- Block release for duplicate-send, cross-tenant/scope, or ambiguous-delivery regressions.

## Execution Results — 2026-07-16

- `FailedMessageRetryService` + endpoint/frontend contract tests: 26 passed.
- Pancake adapter classification + outbound boundary tests: 11 passed.
- SQL Server/Testcontainers concurrent retry test: 1 passed; one channel invocation, second request rejected.
- Frontend `tsc -b` + Vite production build: passed.
- Frontend ESLint: 0 errors; 3 unrelated pre-existing hook warnings outside retry files.
- Full `Clawbot.sln` build: 0 warnings, 0 errors.
- Code review blocking issue fixed: ambiguous transport/timeout outcomes now remain `pending_send`, while definitive provider rejection restores `send_failed`.
