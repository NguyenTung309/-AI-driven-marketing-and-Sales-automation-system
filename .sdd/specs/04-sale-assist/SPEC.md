# SPEC-04 — Sale Assist

Status: `DRAFT`
Traces to: FR-04, UC-C01..C10, SW-047..056

## 1. Business Context

Mục tiêu: 1 sale chăm 3× khách hiện tại (60–90/ngày). Cần AI draft, context panel, quick reply, alert >5 phút.

## 2. User Stories

- AS A Sale I WANT an AI-drafted reply for every incoming message SO THAT I just review and send.
- AS A Sale I WANT a context sidebar (history + score + suggested next step) SO THAT I don't reload context every conversation.

## 3. Acceptance Criteria (EARS)

- WHEN a new inbound message arrives in an assigned conversation THE SYSTEM SHALL produce a draft via `Agent-SaleAssist` within 3 seconds.
- THE SYSTEM SHALL surface drafts in the Sale Assist UI with an Edit + Send button.
- WHEN a conversation has been waiting > 5 minutes without sale reply THE SYSTEM SHALL alert the assigned sale via SignalR + Telegram.
- WHILE drafting THE SYSTEM SHALL include lead score, last 5 messages, suggested next action.
- IF a sale used "discount" or off-brand terms detected by `Agent-SaleAssist` THEN THE SYSTEM SHALL warn the sale before send.

## 4. API Contracts / Data Models

- `GET /api/sale-assist/draft?conversationId=` — get AI draft + context
- `POST /api/sale-assist/send` — edit + send
- `GET /api/sale-assist/quick-replies` — template library
- gRPC: `SaleAssistAgent.Draft`
- Tables: `quick_reply_templates`, `messages`, `leads`, `agents`.

## 5. Technical Constraints

- Draft response streamed via SignalR `DashboardHub`.

## 6. Out of Scope

- Voice-to-text in Sale Assist (Phase 2).

## 7. NFR

NFR-01 (draft p95 <3s), NFR-02 (alert reliability ≥99.5%).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| LLM fail | gRPC error | "draft unavailable" banner | manual reply |
| Alert undelivered | retry queue | banner | manual escalation |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| UX: draft inline vs side-panel | P2 | T5 | open |
