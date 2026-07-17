---
phase: requirements
title: Failed Message Retry Requirements
description: Allow authorized users to safely resend persisted outbound AI messages that failed channel delivery
---

# Failed Message Retry Requirements

## Problem Statement

AI can generate and persist a reply while Zalo/Pancake delivery fails. The Conversations UI only displays `Gửi thất bại`; sales/support users cannot resend the existing message without waiting for another inbound message or manually copying text.

## Goals & Objectives

- Add a per-message `Gửi lại` action for outbound AI messages with `send_failed` status.
- Resend the exact current persisted `Message.Content`; never invoke an LLM or create another message row.
- Prevent duplicate sends from double-clicks or concurrent browsers.
- Preserve tenant/inbox authorization, outbound safety, auditability, and realtime status visibility.
- Return the same message to `send_failed` after an explicit channel failure.

### Non-goals

- Automatic/background channel retries.
- Retrying `pending_send` (ambiguous delivery), `sent`, blocked, inbound, or manually typed messages that were never persisted.
- Adding Retry to Agent Hub.
- Changing provider retry policy or database schema.

## User Stories & Use Cases

- As an authorized inbox user, I can retry a failed AI bubble so the customer receives the already-approved content.
- As a user, I see only the selected message enter `Đang gửi lại…` while other failed messages remain usable.
- As a second user/browser, I receive a conflict instead of causing a duplicate delivery after another request claims the message.
- As an auditor, I can identify who initiated the retry without storing message content in audit metadata.

## Success Criteria

- Retry is rendered only for outbound `agent`/`ai`/`bot` messages in `send_failed`.
- The adapter receives the persisted content exactly once per successful claim.
- The same message ID transitions `send_failed -> pending_send -> sent/send_failed`; content and original `SentAt` do not change.
- Concurrent retry requests produce one adapter invocation and at least one `409` response.
- Tenant, permission, and inbox scope violations are denied.
- Realtime updates patch only the matching message status, not conversation preview/timestamp.
- Targeted backend/frontend tests and full builds pass.

## Constraints & Assumptions

- `Message` has no rowversion; atomic status compare-and-set is required.
- `IChannelAdapter.SendAsync` has no idempotency key. Only explicit `send_failed` is retryable.
- `ExecuteUpdateAsync` bypasses the audit interceptor, so the claim needs an explicit audit record.
- Current frontend testing uses C# source-contract tests rather than React Testing Library.

## Questions & Open Items

- None for the initial implementation. A future delivery-attempt ledger/idempotency key is out of scope.
