# CLAUDE.md — ClawBot Multi-Agent System
# Single source of truth for behavior, architecture, and coding standards.
# BEFORE EXECUTING: Read `.sdd/global/constitution.md` for top-level safety laws (hard rules that override everything here).

Behavioral guidelines to reduce common LLM coding mistakes. Bias toward caution over speed — use judgment for trivial tasks.

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

**ClawBot** is an AI-driven multi-agent marketing & sales automation system for Vietnamese SMEs. It receives messages from multiple channels (Zalo OA, Facebook, etc.) via Pancake, routes them through a YARP API gateway, processes them with an Orchestrator + 3 specialized sub-agents (built on Semantic Kernel + DeepSeek V4), and returns responses back through Pancake.

**Scale target:** ~hundreds of concurrent users, single-node + resilience (not full HA cluster).

**Three architectural pillars:**
1. Multi-agent coordination mechanism (core research focus)
2. Right-sized fault tolerance (Polly + Redis + RabbitMQ + health checks)
3. Measurable NFRs (p95 latency < 2s first token, ~99.5% availability)

---

## 3. ARCHITECTURE: 6-TIER STACK

```
T-0  Channels        Pancake Platform (Zalo OA · FB · IG · TikTok · WhatsApp)
       ↕ Pancake webhook (in) / Pancake API (out)
T-0.5 API Gateway   YARP Reverse Proxy  ← [NEW TIER]
       ↕ Route / Auth / RateLimit
T-1  Backend         ASP.NET Core (stateless, horizontal-ready)
       ↕ Publish job / decouple request path
T-2  Message Queue   RabbitMQ (single node, durable queue)
       ↕ Orchestrate / Agent run
T-3  AI Core         Semantic Kernel · Orchestrator + 3 sub-agents
       ↕ Read / Write / Embed / Query / Cache
T-4  Data + Cache    PostgreSQL · pgvector/Qdrant · MinIO/R2 · Redis
       ↕ Display / Send
T-5  Output          Dashboard (streaming) · BI/CRM/Scheduler (deferred)
```

### T-0.5: YARP Gateway — responsibilities
- **Routing:** Forward `/webhook/*` to backend; forward `/api/*` to backend; isolate external surface from internal services.
- **HMAC-SHA256 pre-validation:** Verify Pancake webhook signature at the gateway level before payload reaches the backend (see LESSON-003).
- **Rate limiting:** Per-IP and per-channel limits to protect the agent pipeline from burst traffic.
- **Request tracing:** Inject or forward `X-Trace-Id` header downstream (see ADR-005).
- **Auth middleware:** JWT validation for dashboard/API endpoints; webhooks use HMAC (not JWT).

### T-3: AI Core — agent responsibilities
| Agent | Role |
|---|---|
| Orchestrator | plan → dispatch → observe → re-plan loop; breaks daily goals into tasks; assigns agents via `IAgentPlugin` |
| Agent-Chat | Customer-facing chat, RAG + memory |
| Agent-Content | Content generation (posts, copy) |
| Agent-Lead | Lead scoring, reporting |

**LLM (DeepSeek V4, updated 05/2026):**
- `deepseek-v4-flash` — default, 1M ctx, $0.14 in / $0.28 out
- `deepseek-v4-pro` — heavy tasks, 1M ctx, $1.74 in / $3.48 out
- Reasoning is now a **mode** (`thinking on/off` + `reasoning_effort`) on the same model — no separate R1 model. Old aliases `deepseek-chat/reasoner` deprecated 24/07/2026.

---

## 4. PROJECT STRUCTURE

