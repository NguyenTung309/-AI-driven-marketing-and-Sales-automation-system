---
phase: monitoring
title: Monitoring & Observability
description: Define monitoring strategy, metrics, alerts, and incident response
---

# Monitoring & Observability

> M18 Content + Research pipeline. Focus on generation latency/token usage, weekly trend scan health, publish reliability, and queue backlog.

## Key Metrics
**What do we need to track?**

### Performance Metrics
- Content generation latency by tenant/platform from `ContentAgentGrpcService` log event `5201`.
- LLM input/output tokens by tenant/platform from the same `5201` event.
- Weekly trend scan duration and trend count per tenant/week from `WeeklyTrendScanJob`.
- Publish job batch size, retry count, and terminal failures from `ContentPublishJob`.
- Pending schedule backlog: count of `content_schedule` rows with `status='pending'` and `scheduled_at <= now`.

### Business Metrics
- Drafts generated per platform from `content_items`.
- Repurpose variants created per source item.
- Trend briefs generated per ISO week from `[trend:{week}]` `content_briefs`.
- Approved, scheduled, published, and rejected counts by platform.
- Publish success rate and average retries before success.

### Error Metrics
- gRPC invalid-argument/not-found rates for content generation, repurpose, and trend scan calls.
- LLM completion failures or missing `Content:Llm` config.
- Trend source zero-result and exception counts by source.
- Publisher failures grouped by reason and platform.
- Hangfire failed jobs on queue `content`.

## Monitoring Tools
**What tools are we using?**

- Serilog console output for structured application logs.
- Hangfire dashboard at `/hangfire` for recurring job status, retries, and failed jobs.
- API health endpoints: `/health/live` and `/health/ready`.
- SignalR dashboard notifications for publish failures through `SignalRContentNotifier`.
- SQL queries against `content_*` tables for operational checks until a dedicated dashboard lands.

## Logging Strategy
**What do we log and how?**

- Content draft generation logs event `5201` with `TenantId`, `ContentItemId`, `Platform`, `InputTokens`, `OutputTokens`, and `LatencyMs`.
- Weekly trend scan logs success/failure with tenant, week, trend count, and reason.
- Publish job logs posted, retrying, and failed events with tenant, item, schedule, attempt, and reason.
- Do not log prompt bodies, generated content bodies, access tokens, publisher bearer tokens, or raw third-party payloads.
- Cost/latency is captured as tokens plus latency in event `5201`; a generic cross-provider cost aggregator is a documented non-goal for M18 because the existing tracker is Anthropic-specific.

## Alerts & Notifications
**When and how do we get notified?**

### Critical Alerts
- Hangfire job `content-publish-due` reaches terminal failed state -> pause publishing, inspect publisher credentials/endpoint, and notify ops.
- `content-weekly-trend-scan` fails for all active tenants in a run -> inspect AgentService reachability and trend source configs.
- API cannot reach AgentService at `AgentService:Url` -> roll back config or restart AgentService.
- Pending due schedules exceed the expected batch window for more than 15 minutes -> check Hangfire worker and publisher availability.

### Warning Alerts
- Content generation p95 latency exceeds 10 seconds for 15 minutes.
- `5201` token counts spike materially for one platform, indicating prompt/config drift.
- All enabled trend sources return zero trends for a week.
- Publish retry count increases but terminal failures have not occurred yet.

## Dashboards
**What do we visualize?**

- Content funnel: draft -> approved -> scheduled -> published/rejected.
- Publish health: pending due count, posted count, failed count, retry histogram.
- Trend scan health: last successful week per tenant and trend count.
- LLM usage: latency, input tokens, output tokens, platform split.
- Hangfire queue `content`: recurring job status and failed job list.

## Incident Response
**How do we handle issues?**

1. Check `/health/live`, `/health/ready`, and AgentService process health.
2. Check Hangfire queue `content` and recent failed jobs.
3. Inspect logs for event `5201`, weekly trend scan failures, and publish failure reasons.
4. Query `content_schedule` for due pending backlog and high retry counts.
5. Disable the failing external dependency in config when possible: trend source, publisher endpoint/token, or LLM config.
6. Re-run the failed operation manually in staging before re-enabling production jobs.

## Health Checks
**How do we verify system health?**

- API liveness: `GET /health/live`.
- API readiness: `GET /health/ready`.
- AgentService readiness is verified through a gRPC client call from API flows or service-level smoke tests.
- Database readiness is verified by SQL Server connectivity and writes to `content_items` / `content_briefs`.
- Queue health is verified by Hangfire recurring job status for `content-weekly-trend-scan` and `content-publish-due`.

## Known Gaps

- No dedicated OpenTelemetry dashboard or generic LLM cost ledger in M18.
- Full HTTP endpoint integration tests are deferred to M21.
- Live third-party trend and publisher checks require ops-provisioned credentials and are not part of unit tests.
