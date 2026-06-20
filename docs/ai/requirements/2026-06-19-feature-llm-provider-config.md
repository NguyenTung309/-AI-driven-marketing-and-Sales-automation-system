---
phase: requirements
title: Requirements & Problem Understanding
description: Clarify the problem space, gather requirements, and define success criteria
feature: llm-provider-config
date: 2026-06-19
---

# Requirements & Problem Understanding

## Problem Statement
**What problem are we solving?**

- The original intent was a feature to **configure agent LLM providers (OpenAI- or Anthropic-standard) from the product**, per tenant. During implementation this drifted into hardcoded **environment variables / `appsettings` (`Anthropic` section)**: `AnthropicChatClient` reads a single static `AnthropicOptions` (`ApiKey`, `Model`, `BaseUrl`, `MaxTokens`) via `IOptions`. There is no way for a tenant admin to set or rotate credentials, pick a model, or point at an OpenAI-compatible endpoint without a redeploy.
- A per-tenant domain aggregate `LlmConfig` (`src/shared/Clawbot.Domain/Llm/LlmConfig.cs`) **already exists and is mapped/migrated** (`llm_configs` table) — provider, model, encrypted API key, base URL, max tokens, temperature, active flag, key rotation. It is **orphaned**: no API endpoint, no UI, and the runtime never reads it.
- **Affected:** tenant admins (cannot self-serve provider/model/credentials), the ops team (every credential change = redeploy / env edit), and multi-tenant correctness (one shared key for all tenants).

- **Current workaround:** edit `appsettings.*.json` / set `Anthropic:*` env vars and redeploy; single global Anthropic credential; OpenAI only wired for RAG embeddings (`OpenAiEmbeddingProvider`), not for chat/content.

## Goals & Objectives
**What do we want to achieve?**

- **Primary**
  - CRUD API + a dedicated **Model / Provider Settings** screen over `LlmConfig`, per tenant, with encrypted-at-rest credentials and a "test connection" action.
  - Support **Anthropic** and **OpenAI-compatible** providers (the latter via `BaseUrl` override → covers Azure OpenAI, vLLM, local proxies).
  - **Wire the runtime to actually use `LlmConfig`**: `AnthropicChatClient` (and a new OpenAI chat adapter) resolve the per-agent provider config from the DB at call time, replacing the static `AnthropicOptions` path. Env-var drift goes away.
  - **Per-agent binding:** each `AgentConfig` references one `LlmConfig` by id; different agents can use different providers/models.
- **Secondary**
  - Key rotation and activate/deactivate without redeploy.
  - Never return plaintext keys to the client (mask as `hasApiKey: true`, like `ChannelsEndpoints`).
  - Cost metadata (input/output USD per 1M) configurable per provider config so cost tracking stays accurate.
- **Non-goals**
  - Streaming protocol changes (keep existing `IClaudeChatClient` streaming contract).
  - Provider-specific advanced params beyond model/maxTokens/temperature/baseUrl (function-calling config, tool schemas) — out of scope this iteration.
  - Migrating RAG embeddings off `OpenAiEmbeddingProvider`.
  - BYO secret-manager integration (Key Vault, etc.) — `IEncryptor`/`AesEncryptor` at-rest is sufficient for now.

## User Stories & Use Cases
**How will users interact with the solution?**

- As a **tenant admin**, I want to add an LLM provider config (provider, model, API key, optional base URL, max tokens, temperature) so that my agents call my own account.
- As a **tenant admin**, I want to **test the connection** before saving/activating so that I catch a bad key or wrong base URL early.
- As a **tenant admin**, I want to **rotate the API key** without re-entering all other fields.
- As a **tenant admin**, I want to **activate/deactivate** a config and **delete** unused ones.
- As a **tenant admin**, I want to **assign a specific provider config to each agent** so that e.g. the chat agent uses Claude and the content agent uses an OpenAI model.
- As the **system at runtime**, when an agent runs, I resolve its bound `LlmConfig`, decrypt the key, and call the matching provider client.

- **Key workflows:** create config → test → activate → bind to agent(s) → agent run uses it.
- **Edge cases:**
  - Agent bound to a config that was deleted/deactivated → defined fallback (block run with clear error vs. fall back to tenant default — see Open Items).
  - No config exists yet → graceful "not configured" error, not a 500.
  - Decryption failure (key material rotated server-side) → surfaced as config error, never a stack trace.
  - OpenAI-compatible base URL without trailing-slash / path differences.
  - Concurrent edits to the same config (last-write-wins via `UpdatedAt`).

## Success Criteria
**How will we know when we're done?**

