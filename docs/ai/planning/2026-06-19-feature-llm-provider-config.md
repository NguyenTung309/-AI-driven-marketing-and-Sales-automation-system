---
phase: planning
title: Project Planning & Task Breakdown
description: Break down work into actionable tasks and estimate timeline
feature: llm-provider-config
date: 2026-06-19
---

# Project Planning & Task Breakdown

## Milestones
**What are the major checkpoints?**

- [x] **M1 — Config surface live:** CRUD + rotate/activate API and Model/Provider Settings screen; credentials encrypted; nothing wired to runtime yet (safe to ship behind permission). _(test-connection deferred to 2.7 with the factory)_
- [x] **M2 — Runtime wired:** resolver + factory + OpenAI adapter; consumers call live per-bound `LlmConfig` via ambient `LlmCallScope`; no fallback (D1/D3). Both hosts build green. _(test verification → 3.5)_
- [ ] **M3 — Per-agent binding + polish:** `agents.LlmConfigId` FK ✅, agent-settings bind ✅, AgentConfigDrawer dropdown ✅, AnthropicOptions deletion ✅, runbook ✅; remaining in 3.5: endpoint SSRF/masking integration tests (need Docker harness) + coverage ≥80% measurement (blocked locally — Docker not running).

## Task Breakdown
**What specific work needs to be done?**

### Phase 1: Foundation (config surface — M1) ✅ DONE 2026-06-20
- [x] 1.1 Domain: added `DisplayName`, rate columns (`InputUsdPer1M`/`OutputUsdPer1M`), mutators `UpdateConnection`/`UpdateRates` to `LlmConfig`.
- [x] 1.2 EF: extended `LlmConfigConfiguration` (pinned `input_usd_per_1m`/`output_usd_per_1m` column names — digit-suffix snake_case ambiguity); **migration `deploy/migrations/0025_llm_config_cost_fields.sql`** (idempotent ALTER ADD, single batch). NB: real DDL path is `deploy/migrations/*.sql` (Testcontainers globs it), NOT EF C# migrations.
- [x] 1.3 Contracts: `Clawbot.Api.Contracts/Llm/LlmConfigDtos.cs` (Create/Update/Rotate requests, masked `LlmConfigDto` with `HasApiKey`).
- [x] 1.4 Endpoints: `LlmConfigsEndpoints` (GET/POST/PUT/rotate-key/activate/deactivate/DELETE), boundary validation (provider enum, https-only baseUrl + private-IP SSRF reject, clamps), encrypt via `IEncryptor`, mask on read. Registered in `Program.cs`. _(test route → 2.7)_
- [x] 1.5 RBAC: seeded `llm-configs:manage` → `Admin` in `RbacSeeder.Matrix`.
- [x] 1.6 FE: `shared/api/llmConfigs.ts` + `features/llm-providers/LlmProvidersPage.tsx` (create/edit + rotate-key modals, test button), nav entry, `/llm-providers` route, `hasPermission` gate. tsc + eslint clean.

