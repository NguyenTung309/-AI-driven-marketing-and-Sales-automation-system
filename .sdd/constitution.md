# ClawBot SaleMkt — Project Constitution

> Status: **DRAFT (v0.1)** — ratify in team review (week 1) before merging to `main`.
> Once ratified, this file is immutable except by unanimous team vote.

---

## ARTICLE 1 — TECH STACK (immutable once ratified)

- **Backend**: .NET 8, C# 12, ASP.NET Core minimal APIs, EF Core 8 + Microsoft.EntityFrameworkCore.SqlServer.
- **gRPC**: Grpc.AspNetCore 2.66, proto3, service contracts in `proto/`.
- **AI / Orchestration**: Microsoft Semantic Kernel + ClawBot Claude Code SDK + CrewAI + Langflow.
- **LLM**: Anthropic Claude Sonnet 4.6 (primary) via `llm_configs` table.
- **Database**: **Microsoft SQL Server 2022**. DDL in `deploy/migrations/0001_init.sql` (source of truth).
- **Cache / queue**: Redis 7, RabbitMQ 3, MassTransit.
- **Vector store**: **Qdrant** (primary, only backend) — SQL Server stores JSON snapshot of embeddings.
- **Object storage**: MinIO (S3-compatible).
- **Frontend**: React 19 + Vite + TypeScript + Tailwind + Zustand + TanStack Query.
- **Observability**: Serilog (structured JSON), OpenTelemetry traces, Metabase BI.
- **Deploy**: Docker Compose (single VPS), 8 GB RAM target. Containers under `deploy/docker-compose.yml`.

Adding a dependency requires a one-page RFC under `.sdd/rfcs/`.

## ARTICLE 2 — CODING STANDARDS

- **Layout**: DDD layered — Domain → Application → Infrastructure → Api/AgentService.
- **Domain**: zero external dependencies (no EF, no Pgvector, no MediatR). Pure C#.
- **Naming**: PascalCase for types, camelCase for locals, snake_case for SQL columns/tables.
- **Immutability**: `record`/`init` setters preferred. Aggregates expose mutations as methods, not setters.
- **Nullability**: enabled project-wide; treat warnings as errors (`TreatWarningsAsErrors=true`).
- **File size**: 400 LOC typical, 800 max. Split when split is natural.
- **No magic numbers** outside `*Constants.cs` or config sections.

## ARTICLE 3 — SECURITY POLICIES (non-negotiable)

- No secrets in source. Use `appsettings.{env}.json` placeholders + env vars + secret manager.
- All PII (`contacts.phone`, `contacts.email`, message content) encrypted at rest where regulation requires.
- Conversation raw content purged after **30 days** (NFR-03). Hashed audit retained.
- Parameterized SQL only. EF Core + `Dapper` parameterized — never string concat.
- HTTPS / TLS 1.3 enforced on all public endpoints.
- Webhook adapters MUST verify HMAC signature before processing.
- JWT short-lived (15 min), refresh via httpOnly cookie. Lockout after 5 failed logins.
- Rate-limit by tenant on `/api/*` and per-IP on `/webhooks/*`.

## ARTICLE 4 — GIT WORKFLOW

- Branch off `main`. Naming: `feat/<scope>`, `fix/<scope>`, `chore/<scope>`.
- Conventional commits: `<type>(<scope>): <summary>` with body explaining *why*.
- Squash on merge. PR must reference SPEC ID (`SPEC-01`, `UC-A07`, etc.).
- No force-push to `main`. Pre-receive hook rejects.

## ARTICLE 5 — TESTING REQUIREMENTS

- TDD on Domain + Application — write test first, then implementation.
- Coverage floor: **80%** on Domain and Application. Reported in CI.
- Test types: xUnit (unit), Testcontainers (integration), Playwright (frontend E2E).
- Integration tests run against real Postgres + Redis containers — no mocks for DB.
- Every bug fix MUST include a regression test.

## ARTICLE 6 — AI AGENT RULES

- Every agent has a `code` registered in `agents` table + a `SKILL.md` file in `.sdd/skills/`.
- Agents read Knowledge Base versions from `kb_versions` table — never hardcode prompts in code.
- Agent traces written to `agent_traces` (immutable). Status changes recorded.
- Claude API monthly budget hard-cap **$200** at Anthropic console + soft alert at 80%.
- Response cache (Redis) keyed by `tenant_id + content_hash` to dedupe identical prompts.
- Auto-escalation to human when KB confidence < threshold or detected sensitive intent.

## ARTICLE 7 — REVIEW PROCESS

- All PRs require:
  1. CI green (build, test, lint).
  2. One human reviewer approval.
  3. Linked SPEC / UC ID.
  4. Updated `docs/` if public contract changed.
- Security-sensitive PRs require `security-reviewer` agent + 2 human approvers.
- `main` deploys to staging automatically; production deploy is manual after smoke check.

---

Ratified by: ___________________ (date: ____)
