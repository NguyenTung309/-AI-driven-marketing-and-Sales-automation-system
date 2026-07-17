---
phase: monitoring
title: Failed Message Retry Monitoring
description: Operational signals for manual channel retry
---

# Failed Message Retry Monitoring

## Key Metrics

### Performance Metrics

- Retry endpoint latency excluding channel provider latency.
- Channel send duration and compare-and-set conflict latency.

### Business Metrics

- Retry attempts, successful retries, and remaining failed messages.

### Error Metrics

- `message_retry_not_available` and `message_already_claimed` counts.
- Channel failure/cancellation rate.
- Rows remaining `pending_send` beyond the channel timeout window.

## Monitoring Tools

- Existing structured application logs, audit logs, SQL inspection, and SignalR client state.

## Logging Strategy

- Log tenant/conversation/message IDs, outcome, duration, and exception type.
- Never log content, access tokens, or provider response bodies.

## Alerts & Notifications

### Critical Alerts

- Duplicate provider delivery for one message ID → disable Retry and investigate.
- Cross-tenant/inbox authorization defect → block release.

### Warning Alerts

- Elevated retry channel failure rate.
- `pending_send` retry rows older than the reconciliation threshold.

## Dashboards

- Delivery status counts and retry outcome trend by channel/tenant.

## Incident Response

1. Confirm message status/audit actor.
2. Check whether the provider returned an external ID.
3. Never reset ambiguous `pending_send` to `send_failed` automatically.
4. Reconcile manually before another customer-facing send.

## Health Checks

- Existing API/Gateway/AgentService checks plus a non-mutating page-token/channel connectivity check.