- A tenant admin can create, test, activate, rotate-key, edit, delete, and per-agent-bind provider configs entirely from the UI — **zero redeploy, zero env edits**.
- An agent bound to an Anthropic config and another bound to an OpenAI-compatible config both produce correct replies through their respective live keys.
- API never returns plaintext key material (verified by test).
- The `Anthropic:*` env/appsettings runtime path is **removed**; after the maintenance-window cutover, chat works only via an active `LlmConfig` (D1/D3).
- An agent with no bound/active config returns `llm_config_not_configured` (never a 500), and the UI clearly flags such agents.
- Test coverage ≥ 80% on new endpoint, resolver, and provider-client adapter logic; integration test proves end-to-end resolution + masking.
- Cost entries (`ClaudeCostEntry`) continue to record correct USD using per-config rates.

## Constraints & Assumptions
**What limitations do we need to work within?**

- **Technical**
  - Build gates are strict ([[clawbot-build-gates]]): NuGetAudit + CA analyzers are **errors**; Gateway net10, rest net8, SDK pinned by `global.json` (10.0.300).
  - Migrations ([[clawbot-migration-no-go]]): one `SqlCommand` per file, **no `GO`**; the new FK column on `agents` (`LlmConfigId`) and any index must be in their own migration files. `AppUser` maps to `users`.
  - Multitenancy: `LlmConfig` is `ITenantOwned` and query-filtered; all access via `ITenantAccessor.Require()`.
  - Secrets: reuse `IEncryptor`/`AesEncryptor`; persisted derived text must be PII-safe ([[pii-redact-derived-content]]) — though API keys are secrets, not customer PII, the same "never persist plaintext / never log" discipline applies.
  - RBAC ([[rbac-perm-seed-required]]): a new permission (e.g. `llm-configs:manage`) must be seeded in `RbacSeeder` with exact dot-code match, or endpoints 403.
- **Business / time**
  - Reuse over rebuild: `LlmConfig` domain, `IEncryptor`, `ChannelsEndpoints`/`ApiKeysEndpoints` CRUD pattern, `AgentConfigDrawer`/`agents.ts` FE pattern, `OpenAiEmbeddingProvider` as the OpenAI HTTP reference.
  - **Hard cutover (D3) requires a coordinated maintenance window** — agents are down per tenant until configured. Communicate before deploy.
- **Assumptions**
  - `IClaudeChatClient` is the chat contract; an OpenAI adapter implements the same contract (or a provider-router selects the impl).
  - Streaming + cost-tracking contracts stay stable.

## Resolved Decisions
**Settled in requirements review (2026-06-19):**

- **D1 — Runtime fallback: NONE (hard error).** If an agent's bound `LlmConfig` is missing or inactive at call time, the run is blocked with a typed error `llm_config_not_configured`. No tenant-default fallback, no `AnthropicOptions` fallback in steady state.
- **D2 — Model source of truth: per-agent override.** `AgentConfig.model` (free text), when set, overrides the bound `LlmConfig.ModelId`; `LlmConfig.ModelId` is the default. Keep the existing per-agent `model` field. (Two sources by design — agent override wins.)
- **D3 — Migration: no auto-seed; true hard cutover.** No backfill of `LlmConfig` from env. At deploy, agents return `llm_config_not_configured` until each tenant adds + activates a config. **Requires a coordinated maintenance window.** Legacy `Anthropic:*` env/appsettings path is removed (not kept as fallback).
- **D4 — Cost rates: columns on `LlmConfig`.** Add nullable `InputUsdPer1M`/`OutputUsdPer1M`, default to current Anthropic constants (3.00 / 15.00).
- **D5 — Permission: single `llm-configs:manage`** (mirror `channels:manage`); seeded **and assigned to the tenant-admin role** in `RbacSeeder`.
- **D6 — Test-connection: minimal 1-token ping** per provider via the provider client; isolates a bad key/baseUrl before activation.
- **D7 — Audit: rely on existing `AuditSaveChangesInterceptor`** for create/rotate/activate/deactivate/delete of `LlmConfig` (security events).

## Open Items (carry into design review)
- **baseUrl validation rules** — https-only? reject internal/SSRF hosts when externally reachable? trailing-slash normalization. (Design to specify boundary validation.)
- **Behavior of agents with `LlmConfigId = null` after cutover** — confirmed they hard-error (D1/D3); UI must clearly flag unbound/misconfigured agents so admins find them fast.
- **Maintenance-window runbook** — ordering of deploy vs. tenant config; who communicates the window. (Operational, track in deployment phase.)
