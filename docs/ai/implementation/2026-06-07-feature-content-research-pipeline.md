---
phase: implementation
title: Implementation Guide
description: Technical implementation notes, patterns, and code guidelines
---

# Implementation Guide

## Development Setup
**How do we get started?**

- Prerequisites and dependencies
- Environment setup steps
- Configuration needed

## Code Structure
**How is the code organized?**

- Directory structure
- Module organization
- Naming conventions

## Implementation Notes
**Key technical details to remember:**

### Core Features
- Phase 1.1 domain behaviors landed in-place with no DDL changes:
  - `ContentBrief.Update(platform, brief, updatedAt)` and `MarkStatus(status, at)`.
  - `ContentItem.UpdateBody(body, at)`, `MarkScheduled(at)`, and `MarkPublished(at)`.
  - Existing create/approve/reject APIs remain in place for later endpoint work.
- Phase 1.2 content gRPC contract extended additively:
  - `ContentAgent.Repurpose(RepurposeRequest)` added.
  - `ContentResponse.variants` added using `ContentVariant(platform, title, body)`.
  - `agent_research.proto` reviewed; existing `WeeklyTrends` / `TrendItem` shape already matches Phase 1.
- Phase 1.3 API contracts added:
  - `Clawbot.Api.Contracts.Content` contains records for briefs, items, generation, queue, repurpose, schedule, calendar, and trends.
- Phase 1.4 checkpoint:
  - `dotnet build Clawbot.sln --no-restore` passed with 16 projects, 0 errors, 0 warnings.
- Phase 2.0 RFC/package decision:
  - Added [.sdd/rfcs/002-openai-compatible-content-llm.md](../../../.sdd/rfcs/002-openai-compatible-content-llm.md).
  - `OpenAI` pinned to `2.1.0-beta.1` because existing `Microsoft.SemanticKernel.Connectors.OpenAI` 1.22.0 requires that exact package version; newer `2.11.0` failed NU1608 under warnings-as-errors.
  - Restore and build pass with the compatible pin.
- Phase 2.1 prompt templates:
  - Added `IPromptTemplateProvider`, `ContentPromptTemplateOptions`, and `ConfigPromptTemplateProvider` under `Clawbot.Agents.Core.Content`.
  - Templates are loaded from `Content:PromptTemplates` configuration children; no default prompt text is embedded in code.
  - `AgentService` now calls `AddClawbotContent(builder.Configuration)`.
- Phase 2.2 content orchestrator:
  - Added `ContentAgent` with RAG retrieval, configured-template rendering for `{{brief}}` / `{{knowledge}}`, and `IContentLlmClient` completion.
  - Added `ContentLlmOptions` and `OpenAiCompatibleChatClient`; SDK types remain inside the wrapper.
- Phase 2.3 content gRPC persistence:
  - `ContentItem.Create` now accepts an optional `briefId` for generated drafts.
  - `ContentAgentGrpcService.Generate` validates tenant/channel/brief, calls `ContentAgent`, persists `content_items`, and returns one `ContentVariant`.
  - `ContentAgentGrpcService.Repurpose` loads the source item tenant-scoped, generates target-channel drafts, persists them, and returns all variants.
- Phase 2.4 content REST API:
  - Added `ContentEndpoints` with brief list/get/create/update/archive, item generate/queue/update/soft-delete/approve/reject/repurpose.
  - API errors from this group use `{ errorCode, message, requestId }`.
  - `Program.cs` registers `ContentAgent.ContentAgentClient` and maps `/api/content`; `/api/content` stub removed from bounded-context stubs.
  - Brief delete archives via `Status = "archived"` because `content_briefs` has no `deleted_at` column.
- Phase 2.5 tests/checkpoint:
  - Added `ContentRepurposeMapper` and tests for target normalization.
  - `ContentItem.Approve` now updates `UpdatedAt`; `Reject(DateTimeOffset)` added for audited reject transitions.
  - API, domain, agents tests pass; full solution build passes 0/0.
- Phase 3.1 trend sources:
  - Added `RawTrend`, `ITrendSource`, and source options under `Clawbot.Agents.Core.Research`.
  - Added `GoogleTrendsRssSource` (XDocument), `YouTubeDataApiSource` (System.Text.Json), and `TikTokScrapeSource` / `BaiduScrapeSource` via AngleSharp HTML parsing.
  - All sources have `Enabled` and `TimeoutSeconds`; YouTube and HTML sources gracefully return empty when disabled or unconfigured.
- Phase 3.2 research scoring:
  - Added `WeightedTrendScorer` using keyword overlap plus logarithmic source-volume score.
  - Added `ResearchAgent` fan-out over enabled `ITrendSource` instances with graceful skip on source exceptions.
