# CLAUDE.md — ClawBot Multi-Agent System
# Single source of truth for behavior, architecture, and coding standards.
# BEFORE EXECUTING: Read `.sdd/CONSTITUTION.md` for top-level hard rules that override everything here.

---

## 1. CORE BEHAVIORAL GUIDELINES

### 1.1. Think Before Coding
**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them — don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

### 1.2. Simplicity First
**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

_Ask yourself:_ "Would a senior engineer say this is overcomplicated?" If yes, simplify.

### 1.3. Surgical Changes
**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it — don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

_The test:_ Every changed line should trace directly to the user's request.

### 1.4. Goal-Driven Execution
**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```text
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

## 2. PROJECT OVERVIEW

**ClawBot SaleMkt** is an AI-driven multi-agent marketing & sales automation system for Vietnamese SMEs (initial focus: Chinese-language tutoring business). It aggregates DMs + comments from **Zalo OA, Facebook, TikTok, Instagram, YouTube** into a unified inbox, processes them with **8 AI agents + 5 humans + a Chinese-language Knowledge Base**, and delivers 24/7 consultation and 3× sales productivity.

**Scale target:** ~hundreds of concurrent users, single-node + resilience (not full HA cluster).

**Three architectural pillars:**
1. Multi-agent coordination mechanism (core research focus)
2. Right-sized fault tolerance (Polly + Redis + RabbitMQ + health checks)
3. Measurable NFRs (p95 latency < 2s first token, ~99.5% availability)

See `docs/ClawBot_SaleMkt_ProjectPlan.docx` for full product plan and `docs/spec-audit.md` for traceability.

---

## 3. ARCHITECTURE: 6-TIER STACK

```
T-0    Channels       Pancake Platform (Zalo OA · FB · IG · TikTok · YouTube)
         ↕ Pancake webhook (in) / Pancake API (out)
T-0.5  API Gateway    Clawbot.Gateway — YARP Reverse Proxy
         ↕ Route / HMAC validate / RateLimit / TraceId inject
T-1    Backend        Clawbot.Api — ASP.NET Core (stateless, horizontal-ready)
         ↕ Publish job / decouple request path
T-2    Message Queue  RabbitMQ + MassTransit (single node, durable queue)
         ↕ Orchestrate / Agent dispatch
T-3    AI Core        Semantic Kernel · Orchestrator + 8 sub-agents via gRPC
         ↕ Read / Write / Embed / Query / Cache
T-4    Data + Cache   SQL Server 2022 · Qdrant · MinIO/S3 · Redis 7
         ↕ Display / Stream / Send
