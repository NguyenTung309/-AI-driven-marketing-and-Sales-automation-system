---
phase: planning
title: Failed Message Retry Plan
description: Implementation tasks and validation order
---

# Failed Message Retry Plan

## Milestones

- [ ] Backend atomic retry and audit complete.
- [ ] Dedicated realtime message-status flow complete.
- [ ] Conversations Retry UI complete.
- [ ] Automated and manual verification complete.

## Task Breakdown

### Phase 1: Backend

- [ ] Register retry route with `conversations:write`.
- [ ] Validate tenant, inbox scope, message ownership, sender/direction/status/external ID.
- [ ] Atomically claim `send_failed -> pending_send` and create `message:retry` audit record.
- [ ] Run safety, send persisted content once, and persist terminal status/external ID.
- [ ] Handle explicit failure/cancellation and ambiguous post-send DB failures safely.

### Phase 2: Realtime and frontend

- [ ] Add `InboxMessageStatusEvent` notifier contract and implementations.
- [ ] Patch matching message status in `useInboxRealtime` without list-preview changes.
- [ ] Add retry API client.
- [ ] Add per-message Retry button, keyed loading/error state, immutable cache update, and `retry:false`.
- [ ] Refresh failed draft approvals so Retry becomes visible.

### Phase 3: Tests and review

- [ ] Test success, exact content, same row, channel failure, invalid states, scope, and concurrent claim.
- [ ] Extend frontend source-contract tests and notifier tests.
- [ ] Run .NET tests/build and frontend lint/type/build.
- [ ] Run code/security review and verify with a test adapter; get confirmation before live Zalo resend.

## Dependencies

- Existing `ApproveDraftAsync`, `OutboundMessageSafetyService`, `IChannelAdapter`, `AuditLog`, `IInboxNotifier`, TanStack Query, and SignalR.
- SQL Server integration environment for compare-and-set concurrency coverage.

## Timeline & Estimates

- Backend + tests: 2–3 hours.
- Realtime + UI + tests: 1–2 hours.
- Verification/review: 1 hour.

## Risks & Mitigation

- Duplicate send: conditional claim and no automatic retry.
- Provider success/DB failure: retain `pending_send` for manual reconciliation.
- Stale browser state: `409`, dedicated realtime event, and authoritative refetch.
- Cross-tenant/inbox access: explicit predicates and existing resolver checks.
- Audit content leakage: store identifiers/status transition only.

## Resources Needed

- Existing local SQL Server, RabbitMQ, AgentService/API/Gateway/web stack.
- Test `IChannelAdapter` and SignalR notifier doubles.
