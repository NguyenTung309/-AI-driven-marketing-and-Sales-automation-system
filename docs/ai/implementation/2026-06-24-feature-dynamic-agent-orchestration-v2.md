---
phase: implementation
title: Dynamic Agent Orchestration v2 — Implementation Guide
description: Technical implementation notes for autonomous scheduled A2A orchestration
feature: dynamic-agent-orchestration-v2
date: 2026-06-24
status: draft
---

# Dynamic Agent Orchestration v2 — Implementation Guide

## Context Quality Assessment

- Existing orchestration code lives under `src/agents/Clawbot.AgentService` and `src/agents/Clawbot.Agents.Core`.
- Existing run state lives in `src/shared/Clawbot.Domain/Agents/AgentSession.cs` and SQL `agent_sessions`/`agent_traces`.
- Existing LLM config entity stores encrypted keys in `src/shared/Clawbot.Domain/Llm/LlmConfig.cs`.

## Executive Summary

Implement V2 by extending existing orchestration, not replacing it. Add data-defined sub-agents, persisted A2A mailbox, schedule worker, and encrypted local LLM seed. Keep autonomous behavior bounded by cost, rounds, runtime, cancellation, RBAC, and approval gates.

## Development Setup

- Use local SQL Server via existing `run-all.bat` flow.
- Use local OpenAI-compatible endpoint: `http://localhost:20128/v1`.
- Set `CLAWBOT_DEMO_LLM_API_KEY` before demo seed.
- Do not put real or local disposable keys into SQL files, docs, `appsettings*.json`, or tests.

## Code Structure

Recommended files:

- `src/shared/Clawbot.Domain/Agents/AgentDefinition.cs`
- `src/shared/Clawbot.Domain/Agents/AgentA2AMessage.cs`
- `src/shared/Clawbot.Domain/Agents/AgentSchedule.cs`
- `src/shared/Clawbot.Domain/Agents/AgentScheduleRun.cs`
- `src/shared/Clawbot.Infrastructure/Persistence/Configurations/AgentV2Configurations.cs`
- `src/agents/Clawbot.Agents.Core/Orchestrator/AutonomousOrchestrator.cs`
- `src/agents/Clawbot.Agents.Core/Orchestrator/A2AMailbox.cs`
- `src/agents/Clawbot.Agents.Core/Orchestrator/AgentDefinitionCatalog.cs`
- `src/agents/Clawbot.AgentService/Services/AgentScheduleWorker.cs`
- `src/agents/Clawbot.AgentService/Services/RecurrenceCalculator.cs`
- `src/api/Clawbot.Api/Endpoints/OrchestrationV2Endpoints.cs`
- `src/frontend/clawbot-web/src/features/agents/AgentDashboardPage.tsx` or dedicated orchestration feature folder.

## Implementation Notes

### Core Features

- **Sub-agent definitions:** seed fixed V1 agents as definitions, then allow new persona rows.
- **A2A mailbox:** persisted message lifecycle with tenant/session ownership.
- **Coordinator loop:** bounded loop: plan → delegate → receive result → review → replan/finalize.
- **Scheduler:** due schedule scan, idempotent window claim, session creation.
- **LLM seed:** local provider upsert with encrypted API key and no plaintext persistence.

### Patterns & Best Practices

- Use C# records for immutable DTOs; entities remain classes.
- Use explicit state transitions on entities.
- Keep SQL migrations small and no `GO`.
- Use parameterized SQL for any seed/upsert command that handles API keys.
- Use `CancellationToken` through all async orchestration APIs.

## Integration Points

- LLM: existing `ScopedLlmChatClient` + `ILlmCallScope`.
- Cost: existing `OrchestratorCostGuard` / ledger.
- Trace: existing `agent_traces` plus V2 A2A message rows.
- RBAC: existing `orchestration:view/run/approve/manage` permissions.
- Demo runner: `run-all.bat` can grow a seed option, but should never echo key.

## Error Handling

- Invalid goal → 400/InvalidArgument `goal_required`.
- Missing LLM config → FailedPrecondition `llm_config_required`.
- Schedule overlap → schedule run status `skipped_overlap`.
- Cost cap → session status `failed`, trace `cost_cap`.
- Cancel/pause → stop claiming new A2A messages; current task honors cancellation when possible.
- Key seed encryption failure → abort seed and leave existing config unchanged.

## Performance Considerations

- Cap coordinator rounds, worker concurrency, and per-run runtime.
- Add indexes on `agent_schedules(is_active,next_run_at)` and `agent_a2a_messages(tenant_id,session_id,status,created_at)`.
- Avoid N+1 loading of message timelines; page trace/A2A results.

## Security Notes

- Never log plaintext key or full prompt payload with PII.
- Redact derived chat/document content before persistence.
- Enforce tenant filters on all new tables.
- External actions require approval gate even when schedule auto-runs.

## Acceptance Criteria

- New V2 runtime composes data-defined sub-agents.
- A2A message timeline survives process restart.
- Schedule creates exactly one run per window.
- Encrypted local LLM seed config resolves for `orchestrator`.

## Traceability Matrix

| Requirement | Implementation Note |
|---|---|
| SK coordinator | `AutonomousOrchestrator` |
| Sub-agents as data | `AgentDefinition` + catalog |
| A2A collaboration | `AgentA2AMessage` + mailbox |
| Schedules | `AgentScheduleWorker` + calculator |
| LLM seed | demo seed path using encryption service |
| Guardrails | RBAC/cost/cancel/max rounds |

## Review Notes

- Keep V2 implementation behind new endpoints or feature flag until stable.
- Do not remove V1 endpoints while demo docs reference V1 foundation.