T-5    Output         Dashboard (SignalR streaming) · BI (Metabase) · CRM/Scheduler (deferred)
```

### T-0.5: YARP Gateway — responsibilities
- **Routing**: `/webhook/*` → backend; `/api/*` → backend; `/hubs/*` → backend. Isolates external surface.
- **HMAC-SHA256 pre-validation**: Verify Pancake webhook signature before payload reaches backend (CONSTITUTION Article 3).
- **Rate limiting**: Per-IP on `/webhook/*`, per-tenant on `/api/*`.
- **TraceId**: Inject or forward `X-Trace-Id` header downstream on every request.
- **Auth**: JWT validation for dashboard/API endpoints; webhooks use HMAC (not JWT).

### T-3: AI Core — agent catalog
| Agent code | Role |
|---|---|
| `orchestrator` | plan → dispatch → observe → re-plan; breaks goals into tasks; assigns agents |
| `chat` | Customer-facing chat, RAG + conversation memory |
| `content` | Content generation (posts, copy, captions) |
| `lead` | Lead scoring, qualification, reporting |
| `kb-ingest` | Knowledge Base ingestion + chunking + embedding |
| `ads` | Ad campaign suggestions and performance analysis |
| `sale-assist` | Human sales rep assistance, objection handling |
| `analytics` | Dashboard metrics, trend summarization |

All agents are gRPC services in `Clawbot.AgentService`. Each has a registered `code` in `agents` table and a `SKILL.md` in `.sdd/skills/`.

### LLM configuration
LLM is **not hardcoded**. Resolved at runtime from `llm_configs` table per tenant/task:
- Primary slot: Anthropic Claude Sonnet 4.6
- Cost-optimized slot: DeepSeek V4 flash (`deepseek-v4-flash`) — 1M ctx
- Heavy reasoning slot: DeepSeek V4 pro (`deepseek-v4-pro`) — 1M ctx
- Note: DeepSeek old aliases `deepseek-chat/reasoner` deprecated 24/07/2026. Reasoning is now a mode (`thinking on/off`) on same model.

---

## 4. PROJECT STRUCTURE

```
ClawBot.sln
├── src/
│   ├── api/
│   │   └── Clawbot.Api/                      # ASP.NET Core minimal APIs
│   │       ├── Endpoints/<Context>Endpoints.cs
│   │       ├── Middleware/                   # TraceId, error handling
│   │       ├── Hubs/                         # SignalR Hub (realtime dashboard)
│   │       └── Program.cs
│   ├── gateway/
│   │   └── Clawbot.Gateway/                  # YARP reverse proxy (T-0.5)
│   │       ├── Configuration/GatewayOptions.cs
│   │       ├── Middleware/TraceIdMiddleware.cs
│   │       ├── Middleware/PancakeHmacMiddleware.cs
│   │       ├── appsettings.json              # ReverseProxy routes/clusters
│   │       └── Program.cs
│   ├── shared/
│   │   ├── Clawbot.Domain/
│   │   │   └── <Context>/                    # One folder per bounded context
│   │   │       ├── <Aggregate>.cs            # Must extend BaseEntity
│   │   │       ├── Events/
│   │   │       └── Enums/                    # Stored as string (ADR-003)
│   │   ├── Clawbot.Application/
│   │   │   └── Modules/<Context>/
│   │   │       ├── Commands/
│   │   │       └── Queries/
│   │   ├── Clawbot.Infrastructure/
│   │   │   ├── Persistence/
│   │   │   │   ├── Configurations/           # EF Fluent API + global filters
│   │   │   │   └── Migrations/
│   │   │   ├── Repositories/
│   │   │   ├── Messaging/                    # RabbitMQ / MassTransit
│   │   │   ├── AI/                           # Semantic Kernel wiring
│   │   │   └── Cache/                        # Redis wrappers
│   │   └── Clawbot.SharedKernel/
│   │       └── (Multitenancy|Time|Vectors|Channels|Security)
│   └── agents/
│       ├── Clawbot.Agents.Contracts/         # gRPC stubs from /proto
│       ├── Clawbot.Agents.Core/              # IAgent, orchestrator, ISkill interfaces
│       └── Clawbot.AgentService/
│           └── Services/<Agent>GrpcService.cs
├── tests/
│   ├── Clawbot.UnitTests/
│   └── Clawbot.IntegrationTests/
├── proto/
│   └── <service>.proto                       # gRPC contracts — source of truth
├── deploy/
│   ├── docker-compose.yml
│   └── migrations/0001_init.sql              # DDL — source of truth for schema
├── docs/
│   ├── ClawBot_SaleMkt_ProjectPlan.docx
│   ├── spec-audit.md                         # FR → code traceability
│   ├── erd.md                                # Mermaid ERD
│   └── erd-notion.md
└── .sdd/
    ├── CONSTITUTION.md                       # Hard rules — read before executing
    ├── AGENTS.md                             # Entry point for Codex
    ├── rfcs/                                 # One-page RFCs for new dependencies
    ├── specs/<feature>/SPEC.md              # EARS notation per feature
    └── skills/<agent-code>.md               # Per-agent prompt skill files
```

**Dependency flow (strictly inward):**
```
Gateway → (none — infrastructure only)
Api → Application → Domain ← Infrastructure
AgentService → Agents.Core → Domain
```
Never reference `Api`, `Infrastructure`, or `AgentService` from `Domain`.

**Bounded contexts** (one folder per context under `Clawbot.Domain`):
`Tenants, Contacts, Conversations, KnowledgeBase, ChatScenarios, Agents, Leads, SaleAssist, Documents, Content, Ads, Analytics, Security`

---

## 5. COMMON COMMANDS

```bash
# Build entire solution
dotnet build ClawBot.sln

# Run gateway (port 5050)
dotnet run --project src/gateway/Clawbot.Gateway

# Run API (port 5051)
dotnet run --project src/api/Clawbot.Api

# Run agent service
dotnet run --project src/agents/Clawbot.AgentService

# Run all tests
dotnet test ClawBot.sln

# Run unit tests only
dotnet test tests/Clawbot.UnitTests

# Run integration tests only
dotnet test tests/Clawbot.IntegrationTests

# EF Core migrations (always target Infrastructure + Api startup)
dotnet ef migrations add <MigrationName> \
  --project src/shared/Clawbot.Infrastructure \
  --startup-project src/api/Clawbot.Api

dotnet ef database update \
  --project src/shared/Clawbot.Infrastructure \
  --startup-project src/api/Clawbot.Api

# Regenerate gRPC stubs from proto
dotnet build src/agents/Clawbot.Agents.Contracts

# Docker local stack (SQL Server, Redis, RabbitMQ, Qdrant, MinIO)
docker compose -f deploy/docker-compose.yml up -d

# Format check
dotnet format ClawBot.sln --verify-no-changes
```

---

## 6. ARCHITECTURE DECISION RECORDS (ADR)

**ADR-001: Clean Architecture (project separation)**
Four shared projects: `Clawbot.Domain`, `Clawbot.Application`, `Clawbot.Infrastructure`, `Clawbot.SharedKernel`. Dependency strictly inward. Gateway is intentionally isolated with zero references to shared projects.

**ADR-002: Soft delete**
All core business entities extend `BaseEntity` and include `IsDeleted` + `DeletedAt`. EF Core global query filter in `OnModelCreating` automatically excludes soft-deleted records from all LINQ queries.

**ADR-003: Enums stored as string in DB**
Configure EF Fluent API with `HasConversion<string>()` for all enums. DB records must be human-readable. Never magic numbers.

**ADR-004: Realtime infrastructure**
Sprint 1 (dev): SignalR Hub in-memory acceptable. **Before staging/production:** Redis Backplane must be configured for horizontal scaling.

**ADR-005: TraceId propagation**
`X-Trace-Id` injected at YARP gateway layer. Propagated via `Serilog LogContext` through all downstream layers. Returned in HTTP Response Header. Every log line carries `TraceId`.

**ADR-006: Windows Auth for local DB**
SQL Server 2022 on local/dev uses Windows Authentication (`Trusted_Connection=True`). Never embed plaintext passwords in connection strings.

**ADR-007: YARP as API Gateway**
Standalone project `Clawbot.Gateway` using YARP sits between Pancake (T-0) and backend (T-1). Owns routing, HMAC pre-validation, rate limiting, trace injection. Config in `appsettings.json` under `ReverseProxy` section — not in C# code. Zero project references to other ClawBot projects.

**ADR-008: gRPC for agent transport**
Agents are gRPC services in `Clawbot.AgentService`. Proto contracts in `proto/` are source of truth. Orchestrator calls agents via generated stubs from `Clawbot.Agents.Contracts`. Reason: strongly-typed contracts, streaming support, language-agnostic if agents need to split later.

**ADR-009: DDL as schema source of truth**
`deploy/migrations/0001_init.sql` is source of truth. EF Core maps to it — never generates schema. Do not run `EnsureCreated`. New schema changes require a new `000X_*.sql` migration file AND updated EF Fluent configurations.

**ADR-010: LLM resolved at runtime**
LLM model per request resolved from `llm_configs` table (tenant + task type). Never hardcoded in agent code. Enables per-tenant model routing and cost control without redeployment.

**ADR-011: Multi-tenancy via ITenantOwned**
All tenant-scoped entities implement `ITenantOwned` and carry `tenant_id`. EF Core global query filter enforces tenant isolation automatically. Never filter by `tenant_id` manually in queries.

---

## 7. LESSONS LEARNED

**LESSON-001: EF Core global filter placement**
Soft delete global query filters **must** be in `OnModelCreating`. Declaring in `OnConfiguring` causes EF Core to silently ignore them — soft-deleted records leak into results.

**LESSON-002: PII risk on webhook endpoints**
Endpoints receiving raw Pancake webhook payloads must **never log raw payload**. Logging message content, customer names, or phone numbers violates PII laws. Log only sanitized metadata (message ID, channel, timestamp).

**LESSON-003: Webhook security gate**
HMAC-SHA256 validation is a **hard rule**. Validate at YARP middleware before payload reaches backend. Failed HMAC → `401`, never forwarded. Use `CryptographicOperations.FixedTimeEquals` — not `==`.

**LESSON-004: YARP route ordering**
YARP evaluates routes top-to-bottom. Specific routes (`/webhook/pancake`) must be declared before broad catch-alls. Wrong order silently routes traffic to wrong clusters.

**LESSON-005: Tenant filter and soft delete stacking**
When both `ITenantOwned` filter and soft delete filter apply to the same entity, declare them as separate `HasQueryFilter` calls in `OnModelCreating`. EF Core 8 supports multiple filters per entity — combining them in one lambda causes one filter to silently override the other.

---

## 8. CURRENT SPRINT STATUS

> **This section changes every sprint. For the authoritative current state, see `.sdd/specs/` active SPEC files.**

**Sprint 0 — completed (planning + scaffolding):**
- DDL applied to local SQL Server
- 12 Domain bounded contexts stubbed
- 8 agent gRPC services stubbed
- `.sdd/` artifacts authored
- Active SPEC files: `.sdd/specs/01-omnichannel-inbox` .. `10-admin-security`

**Sprint 1 — in progress (core infrastructure):**
- EF Core entities + Fluent configurations
- Serilog JSON logging + OpenTelemetry setup
- SignalR Hub (realtime dashboard)
- Webhook stub APIs (receive Pancake events)
- YARP gateway initial route configuration + HMAC middleware

**Blocked (deferred to Sprint 2):**
- RabbitMQ message publishing
- Polly resilience wrapping around external calls
- Hangfire background jobs

**Next tasks:**
1. Implement HMAC-SHA256 validation in `Clawbot.Gateway`
2. Parse raw Pancake webhook payload → map to `IncomingMessage` entity
3. Push `IncomingMessage` to RabbitMQ queue

---

## 9. MANDATORY CODING PATTERNS

### Repository Pattern
`Domain` defines `IRepository<T>` in `Domain/<Context>/Interfaces/`. `Infrastructure` implements with EF Core. `Application` never references EF Core directly.

### Unit of Work Pattern
All mutating operations in `Application` handlers call `IUnitOfWork.SaveChangesAsync()` at the end. Ensures transactional integrity across multiple repository operations.

### Domain Events
Entity raises event on state change → dispatch immediately after Unit of Work saves successfully → publish to RabbitMQ via MassTransit.

### Multi-tenancy
All tenant-scoped entities implement `ITenantOwned`. Never manually filter by `tenant_id` in queries — the EF global filter handles it. Inject `ITenantContext` to get current tenant.

### Error Response Format
All API error responses:
```json
{ "errorCode": "...", "message": "...", "requestId": "..." }
```
Never expose stack traces or internal exceptions to clients.

### Logging Injection
Always inject `ILogger<T>` via constructor DI. Never call `Serilog.Log.*` static methods in `Api`, `Application`, or `AgentService` layers.

### EARS Comment Annotation
All business logic code must have an EARS comment immediately above:
```csharp
// EARS[WHEN <trigger> THE SYSTEM SHALL <behavior>]
```

### Graceful Degradation
When one agent fails mid-execution, the orchestrator re-assigns or skips with controlled fallback. Failure must not propagate to the entire response pipeline. Implement at orchestrator business logic level — not just infrastructure retry.

### YARP Route Config Pattern
All YARP routes and clusters defined in `appsettings.json` under `ReverseProxy`. Never in C# code unless dynamic routing is explicitly required and documented.

### gRPC Agent Pattern
Agent gRPC services in `Clawbot.AgentService` are thin — they delegate to `Clawbot.Agents.Core`. Skills loaded from `kb_versions` table, not hardcoded. Traces written to `agent_traces` on every invocation.

### Forbidden Patterns
- ❌ Secrets in source files or committed `appsettings.json`
- ❌ String-concat SQL anywhere
- ❌ Mutable aggregate state via public setters
- ❌ Logic in controllers/endpoints — push to Application handlers
- ❌ Hardcoded prompts in agent service code
- ❌ Calling LLM from Domain or Application layer — only from `AgentService`
- ❌ Skipping audit log on security-sensitive actions
- ❌ Manual `tenant_id` filtering in queries

---

## 10. DEFINITION OF DONE

A change is done when:
- [ ] Tests written first (TDD), passing locally and in CI
- [ ] 80%+ coverage on touched Domain/Application code
- [ ] EF mapping updated AND `deploy/migrations/` updated (new `000X_*.sql` if schema changed)
- [ ] SPEC.md cross-referenced (`SPEC-XX`, `UC-Yzz`)
- [ ] No new TODO without an issue link
- [ ] `dotnet format` clean. `dotnet build` clean (0 warnings)
- [ ] PR description ties back to the user-visible outcome

---

## 11. NON-FUNCTIONAL REQUIREMENTS (NFR TARGETS)

| Dimension | Target | Measurement |
|---|---|---|
| Latency (chat) | p95 first token < 2s, full response < 8s | Serilog timing + `/metrics` |
| Throughput | ~hundreds of concurrent sessions | RabbitMQ offloads; request path unblocked |
| Availability | ~99.5% (single-node + auto-restart + degradation) | Health check + Docker restart policy |
| Recovery | Process crash → auto-restart < 10s | Docker `restart: unless-stopped` |
| Data durability | No loss of non-reproducible data | MinIO for originals + daily DB backup |
| LLM cost | Per-session cost bounded | Flash/cost-optimized default; track tokens/session on dashboard; hard-cap $200/month |
| PII retention | Raw conversation content purged after 30 days | Scheduled purge job; hashed audit retained |

---

## 12. UPGRADE THRESHOLDS

| Trigger | Action |
|---|---|
| > ~1k users / need uptime SLA | Horizontal scale backend (stateless, already ready) |
| DB becomes bottleneck | Read replica first → Always On AG for write HA |
| Redis is single point of failure | Redis Sentinel / Cluster for failover |
| Queue overloaded / durability critical | RabbitMQ quorum queue + multiple consumers |
| Need deep incident tracing | OpenTelemetry + Jaeger + Sentry |
| Large team / independent deployments | Split microservices + gRPC by bounded context |