# SPEC-05 — Lead & CRM

Status: `DRAFT`
Traces to: FR-05, UC-D01..D10, UC-F01..F08, UC-G01..G08, SW-057..068

## 1. Business Context

5-channel lead routing với weighted scoring. Phân loại Nóng ≥70 / Ấm 30–70 / Lạnh <30. Drip + nurture + remarketing.

## 2. User Stories

- AS A PM I WANT scoring rules editable per channel SO THAT I can rebalance weights when channel quality shifts.
- AS A Sale I WANT hot leads pushed to me <2 min SO THAT I close while warm.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL compute lead score from `lead_scoring_rules` weighted by `(platform, event_code)`.
- WHEN `leads.score >= 70` THE SYSTEM SHALL `assign` to an active sale + Telegram alert within 2 minutes.
- THE SYSTEM SHALL classify stage: `score >= 70 = hot`, `30..70 = warm`, `<30 = cold`.
- WHEN a lead with `score >= 70` has no `lead_activities` in 24h THE SYSTEM SHALL reassign to another sale.
- WHILE `stage = 'cold'` THE SYSTEM SHALL execute drip sequence per platform (Zalo 7-day, FB 5-day).
- WHEN a lead becomes `customer` THE SYSTEM SHALL trigger welcome sequence + onboarding kit (FR-07).

## 4. API Contracts / Data Models

- `GET /api/leads?stage=&platform=` — list
- `POST /api/leads/{id}/assign` — assign sale
- `GET /api/leads/scoring-rules`, `PUT /api/leads/scoring-rules/{id}`
- Tables: `leads`, `lead_scoring_rules`, `lead_activities`, `contacts`.

## 5. Technical Constraints

- Scoring re-evaluated on every `lead_activities` insert via DB trigger or app handler.
- Drip jobs in MassTransit with delayed messages.

## 6. Out of Scope

- Phone-call CTI integration.

## 7. NFR

NFR-01 (assign p95 <2s), NFR-04 (≥500 leads/min scoring).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Sale offline | absence in last 15 min | round-robin to next | auto |
| Telegram failure | bot lib exception | banner | email fallback |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Round-robin vs skill-based assign | P5 | T7 | open |
