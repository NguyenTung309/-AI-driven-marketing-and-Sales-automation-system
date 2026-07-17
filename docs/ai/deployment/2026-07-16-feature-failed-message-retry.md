---
phase: deployment
title: Failed Message Retry Deployment
description: Release and rollback steps for retry endpoint and UI
---

# Failed Message Retry Deployment

## Infrastructure

- Existing API, SignalR, SQL Server, Gateway, and web app; no new service.

## Deployment Pipeline

### Build Process

- `dotnet build Clawbot.sln --no-restore`
- Run targeted and integration test projects.
- Frontend formatter/lint/type/build.

### CI/CD Pipeline

- Existing CI gates remain unchanged; no migration gate is needed.

## Environment Configuration

- No new environment variables or secrets.
- Channel credentials remain tenant-owned and encrypted.

## Deployment Steps

1. Pass backend/frontend tests and code/security review.
2. Deploy SharedKernel/Infrastructure/API together because `IInboxNotifier` changes.
3. Deploy frontend after API supports the route/event.
4. Smoke test with a test adapter or non-production channel.
5. Confirm audit and realtime behavior.

## Database Migrations

- None.

## Secrets Management

- Retry logs/events/audits must not contain message content or channel tokens.

## Rollback Plan

- Roll back frontend first to hide Retry.
- Roll back API/SharedKernel/Infrastructure as a compatible unit.
- Existing message statuses remain valid; no data rollback is required.
