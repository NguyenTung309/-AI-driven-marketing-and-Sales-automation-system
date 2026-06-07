---
phase: planning
title: Analytics KPI + Metabase Plan
description: M20 implementation checklist for omnichannel KPI, forecasting, anomalies, exports, and BI
---

# Analytics KPI + Metabase Plan

## Milestones

- [ ] Phase A: Per-platform rollup, shared aggregator, forecast storage, and ReportAgent daily snapshot
- [ ] Phase B: Analytics skills and ReportAgent forecast/anomaly RPCs
- [ ] Phase C: Analytics API endpoints, DTOs, read services, and exports
- [ ] Phase D: Anomaly alert and forecast precompute jobs
- [ ] Phase E: Metabase infrastructure assets
- [ ] Phase F: Focused tests and verification

## Task Breakdown

### Phase A: Per-platform rollup + aggregator + ReportAgent

- [x] Add `IKpiAggregator` and `KpiAggregator` for tenant/date/platform KPI aggregation.
- [x] Compute `avg_response_time_sec` from inbound messages to first outbound replies.
- [x] Refactor `DailyKpiRollupJob` to upsert each platform plus `all`.
- [x] Align the rollup cron to 00:30 GMT+7 and keep retry/audit behavior.
- [x] Add `kpi_forecast` migration, domain entity, EF configuration, and `DbSet`.
- [x] Fill `ReportAgentGrpcService.DailySnapshot` with tenant/date validation and platform KPI rows.
- [x] Register `ReportAgent.ReportAgentClient` for API use.

### Phase B: Analytics skills

- [x] Add audit-clean MathNet and ML.NET package pins and references.
- [x] Implement rolling z-score anomaly detection.
- [x] Implement 7-day ML.NET SSA forecasting.
- [x] Extend `agent_report.proto` additively with `Forecast` and `DetectAnomaly` RPCs.
- [x] Implement the new ReportAgent RPCs over `kpi_daily`.

### Phase C: Analytics API

- [x] Add analytics DTOs in `Clawbot.Api.Contracts`.
- [x] Add `AnalyticsAggregationService` for read-side range aggregation.
- [x] Add `AnalyticsExportService` for CSV and QuestPDF export.
- [x] Add `AnalyticsEndpoints` and remove the 501 bounded-context stub.
- [x] Map endpoints in `Program.cs` and register services in DI.

### Phase D: Jobs

- [x] Add `AnomalyAlertJob` for CPL, lead/conversion volume drops, and stale KPI checks.
- [x] Add `ForecastPrecomputeJob` to generate/upsert cached forecasts.
- [x] Register queues and schedules in `HangfireModule`.

### Phase E: Metabase

- [x] Add Metabase and metadata Postgres services to compose.
- [x] Add `.env.example` entries.
- [x] Add read-only analytics SQL role script.
- [x] Add checked-in KPI dashboard JSON.

### Phase F: Tests and Verification

- [x] Add anomaly detector tests.
- [x] Add forecaster tests.
- [x] Add KPI aggregator tests.
- [x] Add analytics export tests.
- [x] Run `dotnet build Clawbot.sln`.
- [x] Run relevant tests or `dotnet test Clawbot.sln` if feasible.
- [x] Tick M20 in `docs/module-checklist.md` only after green verification.

## Dependencies

- M19 ads metrics may still be in user-edited work; ad spend can remain null where unavailable.
- ReportAgent gRPC remains the boundary for ML skills.
- QuestPDF renderer from M17 should be reused for PDF.
- SignalR notifier path should be reused for alerts.

## Risks and Mitigations

- New NuGet package pins can fail audit gates. Verify with build before reporting completion.
- Full solution tests may expose unrelated dirty-worktree failures. Record exact failures and keep M20 fixes scoped.
- Metabase dashboard loading may require manual service startup and credentials; static assets can still be validated in this phase.
