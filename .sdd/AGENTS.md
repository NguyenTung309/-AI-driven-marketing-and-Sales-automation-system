# AGENTS.md — ClawBot Multi-Agent System
# Entry point for Codex / OpenAI coding agents.
# BEFORE EXECUTING: Read `.sdd/CONSTITUTION.md` for top-level hard rules that override everything here.
# For current sprint state and active SPECs: read `.sdd/specs/` directory.

> Keep this file in sync with `CLAUDE.md`. Differences: this file omits the volatile sprint section
> (Section 8 of CLAUDE.md) and replaces it with a pointer to `.sdd/specs/`. All conventions,
> patterns, and hard rules are identical.

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

---

## 2. PROJECT OVERVIEW

**ClawBot SaleMkt** is an AI-driven multi-agent marketing & sales automation system for Vietnamese SMEs (initial focus: Chinese-language tutoring business). It aggregates DMs + comments from **Zalo OA, Facebook, TikTok, Instagram, YouTube** into a unified inbox, processes them with **8 AI agents + 5 humans + a Chinese-language Knowledge Base**, and delivers 24/7 consultation and 3× sales productivity.

**Scale target:** ~hundreds of concurrent users, single-node + resilience (not full HA cluster).

See `docs/ClawBot_SaleMkt_ProjectPlan.docx` for full product plan and `docs/spec-audit.md` for traceability.

---

## 3. TECH STACK (strict — do not deviate without RFC)

- **Backend**: .NET 8, C# 12, ASP.NET Core minimal APIs, EF Core 8 + `Microsoft.EntityFrameworkCore.SqlServer`
- **API Gateway**: YARP — standalone project `Clawbot.Gateway` (T-0.5)
- **gRPC**: `Grpc.AspNetCore` 2.66, proto3. Contracts in `proto/`
- **AI / Orchestration**: Microsoft Semantic Kernel + CrewAI + Langflow
- **LLM**: Resolved at runtime from `llm_configs` table — never hardcoded. Primary: Claude Sonnet 4.6. Cost-optimized: DeepSeek V4 flash/pro
- **Database**: **Microsoft SQL Server 2022**. DDL in `deploy/migrations/0001_init.sql` is source of truth
- **Cache / Queue**: Redis 7, RabbitMQ 3 + MassTransit
- **Vector store**: Qdrant (primary). SQL Server stores JSON embedding snapshot
- **Object storage**: MinIO (S3-compatible)
- **Frontend**: React 19 + Vite + TypeScript + Tailwind + Zustand + TanStack Query
- **Observability**: Serilog (structured JSON), OpenTelemetry, Metabase BI
- **Deploy**: Docker Compose (single VPS, 8 GB RAM). See `deploy/docker-compose.yml`

Adding a dependency requires a one-page RFC under `.sdd/rfcs/`.

---

## 4. ARCHITECTURE: 6-TIER STACK

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

### T-3: Agent catalog
| Agent code | Role |
|---|---|
| `orchestrator` | plan → dispatch → observe → re-plan |
| `chat` | Customer-facing chat, RAG + memory |
| `content` | Content generation (posts, copy) |
| `lead` | Lead scoring, qualification, reporting |
| `kb-ingest` | Knowledge Base ingestion + embedding |
| `ads` | Ad campaign suggestions + performance analysis |
| `sale-assist` | Human sales rep assistance |
| `analytics` | Dashboard metrics, trend summarization |

---

## 5. FILE NAMING & STRUCTURE

```
ClawBot.sln
├── src/
│   ├── api/Clawbot.Api/Endpoints/<Context>Endpoints.cs
│   ├── gateway/Clawbot.Gateway/
│   │   ├── Middleware/(TraceIdMiddleware|PancakeHmacMiddleware).cs
│   │   └── appsettings.json              # ReverseProxy routes/clusters config
│   ├── shared/
│   │   ├── Clawbot.Domain/<Context>/<Aggregate>.cs
│   │   ├── Clawbot.Application/Modules/<Context>/Commands|Queries/...
│   │   ├── Clawbot.Infrastructure/Persistence/Configurations/*.cs
│   │   └── Clawbot.SharedKernel/(Multitenancy|Time|Vectors|Channels|Security)
│   └── agents/
│       ├── Clawbot.Agents.Contracts/     # gRPC stubs from /proto
│       ├── Clawbot.Agents.Core/          # IAgent, orchestrator, ISkill interfaces
│       └── Clawbot.AgentService/Services/<Agent>GrpcService.cs
├── tests/
│   ├── Clawbot.UnitTests/
│   └── Clawbot.IntegrationTests/
├── proto/<service>.proto
├── deploy/
│   ├── docker-compose.yml
│   └── migrations/0001_init.sql          # DDL — source of truth
└── .sdd/
    ├── CONSTITUTION.md                   # Hard rules
    ├── AGENTS.md                         # This file
    ├── rfcs/
    ├── specs/<feature>/SPEC.md
    └── skills/<agent-code>.md
```

**Dependency flow:**
```
Gateway → (zero project refs — infrastructure only)
Api → Application → Domain ← Infrastructure
AgentService → Agents.Core → Domain
```

**Bounded contexts**: `Tenants, Contacts, Conversations, KnowledgeBase, ChatScenarios, Agents, Leads, SaleAssist, Documents, Content, Ads, Analytics, Security`

---

## 6. COMMON COMMANDS

