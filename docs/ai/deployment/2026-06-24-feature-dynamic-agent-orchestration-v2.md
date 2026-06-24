---
phase: deployment
title: Dynamic Agent Orchestration v2 — Deployment Strategy
description: Deployment plan for autonomous scheduled A2A orchestration
feature: dynamic-agent-orchestration-v2
date: 2026-06-24
status: draft
---

# Dynamic Agent Orchestration v2 — Deployment Strategy

## Context Quality Assessment

- Deployment touches SQL schema, AgentService background workers, API endpoints, frontend UI, and demo seed flow.
- Rollout should be gated because autonomous schedules can create cost and operational noise.

## Executive Summary

Deploy V2 behind configuration/role gates. Apply DB migrations first, seed local/staging LLM config only in non-production demo environments, then enable scheduler worker and UI. Production should start with schedules disabled until admin explicitly enables them.

## Infrastructure

- Existing local/staging/prod app topology remains.
- AgentService hosts autonomous coordinator and schedule worker.
- SQL Server stores sub-agent definitions, A2A messages, schedules, and schedule runs.
- Existing API/Gateway/frontend host V2 management UI.

## Deployment Pipeline

### Build Process

- `dotnet restore Clawbot.sln`
- `dotnet build Clawbot.sln --no-restore`
- `dotnet test` relevant test projects
- frontend install/build/test if UI changes land

### CI/CD Pipeline

- NuGetAudit/analyzers must remain clean.
- Migration SQL verification must reject `GO`.
- Secret scan must reject committed API keys.

## Environment Configuration

### Development

- Local model endpoint: `http://localhost:20128/v1`.
- Key input: `CLAWBOT_DEMO_LLM_API_KEY` or runner argument.
- Scheduler can be enabled for smoke testing.

### Staging

- Scheduler disabled by default until seed/smoke pass.
- Demo LLM config may point to local/staging-compatible OpenAI endpoint.
- Admin enables specific schedules after RBAC check.

### Production

- Do not seed local demo provider.
- Scheduler disabled by default at first deploy.
- Enable per tenant after acceptance test, cost cap, and approval policy review.

## Deployment Steps

1. Apply migrations for `agent_definitions`, `agent_a2a_messages`, `agent_schedules`, and `agent_schedule_runs`.
2. Deploy AgentService/API/frontend with V2 code disabled or scheduler inactive.
3. Seed base sub-agent definitions.
4. In dev/staging only, seed encrypted OpenAI-compatible demo config from env/CLI.
5. Run smoke: create schedule → run now → verify session + A2A trace.
6. Enable UI access for admin/sales lead roles.
7. Enable scheduler per environment/tenant.

## Database Migrations

- No `GO` statements.
- Indexes on newly added columns get separate migration files if needed.
- Add unique key for schedule idempotency: schedule + window key.
- Add tenant-scoped indexes for due schedule scan and A2A message claiming.

## Secrets Management

- Source of truth for demo key is env/CLI, not SQL file.
- DB stores encrypted key only.
- Logs/dry-run output mask key completely.
- Production uses real secret manager or admin-managed encrypted LLM config path.

## Rollback Plan

- Disable scheduler worker/config first.
- Disable V2 UI routes or hide V2 navigation.
- Keep schema unless destructive rollback explicitly planned; data can remain inert.
- Cancel active V2 sessions if they are still running.

## Acceptance Criteria

- Deployment can ship with V2 disabled and no behavior change.
- Staging smoke proves one scheduled A2A run.
- No plaintext key appears in repo, logs, SQL output, or UI.

## Traceability Matrix

| Requirement | Deployment Control |
|---|---|
| Schedules | scheduler enable flag + per-tenant active flag |
| LLM seed | env/CLI only dev/staging step |
| Guardrails | RBAC/cost/approval checks before enabling |
| A2A persistence | migrations + smoke trace |

## Review Notes

- Do not enable production schedules automatically on deploy.
- Treat demo seed as non-production only unless product owner approves otherwise.