```
ClawBot.sln
├── src/
│   ├── Clawbot.Api/                  # ASP.NET Core Web API + YARP config
│   │   ├── Controllers/
│   │   ├── Middleware/               # TraceId, HMAC validation, error handling
│   │   ├── Hubs/                     # SignalR Hub (realtime dashboard)
│   │   ├── Gateway/                  # YARP route/cluster configuration
│   │   └── Program.cs
│   ├── Clawbot.Application/          # Use cases, commands, queries, handlers
│   │   ├── Features/
│   │   │   ├── Chat/
│   │   │   ├── Content/
│   │   │   └── Lead/
│   │   ├── Interfaces/               # IUnitOfWork, service interfaces
│   │   └── Common/                   # Behaviors (validation, logging)
│   ├── Clawbot.Domain/               # Entities, domain events, value objects
│   │   ├── Entities/                 # Must extend BaseEntity
│   │   ├── Events/
│   │   ├── Interfaces/               # IRepository<T>
│   │   └── Enums/                    # Stored as string (see ADR-003)
│   └── Clawbot.Infrastructure/       # EF Core, Redis, RabbitMQ, Semantic Kernel
│       ├── Persistence/
│       │   ├── Configurations/       # Fluent API, global query filters
│       │   └── Migrations/
│       ├── Repositories/
│       ├── Messaging/                # RabbitMQ publishers/consumers
│       ├── AI/                       # Semantic Kernel setup, agent plugins
│       └── Cache/                    # Redis client wrappers
├── tests/
│   ├── Clawbot.UnitTests/
│   └── Clawbot.IntegrationTests/
└── .sdd/
    └── global/
        └── constitution.md           # Top-level safety laws — read before executing
```

**Dependency flow (strictly inward):**
```
Api → Application → Domain ← Infrastructure
```
Never reference `Api` or `Infrastructure` from `Domain`. Never reference `Infrastructure` from `Application`.

---

## 5. COMMON COMMANDS

```bash
# Build
dotnet build ClawBot.sln

# Run API (development)
dotnet run --project src/Clawbot.Api

# Run all tests
dotnet test ClawBot.sln

# Run unit tests only
dotnet test tests/Clawbot.UnitTests

# EF Core migrations
dotnet ef migrations add <MigrationName> --project src/Clawbot.Infrastructure --startup-project src/Clawbot.Api
dotnet ef database update --project src/Clawbot.Infrastructure --startup-project src/Clawbot.Api

# Docker (local stack: SQL Server, Redis, RabbitMQ, Qdrant)
docker compose up -d
```

---

## 6. ARCHITECTURE DECISION RECORDS (ADR)

**ADR-001: Clean Architecture (Project separation)**
System split into 4 projects: `Clawbot.Api`, `Clawbot.Application`, `Clawbot.Domain`, `Clawbot.Infrastructure`. Dependency flow is strictly inward — never outward.

**ADR-002: Soft Delete**
All core business entities must extend `BaseEntity` and include `IsDeleted` + `DeletedAt`. EF Core global query filter in `OnModelCreating` automatically excludes soft-deleted records from all LINQ queries.

**ADR-003: Enums stored as string in DB**
Configure EF Core Fluent API with `HasConversion<string>()` for all enums. Never use magic numbers. DB records must be human-readable.

**ADR-004: Realtime infrastructure**
Sprint 1 (dev env): SignalR Hub in-memory is acceptable. **Before staging/production deploy:** Redis Backplane must be configured and integrated to support horizontal scaling.

**ADR-005: TraceId middleware**
Integrate `X-Trace-Id` middleware in the Web API. Use `Serilog LogContext` to propagate the trace ID across all processing layers and return it in the HTTP Response Header. YARP must forward this header downstream.

**ADR-006: Windows Authentication for local DB**
SQL Server 2022 on local/dev must use Windows Authentication (`Trusted_Connection=True`). Never embed plaintext passwords in connection strings.

**ADR-007: YARP as API Gateway**
YARP (Yet Another Reverse Proxy) sits between Pancake (T-0) and the ASP.NET Core backend (T-1). It owns: route configuration, HMAC pre-validation for webhook endpoints, rate limiting, and trace header injection. YARP config lives in `Clawbot.Api/Gateway/`. Reason: decouples the external surface area from internal routing logic; allows adding new upstream sources (new Pancake channels, direct webhooks) without touching business logic.

---

## 7. LESSONS LEARNED

**LESSON-001: EF Core global filter placement**
Soft delete global query filters **must** be declared inside `OnModelCreating`. Declaring them in `OnConfiguring` causes EF Core to silently ignore them, leaking soft-deleted records into query results.

**LESSON-002: PII risk on webhook endpoints**
Endpoints receiving raw Pancake webhook payloads **must never log raw payload**. Logging message content, customer names, or phone numbers violates PII data protection laws. Log only sanitized metadata (message ID, channel, timestamp).

