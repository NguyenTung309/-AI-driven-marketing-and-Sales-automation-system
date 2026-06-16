---
phase: requirements
title: Analytics KPI + Metabase Requirements
description: M20 omnichannel analytics, forecasting, anomaly alerts, exports, and BI dashboard
---

# Analytics KPI + Metabase Requirements

## Problem Statement

Tenants need omnichannel visibility across Zalo, Facebook, Instagram, TikTok, and YouTube. The current scaffold writes only a daily `platform='all'` KPI row and the analytics API is still a 501 stub, so tenants cannot inspect per-channel funnel, agent performance, CPL anomalies, forecasted trends, or a BI dashboard.

## Goals

- Compute daily KPI rows per platform plus an `all` aggregate.
- Expose analytics API endpoints for omnichannel KPI, funnel, agent performance, anomaly detection, forecast, and CSV/PDF export.
- Provide CPL and volume-drop anomaly alerts via SignalR.
- Precompute configurable 7-day metric forecasts and serve them within the analytics p95 target.
- Add Metabase services, read-only SQL access, and dashboard assets for the KPI surface.

## Non-Goals

- Telegram alerts are deferred until a Telegram adapter exists.
- Live ad-spend population depends on M19 ads metrics; null ad spend is acceptable until that integration is ready.
- gRPC/HTTP integration tests are deferred to M21 per the standing repo posture.
- Read-replica setup is deferred; Metabase uses read-only credentials against the primary dev database.

## Success Criteria

- `kpi_daily` contains per-platform rows and an `all` aggregate after rollup.
- `/api/analytics/omnichannel`, `/funnel`, `/agent-performance`, `/anomalies`, `/forecast`, and `/export` are mapped and tenant-scoped.
- Forecast endpoint returns precomputed rows for a requested metric and horizon.
- Anomaly alert job covers CPL spikes, leads/conversions drops, and stale KPI data.
- Metabase compose services and dashboard artifacts are checked in.
- `dotnet build Clawbot.sln` and relevant tests pass with fresh output.

## Constraints and Assumptions

- API projects cannot reference `Clawbot.Agents.Core`; ML work remains behind ReportAgent gRPC.
- Build gates treat NuGet audit and code analysis warnings as errors, so new package versions must be pinned cleanly.
- Existing user changes for M18/M19 are present in the worktree and must be preserved.
- The pasted M20 plan is the source of truth because formal docs did not exist before this execution.

