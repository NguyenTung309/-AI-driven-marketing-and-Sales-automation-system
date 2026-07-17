---
phase: implementation
title: Failed Message Retry Implementation Guide
description: Coding notes for atomic retry and per-message UI state
---

# Failed Message Retry Implementation Guide

## Development Setup

- Use the existing local database and service configuration; no migration or package is required.
- Keep existing uncommitted work intact and limit edits to retry/realtime/UI/test files.

## Code Structure

- Backend orchestration remains adjacent to `ApproveDraftAsync` in `InboxEndpoints` with focused private helpers if needed.
- Shared realtime event belongs in `Clawbot.SharedKernel.Inbox`.
- UI API types/functions stay in `shared/api/inbox.ts`; mutation ownership stays in `ConversationsPage`.

## Implementation Notes

- Never accept retry content from the browser.
- Never call `Conversation.AppendMessage` or an LLM.
- Use conditional `ExecuteUpdateAsync` for the claim and reload the message after detaching stale tracking.
- Commit claim + explicit audit before external I/O.
- Call `IChannelAdapter.SendAsync` once; keep HTTP retry disabled.
- Use immutable `Set`/record updates for frontend pending/error state.

## Integration Points

- `OutboundMessageSafetyService` checks current persisted content.
- `PancakeChannelAdapter` resolves tenant-owned page token from conversation external thread ID.
- SignalR event name: `messageStatus`.

## Error Handling

- Invalid/stale eligibility: `409`.
- Explicit channel failure: persist `send_failed`, emit status, return safe channel error.
- Cancellation: persist `send_failed`, emit status with `CancellationToken.None`, then rethrow.
- Post-provider DB failure: do not restore `send_failed`; leave durable `pending_send`.

## Performance Considerations

- Claim is a single conditional update on message ID/conversation/status.
- Realtime patches one cached message and invalidates one exact detail query.

## Security Notes

- Require `conversations:write`, tenant ownership, and inbox membership.
- Log/audit no message content, credentials, or provider response bodies.
- Rows with an existing external ID are not retryable.