**LESSON-003: Webhook security gate**
HMAC-SHA256 signature validation is a **hard rule (Layer 1)**. Validate the signature successfully at the first intercept point (YARP middleware or dedicated filter) before deserializing the payload. A request that fails HMAC must be rejected with `401` — never passed to the backend.

**LESSON-004: YARP route ordering**
YARP evaluates routes top-to-bottom. More specific routes (e.g. `/webhook/pancake`) must be declared before broad catch-all routes. Incorrect ordering causes webhook traffic to be routed to wrong clusters silently.

---

## 8. CURRENT SPRINT STATUS

**Sprint 1 focus — Core infrastructure:**
- EF Core entities + migrations
- Serilog JSON logging
- OpenTelemetry (OTel) setup
- SignalR Hub (realtime dashboard)
- Webhook stub APIs (receive Pancake events)
- YARP gateway initial route configuration

**Blocked (deferred to Sprint 2):**
- RabbitMQ message publishing
- Polly resilience wrapping around external calls
- Hangfire background jobs

**Next tasks:**
1. Implement HMAC-SHA256 validation filter for Pancake webhooks
2. Parse raw webhook payload → map to `IncomingMessage` entity
3. Push `IncomingMessage` to RabbitMQ queue

---

## 9. MANDATORY CODING PATTERNS

### Repository Pattern
`Domain` layer defines only interfaces as `IRepository<T>` in `Domain/Interfaces/`. `Infrastructure` layer is solely responsible for implementing them with EF Core. Never let Application reference EF Core directly.

### Unit of Work Pattern
All mutating operations in the `Application` layer must call `IUnitOfWork.SaveChangesAsync()` at the end of the handler to ensure transactional integrity.

### Domain Events
Entity raises event on state change → dispatch event immediately after Unit of Work saves successfully → publish event to RabbitMQ.

### Error Response Format
All API endpoints must return errors in the standard JSON format:
```json
{ "errorCode": "...", "message": "...", "requestId": "..." }
```
Never expose stack traces or internal exceptions to the client.

### Logging Injection
Always use Dependency Injection to inject `ILogger<T>` via constructor. Never call static methods like `Serilog.Log.*` directly in the `Api` or `Application` layers.

### EARS Comment Annotation
All business logic code must have an EARS comment immediately above it for audit purposes:
```csharp
// EARS[WHEN <trigger> THE SYSTEM SHALL <behavior>]
```

### Graceful Degradation
When one agent fails mid-execution, the orchestrator must re-assign or skip with controlled fallback — not propagate the failure to the entire response. Implement at the business logic level, not just infrastructure.

### YARP Route Config Pattern
All YARP routes and clusters are defined in `appsettings.json` under the `ReverseProxy` section, not in code. Use `IProxyStatefulFeature` only when dynamic routing is explicitly required.

---

## 10. NON-FUNCTIONAL REQUIREMENTS (NFR TARGETS)

| Dimension | Target | Measurement |
|---|---|---|
| Latency (chat) | p95 first token < 2s, full response < 8s | Serilog timing + `/metrics` endpoint |
| Throughput | ~hundreds of concurrent sessions | Queue offloads heavy work; request path stays unblocked |
| Availability | ~99.5% (single-node + auto-restart + degradation) | 99.9%+ requires HA — see upgrade thresholds |
| Recovery | Process crash → auto-restart < 10s | Docker restart policy + health check |
| Data durability | No loss of non-reproducible data | Object storage for originals + daily DB backup |
| Cost | LLM cost per session bounded | Flash default + context caching; track tokens/session on dashboard |

---

## 11. UPGRADE THRESHOLDS (Scale when these are hit)

| Trigger | Action |
|---|---|
| > ~1k users / need uptime SLA | Horizontal scale backend (stateless, already ready) |
| DB becomes bottleneck | Read replica first → Always On AG / Patroni for write HA |
| Redis is single point of failure | Redis Sentinel / Cluster for failover |
| Queue overloaded / durability critical | RabbitMQ quorum queue + multiple consumers |
| Need deep incident tracing | OpenTelemetry + Jaeger distributed tracing + Sentry |
| Large team / independent deployments | Split microservices + gRPC by bounded context |
