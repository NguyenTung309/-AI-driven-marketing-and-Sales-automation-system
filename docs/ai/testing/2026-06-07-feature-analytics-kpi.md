---
phase: testing
title: Analytics KPI + Metabase Testing
description: Test strategy and execution notes for M20
---

# Analytics KPI + Metabase Testing

## Test Coverage Goals

- Cover new aggregation, anomaly, forecast, and export behavior with focused unit tests.
- Prefer public behavior tests over implementation details.
- Keep gRPC/HTTP integration coverage deferred to M21 per the plan.

## Unit Tests

### KPI Aggregator

- [x] Aggregates leads, DMs, replies, conversions, and response time per platform. Covered by `tests/Clawbot.Infrastructure.Tests/Analytics/KpiAggregatorTests.cs`; verified with `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~KpiAggregatorTests --no-restore`.
- [x] Produces an `all` aggregate that sums platform totals. Covered by `tests/Clawbot.Infrastructure.Tests/Analytics/KpiAggregatorTests.cs`; verified with `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~KpiAggregatorTests --no-restore`.
- [x] Rollup writes per-platform rows idempotently. Covered by `tests/Clawbot.Infrastructure.Tests/Analytics/KpiAggregatorTests.cs`; verified with `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~KpiAggregatorTests --no-restore`.
- [x] Rollup at 00:30 GMT+7 writes the previous completed day. Covered by `tests/Clawbot.Infrastructure.Tests/Analytics/KpiAggregatorTests.cs`; verified with `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~Rollup_job_writes_platform_rows_idempotently --no-restore`.

### Anomaly Detector

- [x] Flags an injected spike above threshold. Covered by `tests/Clawbot.Agents.Tests/Skills/OpsSkillTests.cs`; verified with `dotnet test tests\Clawbot.Agents.Tests\Clawbot.Agents.Tests.csproj --filter "FullyQualifiedName~ZScoreAnomalyDetectorTests|FullyQualifiedName~MlNetForecasterTests" --no-restore`.
- [x] Ignores normal noise below threshold. Covered by `tests/Clawbot.Agents.Tests/Skills/OpsSkillTests.cs`; verified with `dotnet test tests\Clawbot.Agents.Tests\Clawbot.Agents.Tests.csproj --filter "FullyQualifiedName~ZScoreAnomalyDetectorTests|FullyQualifiedName~MlNetForecasterTests" --no-restore`.
- [x] Handles short or zero-variance series without false positives. Covered by `tests/Clawbot.Agents.Tests/Skills/OpsSkillTests.cs`; verified with `dotnet test tests\Clawbot.Agents.Tests\Clawbot.Agents.Tests.csproj --filter "FullyQualifiedName~ZScoreAnomalyDetectorTests|FullyQualifiedName~MlNetForecasterTests" --no-restore`.

### Forecaster

- [x] Returns the requested horizon length. Covered by `tests/Clawbot.Agents.Tests/Skills/OpsSkillTests.cs`; verified with `dotnet test tests\Clawbot.Agents.Tests\Clawbot.Agents.Tests.csproj --filter "FullyQualifiedName~ZScoreAnomalyDetectorTests|FullyQualifiedName~MlNetForecasterTests" --no-restore`.
- [x] Returns lower bounds less than or equal to values and values less than or equal to upper bounds. Covered by `tests/Clawbot.Agents.Tests/Skills/OpsSkillTests.cs`; verified with `dotnet test tests\Clawbot.Agents.Tests\Clawbot.Agents.Tests.csproj --filter "FullyQualifiedName~ZScoreAnomalyDetectorTests|FullyQualifiedName~MlNetForecasterTests" --no-restore`.

### Analytics Export

- [x] CSV export includes stable header and rows. Covered by `tests/Clawbot.Api.Tests/AnalyticsExportTests.cs`; verified with `dotnet test tests\Clawbot.Api.Tests\Clawbot.Api.Tests.csproj --filter "FullyQualifiedName~AnalyticsExportTests|FullyQualifiedName~AnalyticsAggregationTests" --no-restore`.
- [x] Unsupported export format is rejected. Covered by `tests/Clawbot.Api.Tests/AnalyticsExportTests.cs`; verified with `dotnet test tests\Clawbot.Api.Tests\Clawbot.Api.Tests.csproj --filter "FullyQualifiedName~AnalyticsExportTests|FullyQualifiedName~AnalyticsAggregationTests" --no-restore`.
- [x] PDF export generates a non-trivial QuestPDF document. Covered by `tests/Clawbot.Api.Tests/AnalyticsExportTests.cs`; verified with `dotnet test tests\Clawbot.Api.Tests\Clawbot.Api.Tests.csproj --filter "FullyQualifiedName~AnalyticsBoundaryTests|FullyQualifiedName~IsFreshForecast|FullyQualifiedName~BuildPdf_returns_generated_pdf_document"`.

