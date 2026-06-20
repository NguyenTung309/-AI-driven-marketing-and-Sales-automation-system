---
phase: deployment
title: Deployment Strategy
description: Define deployment process, infrastructure, and release procedures
feature: llm-provider-config
date: 2026-06-20
---

# Deployment Strategy — llm-provider-config

> **This is a hard-cutover release (D1/D3).** After deploy, every agent returns
> `llm_config_not_configured` until its tenant creates + activates an `LlmConfig` and binds it
> to the agent. There is **no env/AnthropicOptions fallback** (removed). Plan a maintenance window.

## Infrastructure
- No new infra. Same API host (net8), AgentService (gRPC), SQL Server, Qdrant, Redis, RabbitMQ.
- New secret material (provider API keys) is stored **encrypted-at-rest** in `llm_configs.api_key_encrypted`
  via the existing `IEncryptor`/`AesEncryptor` (key from the `Encryption` config section — unchanged).

## Database Migrations
Applied by the `deploy/migrations/*.sql` runner (Testcontainers/CI globs the folder, one batch per file):
1. `0025_llm_config_cost_fields.sql` — adds `display_name`, `input_usd_per_1m`, `output_usd_per_1m` to `llm_configs`.
2. `0026_agents_llm_config_id.sql` — adds nullable `agents.llm_config_id` (column only).
3. `0027_agents_llm_config_fk.sql` — adds `ix_agents_llm_config_id` + FK → `llm_configs(id)` `ON DELETE SET NULL`.

All three are idempotent (`IF COL_LENGTH ... IS NULL` / `IF NOT EXISTS`). No data backfill (no auto-seed, D3).
Backup the DB before running, per standard procedure.

## Secrets Management
- Provider keys entered through the UI / `POST /api/llm-configs`; encrypted before persist; never returned
  (masked as `hasApiKey`) and never logged.
- **Dead config (D8):** `Content:Llm:*` env/appsettings values are **no longer read** — `ContentAgent` now
  resolves its provider through the per-tenant `LlmConfig` like every other agent. Remove those entries (or
  leave them — they are inert). The **content-agent must be bound + activated** to a config (step 5d) or
  content generation returns `llm_config_not_configured`.
- Rotation: `POST /api/llm-configs/{id}/rotate-key` (no redeploy).
- The `Encryption` key remains the root secret — if it rotates, all stored provider keys must be re-entered.

## Deployment Steps (maintenance window)
1. **Pre-deploy comms** — notify tenant admins of the window; agents will be paused until reconfigured.
2. **Backup** the database.
3. **Deploy** API + AgentService + frontend; run migrations `0025`→`0027` (in order).
4. **Verify RBAC** — confirm `llm-configs:manage` seeded and assigned to `Admin` (RbacSeeder runs on startup).
5. **Per tenant** (admin, or ops on their behalf):
   a. Open **Cấu hình mô hình AI** (`/llm-providers`), add a provider config (provider, model, API key, baseUrl?).
   b. Click **Kiểm tra** (test-connection) — expect `OK · <ms>`.
   c. **Activate** the config.
   d. In **Agent settings** drawer, bind **each** agent — including **content-agent** — to the config
      (dropdown). Ensure the agent's **model** field is a real model id (legacy seed value `"claude"` is a
      placeholder and would be sent literally — D2: per-agent model overrides config model). A bind/model
      edit whose model doesn't match the provider is rejected with `model_provider_mismatch` (D9:
      anthropic⇒`claude-*`, openai⇒non-`claude`).
   - **baseUrl format (D10):** enter the host only; the API normalizes it per provider (openai gets `/v1`
     appended, anthropic keeps the bare host). No need to hand-craft the suffix.
6. **Smoke test** — send a chat message per tenant; confirm a reply + a cost entry recorded.
7. **Close window**; resume normal comms.

## Post-deployment validation
- `GET /api/llm-configs` returns configs with `hasApiKey:true`, no plaintext.
- Chat/sale-assist replies succeed for a bound+active tenant.
- An agent left unbound returns `llm_config_not_configured` (not a 500) and is flagged in the UI.
- Cost ledger entries show the resolved model (not hardcoded `claude`).

## Rollback Plan
- **Trigger:** widespread `llm_config_not_configured` that can't be cleared by reconfiguring, or a provider-call regression.
- **Steps:** redeploy the previous app build. The migrations are additive + nullable, so the prior build runs
  against the new schema unchanged (it ignores `llm_config_id` and the new `llm_configs` columns).
  No schema rollback required; if desired, the FK/index/columns can be dropped later out-of-band.
- **Caveat:** the previous build read `Anthropic:*` env/appsettings — ensure those values are still present in
  the environment if rolling back (this release removes the runtime read but not necessarily the env entries).
- **Comms:** notify tenant admins of rollback + that reconfiguration will be needed again on the next attempt.

## CI/CD Notes
- `deploy/ci/verify-migrations.ps1` validates migration ordering — confirm `0025`–`0027` pass.
- Integration tests (`Clawbot.Integration.Tests`) apply the `.sql` migrations to a real SQL container; the new
  files run automatically. Test fixups for the chat-client ctor change are tracked in planning task 3.5.
