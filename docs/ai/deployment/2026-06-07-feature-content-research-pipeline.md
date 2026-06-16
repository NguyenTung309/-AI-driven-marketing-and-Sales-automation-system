---
phase: deployment
title: Deployment Strategy
description: Define deployment process, infrastructure, and release procedures
---

# Deployment Strategy

> M18 Content + Research pipeline. Deploys through the existing Clawbot.Api + Clawbot.AgentService split. No new runtime service is introduced.

## Infrastructure
**Where will the application run?**

- `Clawbot.Api` hosts `/api/content`, SignalR dashboard notifications, Hangfire workers, and the Hangfire dashboard.
- `Clawbot.AgentService` hosts gRPC services for content generation and weekly research scans.
- SQL Server remains the source of truth for `content_briefs`, `content_items`, and `content_schedule`.
- Qdrant remains the RAG dependency used by `ContentAgent`.
- External dependencies are environment-driven: OpenAI-compatible LLM endpoint, YouTube Data API key, optional TikTok/Baidu trend URLs, and Buffer/Later/Ayrshare-shaped publisher endpoint.
- Local supporting services are already in [deploy/docker-compose.yml](../../../deploy/docker-compose.yml): SQL Server, Redis, RabbitMQ, Qdrant, and MinIO.

## Deployment Pipeline
**How do we deploy changes?**

### Build Process
- Restore packages: `dotnet restore Clawbot.sln`.
- Build the full backend solution: `dotnet build Clawbot.sln --no-restore`.
- Run the full test suite before release: `dotnet test Clawbot.sln --no-build`.
- Build and deploy both backend processes together because API gRPC clients depend on AgentService `ContentAgent` and `ResearchAgent` contracts.

### CI/CD Pipeline
- Required gates: restore, solution build, all xUnit projects.
- AgentService service-level tests live in `tests/Clawbot.AgentService.Tests` and directly exercise gRPC methods with SQLite.
- Full HTTP endpoint integration coverage is deferred to M21 with the repo's broader integration-test pattern.

## Environment Configuration
**What settings differ per environment?**

### Development
- Use `deploy/.env.example` for local external-service placeholders.
- `AgentService:Url` defaults to `http://localhost:5050` in API startup when unset.
- `Content:Llm:*` may stay blank only when tests do not call live generation; live manual smoke requires `BaseUrl`, `ApiKey`, and `Model`.
- `Content:Trends:Baidu:Enabled=false` is the safe local default.

### Staging
- Set all secrets through the environment or the staging secret store, not checked-in appsettings.
- Required M18 settings:
  - `Content__Llm__BaseUrl`
  - `Content__Llm__ApiKey`
  - `Content__Llm__Model`
  - `Content__Trends__YouTube__ApiKey`
  - `Content__Publisher__Endpoint`
  - `Content__Publisher__Token`
  - `AgentService__Url`
- Optional settings: TikTok scrape URL, Baidu enabled flag and URL, `Content__Llm__MaxOutputTokens`.

### Production
- Use production SQL Server backups before schema changes.
- Keep Hangfire content jobs enabled only after AgentService and publisher credentials are verified.
- Restrict `/hangfire` before production exposure; current dashboard authorization is development-open.

## Deployment Steps
**What's the release process?**

1. Verify SQL Server backup and confirm the target database already has `0001_init.sql`.
2. Apply [0002_content_schedule_retry_count.sql](../../../deploy/migrations/0002_content_schedule_retry_count.sql).
3. Deploy `Clawbot.AgentService` and verify gRPC port/protocol is HTTP/2.
4. Deploy `Clawbot.Api` with `AgentService:Url` pointing at AgentService.
5. Seed sample briefs if needed with [content-briefs.sql](../../../deploy/seed/content-briefs.sql), after setting the tenant slug in the script.
6. Open `/health/live`, `/health/ready`, and `/hangfire`.
7. Trigger a manual content generation and a manual trend scan in staging.
8. Confirm `content_items`, `content_briefs`, and `content_schedule.retry_count` write as expected.

## Database Migrations
**How do we handle schema changes?**

- M18 adds `content_schedule.retry_count` and a filtered unique index preventing duplicate pending schedules per item.
- Trend briefs reuse `content_briefs`; content generation and repurpose reuse `content_items`; no new trend table is required.
- Rollback for `0002` requires dropping `ix_content_schedule_pending_item` and `content_schedule.retry_count`; do that only after draining or cancelling pending publish jobs.

## Secrets Management
**How do we handle sensitive data?**

- Do not commit LLM, YouTube, or publisher tokens.
- Use double-underscore environment variable names for nested .NET options.
- Rotate publisher and LLM tokens independently; AgentService must be restarted after LLM secret changes, API after publisher secret changes.
- Logs must not include prompts, generated bodies, access tokens, or publisher payload secrets.

## Rollback Plan
**What if something goes wrong?**

- Stop Hangfire content jobs first if publish or trend scans misbehave.
- Roll API back before AgentService only if REST endpoint behavior is the issue.
- Roll both API and AgentService back together if protobuf or gRPC contract behavior is suspect.
- If publisher failures spike, disable the publisher token/endpoint or pause `content-publish-due`; generated drafts remain safe in `content_items`.
- If trend sources fail, disable the affected source config; weekly scans degrade to the remaining enabled sources.