- Phase 3.3 weekly trend persistence:
  - Added `ContentTrendBriefFormatter` in `Clawbot.SharedKernel.Content` to keep trend `content_briefs` parseable with a stable `[trend:{week}] {topic}` marker.
  - Added public `IResearchAgent` abstraction; `ResearchAgentGrpcService.WeeklyTrends` now validates tenant/week, loads active KB module keywords, runs the research scan for `VN`, and idempotently upserts weekly trend briefs by source/topic marker.
  - SignalR notification is emitted by API/job callers after gRPC scans, not directly from AgentService, because AgentService must not reference API hub infrastructure.
- Phase 3.4 trend scan API/job:
  - Added `IWeeklyTrendScanner` + `GrpcWeeklyTrendScanner` and `WeeklyTrendScanJob`; `HangfireModule` now includes queue `content` and schedules the job at Monday 00:00 UTC (Monday 07:00 GMT+7).
  - `Program.cs` already registers `ResearchAgent.ResearchAgentClient`; `ContentEndpoints` now exposes `GET /api/content/trends` and `POST /api/content/trends/scan`.
  - `DashboardHub` now joins/leaves tenant groups on connect/disconnect so `SignalRContentNotifier` group sends reach dashboard clients.
- Phase 3.5 publisher:
  - Added `ISocialPublisher`, `PublishRequest`, `PublishResult`, `PublisherOptions`, and `HttpSocialPublisher` under `Clawbot.Infrastructure.Content.Publishing`.
  - `HttpSocialPublisher` is Buffer-shaped: POST JSON with `profile_ids`, `text`, `scheduled_at`, `media`, and metadata; bearer token and endpoint come from `Content:Publisher`.
  - `AddInfrastructure` registers the publisher HttpClient with existing Polly retry, circuit breaker, and timeout policies.
- Phase 3.6 scheduling/publish:
  - Added `IGoldenHourResolver` + `DefaultGoldenHourResolver` in SharedKernel with GMT+7 per-platform defaults (`zalo`, `youtube`, `instagram`, `tiktok`, `facebook`) and Infrastructure DI registration.
  - `ContentSchedule.MarkPosted` now updates `UpdatedAt`; added audited `MarkFailed(DateTimeOffset)` and `Cancel(DateTimeOffset)`.
  - `ContentEndpoints` now supports `POST /api/content/items/{id}/schedule`, `GET /api/content/calendar`, and `DELETE /api/content/schedule/{id}`.
  - Added `ContentPublishJob`: every five minutes on the `content` queue, loads pending due schedules, publishes via `ISocialPublisher`, marks posted/published on success, marks failed and sends `IContentNotifier.NotifyPublishFailedAsync` on failure.
  - SQLite tests cannot translate `DateTimeOffset <=` for `content_schedule`; the job filters `pending` in SQL and applies the due-time comparison in memory before taking the 50-row batch.
- Phase 3.8 seed/config:
  - Added `deploy/seed/content-briefs.sql` with idempotent sample HSK content briefs for TikTok, Instagram, Facebook, YouTube, and Zalo.
  - Added `.env.example` keys for `Content:Llm`, `Content:Trends:YouTube`, TikTok/Baidu scrape URLs, and `Content:Publisher`.
- Phase 3.9 polish/checkpoint:
  - `ContentAgentGrpcService` now logs input tokens, output tokens, and latency for generated/repurposed drafts after persistence.
  - Added `tests/Clawbot.AgentService.Tests` with SQLite-backed direct gRPC service coverage for `ContentAgentGrpcService` and `ResearchAgentGrpcService`.
  - Filled deployment and monitoring feature docs, including the explicit non-goal for a generic LLM cost aggregator.
  - Final closeout verification: `dotnet build Clawbot.sln` -> 17 projects, 0 errors, 0 warnings; `dotnet test Clawbot.sln --no-build` -> 168 tests passed, 0 warnings.
  - M18 checklist ticks were updated after final build/test verification.

### Patterns & Best Practices
- Design patterns being used
- Code style guidelines
- Common utilities/helpers

## Integration Points
**How do pieces connect?**

- API integration details
- Database connections
- Third-party service setup

## Error Handling
**How do we handle failures?**

- Error handling strategy
- Logging approach
- Retry/fallback mechanisms

## Performance Considerations
**How do we keep it fast?**

- Optimization strategies
- Caching approach
- Query optimization
- Resource management

## Security Notes
**What security measures are in place?**

- Authentication/authorization
- Input validation
- Data encryption
- Secrets management
