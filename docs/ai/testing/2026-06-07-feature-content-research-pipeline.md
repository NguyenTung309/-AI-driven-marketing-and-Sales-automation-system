---
phase: testing
title: Testing Strategy
description: Define testing approach, test cases, and quality assurance
---

# Testing Strategy

## Test Coverage Goals
**What level of testing do we aim for?**

- Unit test coverage target (default: 100% of new/changed code)
- Integration test scope (critical paths + error handling)
- End-to-end test scenarios (key user journeys)
- Alignment with requirements/design acceptance criteria

## Unit Tests
**What individual components need testing?**

### Domain Content Workflow
- [x] `ContentBrief.Update` changes platform/brief and preserves status while setting `UpdatedAt` — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentBrief.MarkStatus` changes status and `UpdatedAt` — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentItem.UpdateBody` changes body and `UpdatedAt` without changing draft status — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentItem.MarkScheduled` / `MarkPublished` advance status and `UpdatedAt` — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)

### Contract Shape
- [x] API content DTOs compile in `Clawbot.Api.Contracts` and cover brief/item/schedule/trend/calendar records — [ContentDtos.cs](../../../src/api/Clawbot.Api.Contracts/Content/ContentDtos.cs)
- [x] Additive `agent_content.proto` changes compile in `Clawbot.Agents.Contracts` — `dotnet build src/agents/Clawbot.Agents.Contracts/Clawbot.Agents.Contracts.csproj --no-restore` passed with 0 warnings.

### Content Generation Core
- [x] `ConfigPromptTemplateProvider` returns templates from configuration by platform, case-insensitive — [ContentPromptTemplateProviderTests.cs](../../../tests/Clawbot.Agents.Tests/Content/ContentPromptTemplateProviderTests.cs)
- [x] Missing or blank prompt template inputs fail clearly — [ContentPromptTemplateProviderTests.cs](../../../tests/Clawbot.Agents.Tests/Content/ContentPromptTemplateProviderTests.cs)
- [x] `ContentAgent.GenerateAsync` retrieves RAG context, renders configured template variables, calls `IContentLlmClient`, and returns trimmed draft metadata — [ContentAgentTests.cs](../../../tests/Clawbot.Agents.Tests/Content/ContentAgentTests.cs)
- [x] `ContentAgent.GenerateAsync` rejects blank briefs — [ContentAgentTests.cs](../../../tests/Clawbot.Agents.Tests/Content/ContentAgentTests.cs)
- [x] `ContentRepurposeMapper.NormalizeTargets` trims, lowercases, deduplicates, and rejects empty target sets — [ContentRepurposeMapperTests.cs](../../../tests/Clawbot.Agents.Tests/Content/ContentRepurposeMapperTests.cs)
- [x] `ContentItem.Approve` / `Reject(DateTimeOffset)` status and audit transitions — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentItem.SoftDelete(DateTimeOffset)` sets `DeletedAt` + `UpdatedAt`, preserves status — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentItem.SetAssets(json, at)` updates `AssetsJson` + `UpdatedAt` — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentItem.RevertToApproved(at)` resets status to "approved" — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentSchedule.RecordRetry(at)` increments count, stays pending before max — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `ContentSchedule.RecordRetry(at)` returns false and marks "failed" at `MaxRetries` — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)

### Research Trend Sources
- [x] Google Trends RSS parser reads topics and traffic metrics — [TrendSourceParserTests.cs](../../../tests/Clawbot.Agents.Tests/Research/TrendSourceParserTests.cs)
- [x] YouTube Data API JSON parser reads video titles, tags, and view counts — [TrendSourceParserTests.cs](../../../tests/Clawbot.Agents.Tests/Research/TrendSourceParserTests.cs)
- [x] HTML scrape parser extracts best-effort trend topics via AngleSharp — [TrendSourceParserTests.cs](../../../tests/Clawbot.Agents.Tests/Research/TrendSourceParserTests.cs)
- [x] `WeightedTrendScorer` ranks Chinese-learning keyword matches above generic source-volume trends — [ResearchAgentTests.cs](../../../tests/Clawbot.Agents.Tests/Research/ResearchAgentTests.cs)
- [x] `ResearchAgent` fans out enabled sources and skips failed/disabled sources — [ResearchAgentTests.cs](../../../tests/Clawbot.Agents.Tests/Research/ResearchAgentTests.cs)
- [x] `ContentTrendBriefFormatter` formats/parses stable weekly trend brief markers and uses GMT+7 ISO-week defaults — [ContentTrendBriefFormatterTests.cs](../../../tests/Clawbot.Infrastructure.Tests/Content/ContentTrendBriefFormatterTests.cs)
- [x] `WeeklyTrendScanJob` scans active tenants for the current GMT+7 week and emits content trend notifications — [WeeklyTrendScanJobTests.cs](../../../tests/Clawbot.Infrastructure.Tests/Jobs/WeeklyTrendScanJobTests.cs)
- [x] `HttpSocialPublisher` sends Buffer-shaped payloads with bearer auth and fails closed when endpoint/token are missing — [HttpSocialPublisherTests.cs](../../../tests/Clawbot.Infrastructure.Tests/Content/HttpSocialPublisherTests.cs)
- [x] `ContentSchedule.MarkPosted` / `MarkFailed(DateTimeOffset)` / `Cancel(DateTimeOffset)` update status and audit time — [ContentWorkflowTests.cs](../../../tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs)
- [x] `DefaultGoldenHourResolver` chooses the next per-platform GMT+7 publish hour — [GoldenHourResolverTests.cs](../../../tests/Clawbot.Infrastructure.Tests/Content/GoldenHourResolverTests.cs)
- [x] `ContentPublishJob` marks due publishes posted/published on success and failed/notified on publisher failure — [ContentPublishJobTests.cs](../../../tests/Clawbot.Infrastructure.Tests/Jobs/ContentPublishJobTests.cs)
- [x] Calendar rows join schedule metadata with content item body — [ContentCalendarTests.cs](../../../tests/Clawbot.Api.Tests/ContentCalendarTests.cs)
- [x] Schedule-time validation uses golden hour when omitted and rejects past manual times — [ContentScheduleValidationTests.cs](../../../tests/Clawbot.Api.Tests/ContentScheduleValidationTests.cs)
- [x] `ResolveScheduledAt` accepts valid future time and uses default golden hour for unknown platforms — [ContentScheduleValidationTests.cs](../../../tests/Clawbot.Api.Tests/ContentScheduleValidationTests.cs)
- [x] `BuildCalendarRows` excludes schedule when item missing, maps all status variants — [ContentCalendarTests.cs](../../../tests/Clawbot.Api.Tests/ContentCalendarTests.cs)

## Integration Tests
**How do we test component interactions?**

- [x] Direct gRPC service test project added: `tests/Clawbot.AgentService.Tests` references `Clawbot.AgentService`.
- [x] `ResearchAgentGrpcService.WeeklyTrends` uses SQLite-backed `AppDbContext` to verify KB keyword extraction and idempotent trend brief upsert.
- [x] `ContentAgentGrpcService.Generate` verifies generated drafts persist to `content_items` and response variants reflect saved rows.
- [x] `ContentAgentGrpcService.Repurpose` verifies target normalization creates one saved draft per distinct target.
- [ ] Full HTTP endpoint tests for `/api/content` are deferred to M21 with the repo integration-test pattern.

## End-to-End Tests
**What user flows need validation?**

- [ ] User flow 1: [Description]
- [ ] User flow 2: [Description]
- [ ] Critical path testing
- [ ] Regression of adjacent features

## Test Data
**What data do we use for testing?**

- AgentService tests use an open in-memory SQLite connection and the real `AppDbContext` model.
- NSubstitute supplies `IResearchAgent`, RAG, prompt-template, LLM, and clock seams.
- Domain factories seed `KbModule`, `ContentItem`, and related content rows; EF entry property assignment is used only for private-set fields that production endpoints also mutate through EF.
- No live LLM, trend-source, or publisher network calls run in tests.

## Test Reporting & Coverage
**How do we verify and communicate test results?**

- 2026-06-07: `dotnet test tests/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --no-restore` -> 24 tests passed, 0 warnings.
- 2026-06-07: `dotnet build src/agents/Clawbot.Agents.Contracts/Clawbot.Agents.Contracts.csproj --no-restore` -> 1 project, 0 errors, 0 warnings.
- 2026-06-07: `dotnet build src/api/Clawbot.Api.Contracts/Clawbot.Api.Contracts.csproj --no-restore` -> 1 project, 0 errors, 0 warnings.
- 2026-06-07: `dotnet build Clawbot.sln --no-restore` -> 16 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet restore Clawbot.sln` with `OpenAI` `2.11.0` failed NU1608 due existing `Microsoft.SemanticKernel.Connectors.OpenAI` exact dependency on `OpenAI (= 2.1.0-beta.1)`.
- 2026-06-07: `dotnet restore Clawbot.sln` after pinning `OpenAI` `2.1.0-beta.1` -> 16 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet build Clawbot.sln --no-restore` after adding the RFC/package pin -> 16 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` after prompt-template provider -> 68 tests passed, 0 warnings.
- 2026-06-07: `dotnet build src/agents/Clawbot.AgentService/Clawbot.AgentService.csproj --no-restore` after registering content module -> 7 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` after ContentAgent/OpenAI wrapper -> 70 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --no-restore` after adding `ContentItem.BriefId` factory support -> 25 tests passed, 0 warnings.
- 2026-06-07: `dotnet build src/agents/Clawbot.AgentService/Clawbot.AgentService.csproj --no-restore` after content gRPC persistence -> 7 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet build src/api/Clawbot.Api/Clawbot.Api.csproj --no-restore` after `ContentEndpoints` -> 8 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet build src/agents/Clawbot.AgentService/Clawbot.AgentService.csproj --no-restore` after content proto variant IDs -> 7 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --no-restore` -> 9 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --no-restore` -> 27 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` -> 72 tests passed, 0 warnings.
- 2026-06-07: `dotnet build Clawbot.sln --no-restore` Phase 2 checkpoint -> 16 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet restore Clawbot.sln` after adding AngleSharp 1.5.0 -> 16 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` after trend source parsers -> 75 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` after scorer/ResearchAgent -> 77 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore --filter ContentTrendBriefFormatterTests` -> 3 tests passed, 0 warnings.
- 2026-06-07: `dotnet build src/agents/Clawbot.AgentService/Clawbot.AgentService.csproj --no-restore` after implementing `ResearchAgentGrpcService.WeeklyTrends` -> 7 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore --filter WeeklyTrendScanJobTests` -> 1 test passed, 0 warnings.
- 2026-06-07: `dotnet build src/api/Clawbot.Api/Clawbot.Api.csproj --no-restore` after trend endpoints + dashboard hub group membership -> 8 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` after research gRPC/job/API wiring -> 77 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore` after research gRPC/job/API wiring -> 31 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --no-restore` after research gRPC/job/API wiring -> 9 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore --filter HttpSocialPublisherTests` -> 2 tests passed, 0 warnings.
- 2026-06-07: `dotnet build src/shared/Clawbot.Infrastructure/Clawbot.Infrastructure.csproj --no-restore` after publisher DI registration -> 6 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore` after publisher DI registration -> 33 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --no-restore --filter ContentSchedule_mark_posted_failed_and_canceled_update_status_and_audit_time` -> 1 test passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore --filter GoldenHourResolverTests` -> 2 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore --filter ContentPublishJobTests` -> 2 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --no-restore --filter ContentCalendarTests` -> 1 test passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --no-restore --filter ContentScheduleValidationTests` -> 2 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --no-restore` after scheduling/publish flow -> 28 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore` after scheduling/publish flow -> 37 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --no-restore` after scheduling/publish flow -> 12 tests passed, 0 warnings.
- 2026-06-07: `dotnet build src/agents/Clawbot.AgentService/Clawbot.AgentService.csproj --no-restore` after content generation telemetry logging -> 7 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet build Clawbot.sln --no-restore` final checkpoint -> 16 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj --no-restore` final checkpoint -> 77 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Domain.Tests/Clawbot.Domain.Tests.csproj --no-restore` final checkpoint -> 28 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --no-restore` final checkpoint -> 37 tests passed, 0 warnings.
- 2026-06-07: `dotnet test tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj --no-restore` final checkpoint -> 12 tests passed, 0 warnings.
- 2026-06-07: Post-CR session — added 9 tests for CR-3/CR-4/CR-5 domain methods + API validation:
  - Domain: `SoftDelete`, `SetAssets`, `RevertToApproved`, `RecordRetry` (retry + boundary) — 5 new tests in `ContentWorkflowTests.cs`
  - API: `ResolveScheduledAt` (valid future, unknown platform), `BuildCalendarRows` (missing item, status variants) — 4 new tests
- 2026-06-07: `dotnet test --no-build` post-CR final -> Domain 33, Application 1, Api 16, Agents 77, Infrastructure 38 = **165 total, 0 failed**
- 2026-06-07: `dotnet test tests/Clawbot.AgentService.Tests/Clawbot.AgentService.Tests.csproj` -> 3 tests passed, 0 warnings.
- 2026-06-07: `dotnet build Clawbot.sln` final M18 closeout -> 17 projects, 0 errors, 0 warnings.
- 2026-06-07: `dotnet test Clawbot.sln --no-build` final M18 closeout -> 168 tests passed, 0 warnings.
- Coverage commands and thresholds (`dotnet test --collect:"XPlat Code Coverage"`)
- Coverage gaps (files/functions below 100% and rationale)
- Links to test reports or dashboards
- Manual testing outcomes and sign-off

## Manual Testing
**What requires human validation?**

- UI/UX testing checklist (include accessibility)
- Browser/device compatibility
- Smoke tests after deployment

## Performance Testing
**How do we validate performance?**

- Load testing scenarios
- Stress testing approach
- Performance benchmarks

## Bug Tracking
**How do we manage issues?**

- Issue tracking process
- Bug severity levels
- Regression testing strategy
