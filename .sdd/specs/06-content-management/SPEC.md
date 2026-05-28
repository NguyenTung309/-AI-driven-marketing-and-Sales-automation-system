# SPEC-06 — Content Management

Status: `DRAFT`
Traces to: FR-06, UC-E01..E10, UC-K01..K03, SW-069..078

## 1. Business Context

Tự sinh 40+ bài/tuần đa nền tảng. Trend tiếng Trung VN. Approve → schedule → publish.

## 2. User Stories

- AS A MKT I WANT trend list mỗi thứ 2 SO THAT tôi approve và team produce.
- AS A MKT I WANT content calendar 5 kênh 1 chỗ.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL run `Agent-Research` every Monday 07:00 GMT+7 to scrape TikTok/YT trends.
- WHEN approved THE SYSTEM SHALL schedule via `content_schedule` per platform's golden hour.
- THE SYSTEM SHALL store post receipt URL in `content_schedule.post_url`.
- WHILE `status = 'pending'` content cannot be posted; MKT must `approve` first.
- IF Buffer/Later schedule fails THEN THE SYSTEM SHALL mark `status = 'failed'` and alert.

## 4. API Contracts / Data Models

- `POST /api/content/briefs` / `GET /api/content/queue`
- `POST /api/content/items/{id}/approve|reject`
- `GET /api/content/calendar?from=&to=`
- Tables: `content_briefs`, `content_items`, `content_schedule`.

## 5. Technical Constraints

- Posting via Buffer/Later API or direct platform API per channel.

## 6. Out of Scope

- Video generation / editing (Phase 2).

## 7. NFR

NFR-04 throughput, NFR-01 list p95 <200 ms.

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Buffer rate-limit | 429 | "delayed" status | retry next hour |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Buffer free tier limits | P2 | T8 | open |