### Phase 2: Core Features (runtime wiring — M2) ✅ CODE DONE 2026-06-20 (tests → 3.5)
- [x] 2.1 `ILlmConfigResolver.ResolveAsync(tenantId, agentCode, ct)` ([LlmConfigResolver.cs](../../../src/shared/Clawbot.Infrastructure/Agents/LlmConfigResolver.cs), singleton via `IServiceScopeFactory`, explicit tenant filter, decrypt, effective model D2, throw `LlmConfigNotConfiguredException` D1). **Ambient `LlmCallScope`** (AsyncLocal-in-singleton) added since 4 deep consumers are singletons without tenant context — see [LlmCallScope.cs](../../../src/agents/Clawbot.Agents.Core/Chat/LlmCallScope.cs).
- [x] 2.2 `ILlmChatClientFactory.Create(ResolvedLlmConfig)` ([LlmChatClientFactory.cs](../../../src/agents/Clawbot.Agents.Core/Chat/LlmChatClientFactory.cs)) — anthropic→named HttpClient, openai→OpenAI SDK.
- [x] 2.3 `AnthropicChatClient` now takes `ResolvedLlmConfig` (no IOptions creds); provider-default rates/baseUrl; stamps Model (D1/D3).
- [x] 2.4 `OpenAiChatClient : IClaudeChatClient` via **OpenAI SDK** (reuses ContentLlmClient's `ChatClient` pattern, not raw HTTP); streaming reads `update.Usage` for cost.
- [x] 2.5 Delegating `ScopedLlmChatClient` registered as the single `IClaudeChatClient`; DI in [ChatModule](../../../src/agents/Clawbot.Agents.Core/Chat/ChatModule.cs) + resolver in Infrastructure. Scope set at entry points: `ChatAgent`→chat-agent, `SaleAssistAgent`→sale-assist, `KbTestRunnerService`→chat-agent, `ContentImagePromptService`→content-agent (via ITenantAccessor). **NB: real consumer count was 7, not 5; ContentLlmClient is a separate IContentLlmClient (untouched); ViZhTranslator/VideoScriptComposer have no callers (uninstrumented, will hard-error if invoked).**
- [x] 2.6 Resolved model stamped into `ClaudeReply`/`ClaudeStreamChunk`; ChatAgent CostEntry uses it (dropped hardcoded `"claude"`); USD computed from resolved rates inside each client.
- [x] 2.7 `POST /api/llm-configs/{id}/test` builds a ResolvedLlmConfig from the row + tiny-max-tokens ping via factory.

### Phase 3: Integration & Polish (binding + cutover — M3)
- [x] 3.1 `AgentConfig.LlmConfigId` (nullable) + EF FK (`OnDelete SetNull`); **migrations `0026_agents_llm_config_id.sql`** (col) + **`0027_agents_llm_config_fk.sql`** (index + FK) — split per no-GO batch rule. _(pulled forward — resolver needs it)_
- [x] 3.2 `AgentSettingsRequest/Response` + `UpdateSettingsAsync` bind `llmConfigId` (tri-state: null=unchanged / empty=unbind / id=bind, tenant-owned check). agents.ts contract synced.
- [x] 3.3 FE: provider-config dropdown + unbound warning in `AgentConfigDrawer.tsx`; `AgentDashboardPage` wires `listLlmConfigs` query + form field + tri-state payload. tsc + eslint clean.
- [x] 3.4 Resolver bound-only-or-error + effective model — folded into 2.1.
- [~] 3.5 Tests (≥80%) — ctor breakage fixed; added: resolver hard-error + inactive + D2 override (`LlmConfigResolverTests`), Anthropic adapter (`AnthropicChatClientTests`), **OpenAI adapter via stub transport** (`OpenAiChatClientTests`), **factory provider-selection** (`LlmChatClientFactoryTests`), scope (`ScopedLlmChatClientTests`), **interceptor mapping** (`LlmConfigGrpcInterceptorTests`). 432 unit/service tests green. **Remaining:** endpoint masking + https/SSRF validation (needs an API integration harness — none exists yet) and migration smoke (both gated on Docker/Testcontainers, not runnable locally now); coverage ≥80% number unmeasured for the same reason.

### Review-driven fixes (from /code-review 2026-06-20)
- [x] **A — OpenAI streaming cost = 0 / cost-cap bypass.** SDK 2.11.0 exposes no public streaming-usage option (`ChatCompletionOptions.StreamOptions`/`IncludeUsage` internal — confirmed via reflection). Fix: `OpenAiChatClient.StreamAsync` now resolves the full completion (usage IS returned) and emits one content chunk + a final usage chunk. Guarded by `OpenAiChatClientTests`.
- [x] **B — `LlmConfigNotConfiguredException` not surfaced typed.** Added `LlmConfigGrpcInterceptor` (server, unary + server-streaming) mapping it → `RpcException(FailedPrecondition, "llm_config_not_configured")`; registered in AgentService `Program.cs`; `ChatAgentGrpcService` catch now rethrows the typed exception so the interceptor maps it. **Follow-up:** API gRPC-client → HTTP mapping still returns 500 for FailedPrecondition (consistent with existing no-global-handler baseline); add client mapping if a friendly 422 is wanted.
- [x] **D8 — ContentAgent rewired to resolver.** `ContentAgent` now injects `IClaudeChatClient`+`ILlmCallScope` (scope `content-agent`), rendered template sent as the user message; deleted `ContentLlmClient.cs` (`IContentLlmClient`/`OpenAiCompatibleChatClient`/`ContentLlmOptions`) + DI in `ContentModule`. Tests updated: `ContentAgentTests` (asserts ambient scope + prompt), `ContentAgentGrpcServiceTests` (platform-bearing template). Per-tenant binding now reaches core content generation — env-drift gone.
- [x] **D9 — bind-time provider/model validation.** `UpdateSettingsAsync` fetches the bound config's provider and rejects cross-provider model strings with `model_provider_mismatch` (`IsModelCompatibleWithProvider`: anthropic⇒`claude-*` only, openai⇒not-`claude*`, unknown⇒allow). Unit-tested.
- [x] **D10 — per-provider baseUrl normalization.** `NormalizeBaseUrl(provider, url)`: openai⇒ensure `/v1` suffix, anthropic⇒strip `/v1`. Removes the silent-404 footgun. Unit-tested.
- [x] Test infra: promoted `NormalizeBaseUrl`/`IsAllowedBaseUrl`/`IsModelCompatibleWithProvider` to `internal` (Api→Api.Tests InternalsVisibleTo) → added `LlmConfigValidationTests` (D9/D10 + **SSRF** https-only/private-IP rejection).
- [x] **D9 drift hardening:** compat check now runs whenever model OR binding is touched (resolves the *effective* bound provider, not just on rebind) — a later model-only edit can't drift a Claude string onto an OpenAI bind. [AgentsEndpoints.UpdateSettingsAsync]
- [x] **API 422 mapping:** `GrpcErrorTranslationMiddleware` maps gRPC `FailedPrecondition` (`llm_config_not_configured`) → HTTP 422 `{error}` at the API edge (was raw 500); unmapped statuses unchanged. Unit-tested. Now satisfies req "never a 500" end-to-end (gRPC typed + HTTP mapped).
- [x] **Runbook:** deployment doc updated — `Content:Llm:*` now dead config (D8), content-agent must be bound; D9 `model_provider_mismatch` + D10 baseUrl-normalization admin notes.
- Full non-integration suite: **459 green** (Api.Tests →105).

- [ ] **(superseded) D2 (#2) — effective-model footgun:** `AgentConfig.Model` is `IsRequired`/always-populated, so `agent.model ?? cfg.ModelId` makes `LlmConfig.ModelId` unreachable and risks sending an Anthropic model string to an OpenAI endpoint (and vice-versa) on a cross-provider bind. Matches design D2 as written → deferred to `/review-design` (changes product behavior).
- [x] 3.6 Deleted `AnthropicOptions.cs` (dead in src; only the broken test referenced it → 3.5). FE flags unbound/inactive agents via the drawer warning.
- [x] 3.7 Deployment runbook written ([deployment doc](../deployment/2026-06-19-feature-llm-provider-config.md)) — hard-cutover maintenance window, migration order, rollback.

## Dependencies
**What needs to happen in what order?**

- 1.1→1.2→1.4 (domain before EF before endpoints). 1.5 before any endpoint call succeeds (else 403).
- Phase 2 depends on Phase 1 data + DTOs. 2.4 OpenAI adapter independent of 2.3 and can parallelize.
- 3.1 FK migration depends on `llm_configs` existing (already true). 3.2/3.3 depend on 3.1.
- **External:** valid Anthropic + OpenAI(-compatible) test keys for test-connection and integration tests.

## Timeline & Estimates
**When will things be done?**

- Phase 1: ~1.5–2 days (domain + migration + endpoints + screen).
- Phase 2: ~2–2.5 days (resolver/factory + OpenAI adapter + rewiring + cost).
- Phase 3: ~1.5 days (FK migration + binding UI + tests + cutover).
- **Total ~5–6 dev-days**, +0.5 buffer for migration/analyzer-gate fiddliness. Estimates only — no committed dates.

## Risks & Mitigation
**What could go wrong?**

- **Migration gates** ([[clawbot-build-gates]], [[clawbot-migration-no-go]]): FK + index + column adds split incorrectly → build/migration failure. → One `SqlCommand` per file, no `GO`, separate files; smoke-run locally.
- **Runtime cutover regression:** rewiring chat path breaks existing Claude flow. → Keep `AnthropicOptions` fallback; feature-gate runtime read; integration test before removing env path.
- **Secret exposure:** plaintext leak via DTO/logs. → Masked DTO test; no key in logs; decrypt only at call.
- **OpenAI shape drift:** non-standard `/v1/chat/completions` on some compatible servers. → Validate against a known-good endpoint; document supported contract.
- **Model-source-of-truth ambiguity** (`AgentConfig.model` vs `LlmConfig.ModelId`). → Resolve in design review before Phase 3.
- **CA analyzer errors** on new code (errors, not warnings). → Build early and often per phase.

## Resources Needed
**What do we need to succeed?**

- Test credentials: Anthropic key + one OpenAI-compatible endpoint/key (or Azure OpenAI).
- Reuse references already in repo: `ChannelsEndpoints`/`ApiKeysEndpoints` (CRUD+mask), `IEncryptor`/`AesEncryptor`, `OpenAiEmbeddingProvider` (OpenAI HTTP), `AnthropicChatClient` (chat contract), `AgentConfigDrawer.tsx`/`agents.ts` (FE pattern), `RbacSeeder` (perm seeding).
- Next: run `/review-requirements`, then `/review-design`; on pass, `/execute-plan`.
