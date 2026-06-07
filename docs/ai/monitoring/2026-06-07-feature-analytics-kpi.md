---
phase: monitoring
title: Analytics KPI + Metabase Monitoring
description: Operational checks for M20 analytics, forecasts, anomalies, and stale KPI data
---

# Analytics KPI + Metabase Monitoring

## Signals

- SignalR alert for CPL anomalies.
- SignalR alert for lead/conversion volume drops.
- SignalR alert when latest KPI data is older than 36 hours.
- Hangfire dashboard status for rollup, anomaly, and forecast jobs.

## Health Checks

- `kpi_daily` has rows for each supported platform plus `all` after the daily rollup.
- `kpi_forecast` has fresh generated rows for configured metrics and platforms.
- Analytics endpoints return within the p95 target using cached forecast rows.
- Metabase dashboard can query analytics data through the read-only role.

## Runbook Notes

- If rollup fails, inspect `audit_logs`; Hangfire retry should run the next scheduled retry attempt.
- If forecasts are stale, rerun `ForecastPrecomputeJob`.
- If Metabase cannot read data, validate the read-only role and datasource credentials.

