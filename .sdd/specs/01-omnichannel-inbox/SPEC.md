# SPEC-01 — Omnichannel Inbox

Status: `DRAFT`
Traces to: FR-01, UC-A01..A10, SW-011..022

## 1. Business Context

5 sales channels (Zalo, Facebook, TikTok, Instagram, YouTube) currently produce siloed inboxes. ~20% messages missed; average response time 15–60 min. Unified inbox + auto-classification eliminates miss and enables `<2 min` first reply.

## 2. User Stories

- AS A Sale Agent I WANT every DM and "purchase-intent" comment from 5 channels in one queue SO THAT I never miss a hot lead.
- AS A PM I WANT priority queue (hot leads first) SO THAT my team handles deals in the order that matters.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL ingest DMs and comments from Zalo OA, Facebook Graph, TikTok Business, Instagram, YouTube Data APIs.
- WHEN a webhook payload arrives THE SYSTEM SHALL verify HMAC signature before persisting.
- WHEN `messages.sent_at` is outside business hours (06:00–22:00 GMT+7) AND `conversations.status = 'open'` THE SYSTEM SHALL auto-reply per `chat_scenarios` group `out-of-hours` within 5 seconds.
- WHEN a contact identified by `(platform, external_id)` already exists in `contact_external_ids` THE SYSTEM SHALL merge the new conversation into the existing `contact_id`.
- WHILE `conversations.status = 'open'` THE SYSTEM SHALL surface conversations with `last_msg_at > NOW() - INTERVAL '10 min'` to the assigned sale's inbox.
- IF webhook signature verification fails THEN THE SYSTEM SHALL respond 401 and record the attempt in `audit_logs`.

## 4. API Contracts / Data Models

- `POST /webhooks/{platform}` — per channel (existing `WebhookEndpoints.cs`).
- `GET /api/inbox?status=&platform=&assigned_to=` — unified list (new `InboxEndpoints.cs`).
- `POST /api/conversations/{id}/assign` — assign to user.
- Tables: `conversations`, `messages`, `contacts`, `contact_external_ids`.

## 5. Technical Constraints

- Each channel adapter implements `IChannelAdapter` (`Clawbot.SharedKernel.Channels`).
- Inbound webhook → MassTransit queue `inbox.ingest` → handler in `Application.Modules.Conversations`.
- Latency budget: webhook ACK <100 ms (queue enqueue only).

## 6. Out of Scope

- Voice / video messages (Phase 2).
- Cross-platform user identity stitching beyond `external_id` (future ML model).

## 7. Non-Functional Requirements

NFR-01 (p95 API), NFR-02 (5-channel uptime), NFR-04 (≥500 concurrent chats).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Channel API 5xx | adapter `VerifyWebhookSignatureAsync` returns false / HTTP retry policy | Sale sees stale data badge | Polly retry with backoff; alert if >3 min |
| Duplicate webhook | DB unique `(tenant_id, platform, external_thread_id)` | none | upsert ignored |
| Out-of-hours auto-reply spam | Throttle per `contact_id` >1/hr | quiet | none |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| YT Data API quota strategy | P1 | T6 | open |
| TikTok Business permissions for DM | P1 | T6 | open |
