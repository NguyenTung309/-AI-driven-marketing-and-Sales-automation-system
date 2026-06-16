---
phase: implementation
title: Analytics KPI + Metabase Implementation
description: Execution notes and file-level deltas for M20
---

# Analytics KPI + Metabase Implementation

## Development Setup

- Worktree: `D:\Clawbot`
- Current branch at execution start: `feature-content-research-pipeline`
- Source plan: pasted M20 Analytics KPI + Metabase plan attached to `/execute-plan`
- Constraint: preserve existing dirty worktree changes for content/ads modules.

## Implementation Log

### 2026-06-07

- Created formal AI DevKit docs for `analytics-kpi` because they were missing at execute-plan start.
- Added `IKpiAggregator` and `KpiAggregator` for tenant/day KPI aggregation with GMT+7 day windows, supported platform rows, `all` totals, response-time samples, and optional M19 ad-spend joins.
- Refactored `DailyKpiRollupJob` to use `IKpiAggregator`, upsert all returned platform rows idempotently, use `IClock`, audit failures, and rely on Hangfire retry. Updated the daily rollup cron to `30 0 * * *`.
- Added cached forecast storage via `KpiForecast`, EF configuration, `AppDbContext.KpiForecasts`, and `deploy/migrations/0006_kpi_forecast.sql`.
- Implemented `ReportAgentGrpcService.DailySnapshot` over `kpi_daily` with GUID/date validation and added the API `ReportAgentClient` registration.
- Added stable NuGet pins for `MathNet.Numerics` 5.0.0, `Microsoft.ML` 5.0.0, and `Microsoft.ML.TimeSeries` 5.0.0 scoped to `Clawbot.Agents.Core`.
- Implemented `ZScoreAnomalyDetector` with MathNet rolling z-score over prior values and `MlNetForecaster` with ML.NET SSA plus deterministic fallback for short/invalid histories.
- Extended `agent_report.proto` with additive `Forecast` and `DetectAnomaly` RPCs and implemented them in `ReportAgentGrpcService` over `kpi_daily` metric series.
- Added analytics DTOs, range aggregation, CSV/PDF export helpers, and real `/api/analytics` endpoints for omnichannel, funnel, agent performance, anomalies, forecast, and export. Removed the bounded-context 501 stub for analytics.
- Added `AnomalyAlertJob` for stale KPI, CPL spike, and leads/conversions drop SignalR alerts, plus `ForecastPrecomputeJob` for daily ReportAgent forecasts into `kpi_forecast`. Registered both in Hangfire.
- Added Metabase deploy assets: metadata Postgres + Metabase compose services, env sample password, SQL Server read-only role script, and KPI dashboard descriptor JSON.
- Fixed code-review bugs: daily rollup now targets the previous completed GMT+7 day, API no longer registers Agents.Core skills directly, anomaly alerts call ReportAgent for z-score scoring, cached forecasts older than 24 hours are excluded, production KPI aggregation pushes date filters into SQL outside SQLite tests, and PDF export now uses QuestPDF.

## Files Changed

- `docs/ai/requirements/2026-06-07-feature-analytics-kpi.md`
- `docs/ai/design/2026-06-07-feature-analytics-kpi.md`
- `docs/ai/planning/2026-06-07-feature-analytics-kpi.md`
- `docs/ai/implementation/2026-06-07-feature-analytics-kpi.md`
- `docs/ai/testing/2026-06-07-feature-analytics-kpi.md`
- `docs/ai/deployment/2026-06-07-feature-analytics-kpi.md`
- `docs/ai/monitoring/2026-06-07-feature-analytics-kpi.md`
- `src/shared/Clawbot.Infrastructure/Analytics/IKpiAggregator.cs`
- `src/shared/Clawbot.Infrastructure/Analytics/KpiAggregator.cs`
- `src/shared/Clawbot.Infrastructure/DependencyInjection.cs`
- `src/shared/Clawbot.Infrastructure/Jobs/DailyKpiRollupJob.cs`
- `src/shared/Clawbot.Infrastructure/Jobs/HangfireModule.cs`
- `src/shared/Clawbot.Domain/Analytics/KpiForecast.cs`
- `src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs`
- `src/shared/Clawbot.Infrastructure/Persistence/Configurations/DomainModelConfigurations.cs`
- `deploy/migrations/0006_kpi_forecast.sql`
- `src/agents/Clawbot.AgentService/Services/ReportAgentGrpcService.cs`
- `src/api/Clawbot.Api/Program.cs`
- `tests/Clawbot.Infrastructure.Tests/Analytics/KpiAggregatorTests.cs`
- `tests/Clawbot.Infrastructure.Tests/Analytics/KpiForecastPersistenceTests.cs`
- `tests/Clawbot.Infrastructure.Tests/Jobs/HangfireModuleTests.cs`
- `tests/Clawbot.AgentService.Tests/Services/ReportAgentGrpcServiceTests.cs`
- `Directory.Packages.props`
- `src/agents/Clawbot.Agents.Core/Clawbot.Agents.Core.csproj`
- `src/agents/Clawbot.Agents.Core/Skills/Ops/IAnomalyDetector.cs`
- `src/agents/Clawbot.Agents.Core/Skills/Ops/IForecaster.cs`
- `proto/agent_report.proto`
- `tests/Clawbot.Agents.Tests/Skills/OpsSkillTests.cs`
- `src/api/Clawbot.Api.Contracts/Analytics/AnalyticsDtos.cs`
- `src/api/Clawbot.Api/Services/AnalyticsAggregationService.cs`
- `src/api/Clawbot.Api/Services/AnalyticsExportService.cs`
- `src/api/Clawbot.Api/Endpoints/AnalyticsEndpoints.cs`
- `src/api/Clawbot.Api/Endpoints/BoundedContextEndpoints.cs`
- `tests/Clawbot.Api.Tests/AnalyticsAggregationTests.cs`
- `tests/Clawbot.Api.Tests/AnalyticsExportTests.cs`
- `src/shared/Clawbot.SharedKernel/Content/IContentNotifier.cs`
- `src/api/Clawbot.Api/Hubs/SignalRContentNotifier.cs`
- `src/shared/Clawbot.Infrastructure/Jobs/AnomalyAlertJob.cs`
- `src/shared/Clawbot.Infrastructure/Jobs/ForecastPrecomputeJob.cs`
- `deploy/docker-compose.yml`
- `deploy/.env.example`
- `deploy/sql/metabase-readonly.sql`
- `deploy/metabase/clawbot-kpi-dashboard.json`
- `src/api/Clawbot.Api/Clawbot.Api.csproj`
- `tests/Clawbot.Api.Tests/AnalyticsBoundaryTests.cs`

## Decisions and Deviations

- Staying in the current worktree because the repository contains many user changes and no analytics feature branch/worktree exists.
- Treating the pasted plan as source of truth and using these docs for ongoing execution tracking.
- SQLite tests cannot translate `DateTimeOffset` range comparisons or decimal sums used by the analytics queries, so `KpiAggregator` narrows by tenant/date-capable joins first and performs day-window and spend aggregation in memory where needed.

## Edge Cases

- Feature lint may continue to flag a missing `feature-analytics-kpi` branch even after docs exist. No branch switch is performed during this execution without explicit user direction.
- Response-time average ignores inbound messages without a later outbound reply in the same analytics day.
- ReportAgent metric series support `leads`, `dms`, `replies`, `conversions`, `avg_response_time_sec`, `ad_spend`, `cpl`, and alias `response_time`.
- The 00:30 rollup schedule processes yesterday's GMT+7 KPI window, not the partially-started current day.
