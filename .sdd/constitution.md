# ClawBot SaleMkt — Project Constitution

> Status: **DRAFT (v0.2)** — ratify in team review (week 1) before merging to `main`.
> Once ratified, this file is **immutable** except by unanimous team vote documented in `.sdd/rfcs/`.
> Path: `.sdd/CONSTITUTION.md` — referenced by both `CLAUDE.md` and `AGENTS.md`.

---

## ARTICLE 1 — TECH STACK (immutable once ratified)

- **Backend**: .NET 8, C# 12, ASP.NET Core minimal APIs, EF Core 8 + `Microsoft.EntityFrameworkCore.SqlServer`.
- **API Gateway**: YARP (Yet Another Reverse Proxy) — standalone project `Clawbot.Gateway`, sits between Pancake (T-0) and backend (T-1). Owns routing, HMAC pre-validation, rate limiting, trace header injection.
- **gRPC**: `Grpc.AspNetCore` 2.66, proto3. Service contracts in `proto/`. Used for agent ↔ orchestrator transport.
- **AI / Orchestration**: Microsoft Semantic Kernel (orchestrator + agent plugins). CrewAI / Langflow for workflow tooling.
- **LLM**: Configured via `llm_configs` table — **not hardcoded**. Primary model slot: Anthropic Claude Sonnet 4.6. Cost-optimized slot: DeepSeek V4 flash/pro. Active model per tenant resolved at runtime.
- **Database**: **Microsoft SQL Server 2022**. DDL in `deploy/migrations/0001_init.sql` is source of truth. EF Core maps to it — do NOT run `EnsureCreated` or auto-generate schema.
- **Cache / Queue**: Redis 7 (cache + SignalR backplane), RabbitMQ 3 + MassTransit.
- **Vector store**: Qdrant (primary, only query backend). SQL Server stores JSON snapshot of embeddings for backup/audit.
- **Object storage**: MinIO (S3-compatible).
- **Frontend**: React 19 + Vite + TypeScript + Tailwind + Zustand + TanStack Query.
- **Observability**: Serilog (structured JSON), OpenTelemetry traces, Metabase BI.
- **Deploy**: Docker Compose (single VPS, 8 GB RAM target). All containers under `deploy/docker-compose.yml`.

> Adding any dependency requires a one-page RFC under `.sdd/rfcs/` approved before implementation.

---

## ARTICLE 2 — CODING STANDARDS

- **Layout**: DDD layered — `Domain → Application → Infrastructure → Api / AgentService / Gateway`.
- **Domain**: zero external dependencies — no EF, no MediatR, no gRPC. Pure C#.
- **Naming**: PascalCase for types, camelCase for locals, `snake_case` for SQL columns/tables.
- **Immutability**: `record` / `init` setters preferred. Aggregates expose mutations as methods, not public setters.
- **Soft delete**: All core business entities extend `BaseEntity` and carry `IsDeleted` + `DeletedAt`. EF Core global query filter declared in `OnModelCreating` (not `OnConfiguring`).
- **Enums**: Stored as `string` in DB via `HasConversion<string>()`. Never magic numbers.
- **Nullability**: enabled project-wide. `TreatWarningsAsErrors=true`.
- **File size**: 400 LOC typical, 800 max. Split when split is natural.
- **No magic numbers** outside `*Constants.cs` or config sections.
- **EARS annotation**: All business logic must have an EARS comment above it:
  `// EARS[WHEN <trigger> THE SYSTEM SHALL <behavior>]`
- **Logging**: Always inject `ILogger<T>` via constructor DI. Never call `Serilog.Log.*` static methods in `Api`, `Application`, or `AgentService` layers.

---

## ARTICLE 3 — SECURITY POLICIES (non-negotiable)

- **No secrets in source.** Use `appsettings.{env}.json` placeholders + env vars + secret manager. `Trusted_Connection=True` for local SQL Server (Windows Auth). Never embed plaintext passwords.
- **PII protection**: `contacts.phone`, `contacts.email`, and raw message content must never be logged. Log only sanitized metadata (message ID, channel, timestamp).
- **Conversation purge**: Raw content purged after **30 days** (NFR-03). Hashed audit record retained.
- **SQL**: Parameterized only — EF Core or `Dapper` parameterized. Never string-concat SQL.
- **HTTPS / TLS 1.3** enforced on all public endpoints.
- **Webhook HMAC**: All webhook adapters MUST verify HMAC-SHA256 signature at the first intercept point (YARP middleware) before deserializing payload. Fail → `401`. This is a **Layer 1 hard rule**.
- **HMAC comparison**: Use `CryptographicOperations.FixedTimeEquals` — never `==`.
- **JWT**: Short-lived (15 min), refresh via httpOnly cookie. Lockout after 5 failed logins.
- **Rate limiting**: Per-tenant on `/api/*`, per-IP on `/webhook/*`. Configured in YARP gateway.
- **Stack traces**: Never expose to client. All error responses use `{ errorCode, message, requestId }`.

---

## ARTICLE 4 — GIT WORKFLOW

- Branch off `main`. Naming: `feat/<scope>`, `fix/<scope>`, `chore/<scope>`.
- Conventional commits: `<type>(<scope>): <summary>` — body explains *why*, not what.
- Squash on merge. PR must reference SPEC ID (`SPEC-01`, `UC-A07`, etc.).
- No force-push to `main`. Pre-receive hook rejects.
- Never `--no-verify`.

---

## ARTICLE 5 — TESTING REQUIREMENTS

- **TDD** on Domain + Application — write test first, then implementation.
- **Coverage floor**: 80% on Domain and Application. Reported in CI.
- **Test types**: xUnit (unit), Testcontainers (integration), Playwright (frontend E2E).
- Integration tests run against real SQL Server + Redis containers — no mocks for DB.
- Every bug fix MUST include a regression test.
- HMAC middleware: must have unit tests covering valid / invalid / missing signature / route-without-HMAC cases.

---

## ARTICLE 6 — AI AGENT RULES

- Every agent has a `code` registered in `agents` table + a `SKILL.md` in `.sdd/skills/`.
- Agents read prompts from `kb_versions` table — **never hardcode prompts in code**.
- Agent traces written to `agent_traces` (immutable append-only). Status changes recorded.
- LLM monthly budget hard-cap **$200** at provider console + soft alert at 80%.
- Response cache (Redis) keyed by `tenant_id + content_hash` to dedupe identical prompts.
- Auto-escalation to human when KB confidence < threshold or detected sensitive intent.
- LLM model used per request must be resolved from `llm_configs` — never hardcoded in agent code.

---

## ARTICLE 7 — REVIEW PROCESS

- All PRs require:
  1. CI green (build, test, lint, `dotnet format` clean, 0 warnings).
  2. One human reviewer approval.
  3. Linked SPEC / UC ID.
  4. Updated `docs/` if public contract changed.
- Security-sensitive PRs require `security-reviewer` label + 2 human approvers.
- `main` deploys to staging automatically; production deploy is manual after smoke check.
- DDL changes require review of `deploy/migrations/` AND EF Core configuration alignment.

---

Ratified by: ___________________ (date: ____)