### Forecast Storage

- [x] `KpiForecast` persists metric values and bounds through EF. Covered by `tests/Clawbot.Infrastructure.Tests/Analytics/KpiForecastPersistenceTests.cs`; verified with `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~KpiForecastPersistenceTests --no-restore`.

## Integration Tests

- [x] ReportAgent daily snapshot reads `kpi_daily` per platform. Covered by `tests/Clawbot.AgentService.Tests/Services/ReportAgentGrpcServiceTests.cs`; verified with `dotnet test tests\Clawbot.AgentService.Tests\Clawbot.AgentService.Tests.csproj --filter FullyQualifiedName~ReportAgentGrpcServiceTests --no-restore`.
- [x] ReportAgent anomaly RPC loads a metric series from `kpi_daily` and returns detector points. Covered by `tests/Clawbot.AgentService.Tests/Services/ReportAgentGrpcServiceTests.cs`; verified with `dotnet test tests\Clawbot.AgentService.Tests\Clawbot.AgentService.Tests.csproj --filter FullyQualifiedName~ReportAgentGrpcServiceTests`.
- [x] ReportAgent forecast RPC loads a metric series from `kpi_daily` and returns forecast points. Covered by `tests/Clawbot.AgentService.Tests/Services/ReportAgentGrpcServiceTests.cs`; verified with `dotnet test tests\Clawbot.AgentService.Tests\Clawbot.AgentService.Tests.csproj --filter FullyQualifiedName~ReportAgentGrpcServiceTests`.
- [ ] Analytics HTTP endpoints are tenant-scoped and mapped. Deferred to M21 if API integration harness is unavailable.
- [x] Analytics range aggregation groups platform totals and CPL. Covered by `tests/Clawbot.Api.Tests/AnalyticsAggregationTests.cs`; verified with `dotnet test tests\Clawbot.Api.Tests\Clawbot.Api.Tests.csproj --filter "FullyQualifiedName~AnalyticsExportTests|FullyQualifiedName~AnalyticsAggregationTests" --no-restore`.
- [x] API host does not register Agents.Core skills directly. Covered by `tests/Clawbot.Api.Tests/AnalyticsBoundaryTests.cs`; verified with `dotnet test tests\Clawbot.Api.Tests\Clawbot.Api.Tests.csproj --filter "FullyQualifiedName~AnalyticsBoundaryTests|FullyQualifiedName~IsFreshForecast|FullyQualifiedName~BuildPdf_returns_generated_pdf_document"`.
- [x] Forecast freshness rejects cached rows older than 24 hours. Covered by `tests/Clawbot.Api.Tests/AnalyticsAggregationTests.cs`; verified with `dotnet test tests\Clawbot.Api.Tests\Clawbot.Api.Tests.csproj --filter "FullyQualifiedName~AnalyticsBoundaryTests|FullyQualifiedName~IsFreshForecast|FullyQualifiedName~BuildPdf_returns_generated_pdf_document"`.

## Verification Commands

- [x] `dotnet build Clawbot.sln` passed after bug fixes: 17 projects, 0 errors, 0 warnings.
- [x] `dotnet test Clawbot.sln --no-build` passed after bug fixes: 212 tests, 0 warnings across 6 projects.
- [x] `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~HangfireModuleTests --no-restore` passed for the 00:30 rollup cron.
- [x] `dotnet test tests\Clawbot.Infrastructure.Tests\Clawbot.Infrastructure.Tests.csproj --filter FullyQualifiedName~HangfireModuleTests --no-restore` passed for anomaly alert and forecast precompute schedules.

## Current Gaps

- `docker compose -f deploy\docker-compose.yml config` could not run because `docker` is not installed on PATH in this environment.
- `deploy/metabase/clawbot-kpi-dashboard.json` validated with PowerShell `ConvertFrom-Json`.
- `npx ai-devkit@latest lint --feature analytics-kpi` passed with one warning: no dedicated `feature-analytics-kpi` worktree is registered.
