# SPEC-03 — AI Agent Management

Status: `DRAFT`
Traces to: FR-03, UC-A02..A08 (agent invocation), SW-035..046

## 1. Business Context

8 agents (Chat, SaleAssist, Lead, Content, Docs, Ads, Report, Research) run as gRPC services. Need CRUD + start/stop + accuracy/latency/cost metrics + pixel-office visualization.

## 2. User Stories

- AS AN Admin I WANT to start/stop an agent SO THAT I can isolate misbehaving agents.
- AS A QA I WANT to send a test prompt SO THAT I can validate KB + SKILL.md changes before deploy.

## 3. Acceptance Criteria (EARS)

- THE SYSTEM SHALL list 8 agents from the `agents` table with `(code, agent_type, status, model)`.
- WHEN `Start` is invoked AND `status = 'stopped'` THE SYSTEM SHALL transition to `running` and emit gRPC ready signal.
- WHEN any agent crashes 3 times within 5 min THE SYSTEM SHALL transition status to `error` and Telegram-alert ops.
- WHILE `status = 'running'` THE SYSTEM SHALL stream traces to `agent_traces`.
- WHEN a `kb_versions.status` becomes `deployed` THE SYSTEM SHALL trigger SIGHUP-style reload to agents referencing that module.

## 4. API Contracts / Data Models

- `GET /api/agents` — list
- `POST /api/agents/{id}/start|stop|restart`
- `POST /api/agents/{id}/test` — single-prompt test
- gRPC: `Orchestrator.Plan`, `<Agent>Grpc.Reply / Score / Generate / ...`
- Tables: `agents`, `agent_sessions`, `agent_traces`.

## 5. Technical Constraints

- All agents implement `IAgent` (`Clawbot.Agents.Core`).
- `AgentRegistry` is singleton; reload via DI scope refresh.
- Trace writes batched every 1s or 100 events.

## 6. Out of Scope

- Auto-scaling agents across multiple nodes (Phase 2).

## 7. NFR

NFR-02 (auto-recovery), NFR-01 (start/stop p95 <500ms).

## 8. Error Handling Matrix

| Error | Detection | User-visible | Recovery |
|---|---|---|---|
| Agent OOM | docker exit code | "agent error" status | auto-restart x3, then `error` |
| gRPC deadline | client-side | "agent timeout" toast | escalate to human |

## 9. Open Questions

| Item | Owner | Due | Status |
|---|---|---|---|
| Per-agent rate limits inside Claude API budget | P5 | T3 | open |
