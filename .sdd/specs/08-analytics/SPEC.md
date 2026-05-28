# SPEC-08 — Analytics

Status: `DRAFT`
Traces to: FR-08, UC-I01..I08, SW-079..088

## 1. Business Context

KPI 5 kênh real-time + daily report 7h30 + weekly trend. Metabase BI.

## 2. User Stories

- AS A PM I WANT a single dashboard showing leads/CPL/conversion per channel SO THAT I rebalance budget weekly.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL aggregate per-day per-platform metrics into `kpi_daily` at 00:30 GMT+7 daily.
- WHEN CPL on any channel spikes >150% vs 7-day rolling avg THE SYSTEM SHALL Telegram-alert ops.
- THE SYSTEM SHALL expose Metabase data source backed by read-only Postgres role.
- THE SYSTEM SHALL provide `/api/analytics/omnichannel?from&to` for custom range queries.
- IF `kpi_daily` aggregation fails THEN THE SYSTEM SHALL record the failure in `audit_logs` and retry next hour.

## 4. API Contracts / Data Models

- `GET /api/analytics/omnichannel?from=&to=`
- `GET /api/analytics/funnel?platform=`
- Tables: `kpi_daily`, plus reads from `leads`, `conversations`, `messages`, `ads_actions`.

## 5. Technical Constraints

- Aggregation as cron job using `pg_cron` or app worker.
- Metabase pointed at read replica.

## 6. Out of Scope

- Predictive forecasting (Phase 2).

## 7. NFR

NFR-01 (analytics p95 <500 ms).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Aggregation lag | last `kpi_daily.created_at` >36h | banner "stale data" | retry job |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| pg_cron vs Hangfire | P1 | T10 | open |
