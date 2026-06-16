---
phase: deployment
title: Analytics KPI + Metabase Deployment
description: Deployment notes for M20 analytics and BI infrastructure
---

# Analytics KPI + Metabase Deployment

## Database

- Apply the new `kpi_forecast` migration after coordinating with M19 migrations `0003` through `0005`.
- Apply the Metabase read-only SQL role script in development before connecting the dashboard.

## Services

- Start Metabase and its metadata Postgres from `deploy/docker-compose.yml`.
- Configure `METABASE_PASSWORD` from `deploy/.env.example`.

## Jobs

- Verify Hangfire schedules for daily KPI rollup at 00:30 GMT+7, anomaly alerts, and forecast precompute.

## Validation

- `dotnet build Clawbot.sln`
- `dotnet test Clawbot.sln`
- `docker compose up metabase postgres` from `deploy`

