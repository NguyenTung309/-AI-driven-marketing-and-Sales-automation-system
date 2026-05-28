# SPEC-09 — Ads Automation

Status: `DRAFT`
Traces to: FR-09, UC-H01..H08, SW-094..096

## 1. Business Context

Meta Ads + TikTok Ads auto pause/scale theo ngưỡng CPL/CTR/frequency. Creative rotation. Budget alert.

## 2. User Stories

- AS A MKT I WANT bad adsets paused automatically SO THAT spend goes to winning creatives.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL sync `ads_campaigns` from Meta + TikTok every 4h.
- WHEN `ads_rules` evaluation true THE SYSTEM SHALL execute `action` (pause/scale_up/scale_down/alert) AND log in `ads_actions`.
- WHEN daily spend reaches 90% budget THE SYSTEM SHALL Telegram-alert.
- WHILE `ads_rules.is_active = false` THE SYSTEM SHALL skip the rule.
- IF Meta API rate-limits THEN THE SYSTEM SHALL backoff and retry within 1 hour.

## 4. API Contracts / Data Models

- `GET /api/ads/campaigns`
- `POST /api/ads/rules` / `GET` / `PATCH`
- gRPC: `AdsAgent.Evaluate`
- Tables: `ads_campaigns`, `ads_rules`, `ads_actions`.

## 5. Technical Constraints

- Per-rule idempotency via `(campaign_id, rule_id, executed_at::date)` to avoid double-action same day.

## 6. Out of Scope

- Google Ads.

## 7. NFR

NFR-02 sync uptime, NFR-01 dashboard p95 <500 ms.

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| API token expired | 401 from platform | banner "reauth" | refresh token job |
| Conflicting rules | dry-run preview | warn UI | last-wins documented |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| TikTok Business approval status | P2 | T10 | open |
