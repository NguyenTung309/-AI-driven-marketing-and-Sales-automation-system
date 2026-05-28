# AGENTS.md — Project Context for AI Coding Agents

> Loaded by Claude Code / Codex / Copilot at the start of every session. Keep concise — high signal.

## 1. PROJECT OVERVIEW

ClawBot SaleMkt — Omnichannel sales automation for a Chinese-language tutoring business. Aggregates DM + comments from **Zalo, Facebook, TikTok, Instagram, YouTube** into a unified inbox. **8 AI agents** + **5 humans** + **Chinese-language Knowledge Base** = 24/7 consultation, 3× sales productivity.

See `docs/ClawBot_SaleMkt_ProjectPlan.docx` for full product plan and `docs/spec-audit.md` for traceability.

## 2. TECH STACK (STRICT — do not deviate without RFC)

- .NET 8 / C# 12 / ASP.NET Core minimal APIs
- EF Core 8 + Microsoft.EntityFrameworkCore.SqlServer
- gRPC for agent ↔ orchestrator
- Anthropic Claude Sonnet 4.6 via Semantic Kernel
- **Microsoft SQL Server 2022**, Redis 7, RabbitMQ 3 (MassTransit)
- Qdrant (primary vector store — SQL Server stores JSON snapshot), MinIO (S3)
- React 19 + Vite + Tailwind + Zustand + TanStack Query
- Serilog + OpenTelemetry + Metabase

See `.sdd/constitution.md` Article 1 for the locked list.

## 3. ARCHITECTURE PRINCIPLES

- **Layers**: Domain → Application → Infrastructure → Api / AgentService.
- **Domain has zero external dependencies** — no EF, no Pgvector, no MediatR.
- **Bounded contexts**: `Tenants, Contacts, Conversations, KnowledgeBase, ChatScenarios, Agents, Leads, SaleAssist, Documents, Content, Ads, Analytics, Security` — one folder per context under `src/shared/Clawbot.Domain`.
- **Multi-tenant** via `ITenantOwned` + EF query filter. Every tenant-scoped row carries `tenant_id`.
- **DDL is source of truth** (`deploy/migrations/0001_init.sql`). EF Core maps to it; do NOT run `EnsureCreated` or generate migrations without coordinating with the DDL.
- **Agents** are gRPC services in `Clawbot.AgentService`; they read SKILL.md + KB modules and emit traces to `agent_traces`.

## 4. FILE NAMING & STRUCTURE

```
src/
├── shared/
│   ├── Clawbot.Domain/<Context>/<Aggregate>.cs
│   ├── Clawbot.Application/Modules/<Context>/Commands|Queries/...
│   ├── Clawbot.Infrastructure/Persistence/Configurations/*.cs
│   └── Clawbot.SharedKernel/(Multitenancy|Time|Vectors|Channels|Security)
├── api/Clawbot.Api/Endpoints/<Context>Endpoints.cs
└── agents/
    ├── Clawbot.Agents.Contracts/ (gRPC stubs from /proto)
    ├── Clawbot.Agents.Core/ (IAgent, orchestrator)
    └── Clawbot.AgentService/Services/<Agent>GrpcService.cs

proto/<service>.proto         (gRPC contracts)
.sdd/
├── constitution.md            (immutable rules)
├── AGENTS.md                  (this file)
├── specs/<feature>/SPEC.md    (EARS notation)
└── skills/<skill>.md          (per-agent prompt skills)
deploy/
├── docker-compose.yml
└── migrations/0001_init.sql   (DDL — source of truth)
docs/
├── ClawBot_SaleMkt_ProjectPlan.docx   (product plan)
├── spec-driven-&-agent-driven-development.pdf (methodology)
├── spec-audit.md              (FR → code traceability)
├── erd.md                     (Mermaid ERD)
└── erd-notion.md
```

## 5. FORBIDDEN PATTERNS

- ❌ Storing secrets in source files / `appsettings.json` committed to repo.
- ❌ String-concat SQL.
- ❌ Mutable aggregate state via public setters.
- ❌ Logic in controllers / endpoints — push to handlers in `Application`.
- ❌ Hardcoded prompts inside agent service code — load from `kb_versions` / SKILL.md.
- ❌ Calling LLM from Domain or Application layer — only from `Clawbot.AgentService`.
- ❌ Skipping audit log on security-sensitive actions.

## 6. DEFINITION OF DONE

A change is done when:
- [ ] Tests written first (TDD), passing locally and in CI.
- [ ] 80%+ coverage on touched Domain/Application code.
- [ ] EF mapping updated AND `deploy/migrations/0001_init.sql` updated (or new `000X_*.sql`).
- [ ] SPEC.md cross-referenced (`SPEC-XX`, `UC-Yzz`).
- [ ] No new TODO without an issue link.
- [ ] `dotnet format` clean. `dotnet build` clean (0 warnings).
- [ ] PR description ties back to the user-visible outcome.

## 7. GIT CONVENTIONS

- Branches: `feat/<scope>`, `fix/<scope>`, `chore/<scope>`.
- Commit format: `<type>(<scope>): <summary>` (conventional commits).
- Squash on merge. Reference SPEC / UC in commit body.
- Never `--no-verify`. Never force-push `main`.

## 8. CURRENT SPRINT CONTEXT

Sprint 0 (planning + scaffolding):
- DDL applied to local Postgres
- 12 Domain bounded contexts stubbed
- 8 agent gRPC services stubbed (chat + 7 more)
- `.sdd/` artifacts authored

Active SPEC files: `.sdd/specs/01-omnichannel-inbox`..`10-admin-security`.

Next sprint focus: SPEC-02 Knowledge Base ingestion + SPEC-01 Pancake/Zalo channel adapter.

## 9. Skill Catalog

31 skill (9 prompt/process + 22 utility/library-backed) — full catalog and Agent ↔ Skill matrix in [.sdd/skills/_index.md](skills/_index.md).

Utility skills have C# adapter interfaces at `src/agents/Clawbot.Agents.Core/Skills/{Nlp,Lead,Content,Ops}/I*Skill.cs`. Wired into DI via `SkillsModule.AddClawbotSkills()` (called from `Clawbot.AgentService/Program.cs`).

Stub impls throw `NotImplementedException` — concrete wiring lands per-skill behind a SPEC.

---

When in doubt, prefer reading `.sdd/constitution.md`, then `docs/spec-audit.md`, then the relevant `SPEC.md` under `.sdd/specs/`. Don't infer from code — the spec is the contract.