```bash
dotnet build ClawBot.sln
dotnet run --project src/gateway/Clawbot.Gateway     # port 5050
dotnet run --project src/api/Clawbot.Api             # port 5051
dotnet run --project src/agents/Clawbot.AgentService
dotnet test ClawBot.sln
dotnet test tests/Clawbot.UnitTests
dotnet ef migrations add <Name> \
  --project src/shared/Clawbot.Infrastructure \
  --startup-project src/api/Clawbot.Api
dotnet ef database update \
  --project src/shared/Clawbot.Infrastructure \
  --startup-project src/api/Clawbot.Api
docker compose -f deploy/docker-compose.yml up -d
dotnet format ClawBot.sln --verify-no-changes
```

---

## 7. ARCHITECTURE DECISION RECORDS (ADR)

**ADR-001**: Clean Architecture — 4 shared projects, dependency strictly inward. Gateway isolated.
**ADR-002**: Soft delete — `BaseEntity` with `IsDeleted`+`DeletedAt`, global filter in `OnModelCreating`.
**ADR-003**: Enums as string in DB via `HasConversion<string>()`. No magic numbers.
**ADR-004**: SignalR in-memory for dev; Redis Backplane required before staging.
**ADR-005**: `X-Trace-Id` injected at YARP, propagated via `Serilog LogContext`, returned in response header.
**ADR-006**: Windows Auth (`Trusted_Connection=True`) for local SQL Server. No plaintext passwords.
**ADR-007**: YARP as standalone `Clawbot.Gateway`. Config in `appsettings.json` under `ReverseProxy`. Zero project refs to other ClawBot projects.
**ADR-008**: gRPC for agent transport. Proto contracts in `proto/` are source of truth.
**ADR-009**: DDL (`deploy/migrations/`) is schema source of truth. EF Core maps to it — never generates schema.
**ADR-010**: LLM resolved at runtime from `llm_configs` table. Never hardcoded in agent code.
**ADR-011**: Multi-tenancy via `ITenantOwned` + EF global query filter. Never manual `tenant_id` filter in queries.

---

## 8. CURRENT SPRINT STATE

> Do NOT rely on this section for task planning — it is intentionally minimal here.
> Read `.sdd/specs/` for active SPEC files and authoritative task state.
> Read `CLAUDE.md` Section 8 for the verbose sprint notes (updated by the team each sprint).

Active SPEC files: `.sdd/specs/01-omnichannel-inbox` .. `10-admin-security`
Next sprint focus: SPEC-02 Knowledge Base ingestion + SPEC-01 Pancake/Zalo channel adapter.

---

## 9. MANDATORY CODING PATTERNS

### Repository Pattern
`Domain` defines `IRepository<T>`. `Infrastructure` implements with EF Core. `Application` never references EF Core.

### Unit of Work
All mutating `Application` handlers call `IUnitOfWork.SaveChangesAsync()` at the end.

### Domain Events
Entity raises event → dispatch after UoW saves → publish to RabbitMQ via MassTransit.

### Multi-tenancy
Entities implement `ITenantOwned`. EF global filter handles isolation. Inject `ITenantContext` — never manual filter.

### Error Response Format
```json
{ "errorCode": "...", "message": "...", "requestId": "..." }
```
Never expose stack traces to clients.

### Logging Injection
Inject `ILogger<T>` via constructor. Never `Serilog.Log.*` static in `Api`, `Application`, `AgentService`.

### EARS Comment Annotation
```csharp
// EARS[WHEN <trigger> THE SYSTEM SHALL <behavior>]
```

### Graceful Degradation
Agent failure → orchestrator re-assigns or skips with fallback. Never propagate to full pipeline.

### YARP Route Config
Routes/clusters in `appsettings.json` only. Never in C# unless dynamic routing is explicitly required.

### gRPC Agent Pattern
`AgentService` is thin — delegates to `Agents.Core`. Prompts from `kb_versions`. Traces to `agent_traces`.

### Forbidden Patterns
- ❌ Secrets in source / committed config
- ❌ String-concat SQL
- ❌ Mutable aggregate public setters
- ❌ Logic in controllers/endpoints
- ❌ Hardcoded LLM prompts in agent code
- ❌ LLM calls from Domain or Application layer
- ❌ Skipping audit log on security-sensitive actions
- ❌ Manual `tenant_id` filtering in queries

---

## 10. DEFINITION OF DONE

- [ ] TDD — tests written first, passing locally and in CI
- [ ] 80%+ coverage on touched Domain/Application code
- [ ] EF mapping updated AND `deploy/migrations/` updated if schema changed
- [ ] SPEC.md cross-referenced (`SPEC-XX`, `UC-Yzz`)
- [ ] No new TODO without an issue link
- [ ] `dotnet format` clean. `dotnet build` clean (0 warnings)
- [ ] PR description ties back to user-visible outcome

---

## 11. SKILL CATALOG

31 skills total (9 prompt/process + 22 utility/library-backed).
Full catalog and Agent ↔ Skill matrix: `.sdd/skills/_index.md`

Utility skills have C# adapter interfaces at:
`src/agents/Clawbot.Agents.Core/Skills/{Nlp,Lead,Content,Ops}/I*Skill.cs`

Wired into DI via `SkillsModule.AddClawbotSkills()` in `Clawbot.AgentService/Program.cs`.
Stub implementations throw `NotImplementedException` — concrete wiring lands per-skill behind a SPEC.

---

When in doubt: read `.sdd/CONSTITUTION.md` first, then `docs/spec-audit.md`, then the relevant `SPEC.md` under `.sdd/specs/`. Don't infer from code — the spec is the contract.