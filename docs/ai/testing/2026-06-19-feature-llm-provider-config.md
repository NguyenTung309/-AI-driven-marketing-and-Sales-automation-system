---
phase: testing
title: LLM Provider Config — Testing Strategy
description: Define testing approach, test cases, and quality assurance
feature: llm-provider-config
date: 2026-06-19
last_reviewed: 2026-06-21
---

# LLM Provider Config — Testing Strategy

## Test Coverage Goals
**What level of testing do we aim for?**

- Unit test coverage target: 100% of new resolver, provider-client, validation, and model/provider guard branches.
- Integration test scope: `/api/llm-configs` CRUD, secret masking, RBAC, agent binding, resolver/runtime failure modes, and provider cost propagation.
- End-to-end test scenarios: tenant admin creates a provider config, tests it, binds it to an agent, and the runtime resolves the bound provider; unbound/inactive agent returns `llm_config_not_configured`.
- Alignment with [requirements](../requirements/2026-06-19-feature-llm-provider-config.md) and [design](../design/2026-06-19-feature-llm-provider-config.md): no env fallback, per-agent binding, key masking, SSRF-safe baseUrl validation, provider routing, and per-config cost rates.

## Unit Tests
**What individual components need testing?**

### `LlmConfigResolver`
- [x] Bound active config resolves and decrypts key; agent model override wins over config model — [LlmConfigResolverTests.cs](../../tests/Clawbot.Infrastructure.Tests/Agents/LlmConfigResolverTests.cs).
- [x] No bound config throws `LlmConfigNotConfiguredException`.
- [x] Inactive config throws typed config error.
- [x] Decryption failure maps to typed config error without leaking internals.
- [ ] Deleted bound config after bind throws typed config error.
- [ ] Hard-cutover regression: no `AnthropicOptions`/environment fallback when no bound config exists.
- [ ] Request-scope cache/decrypt-once behavior if resolver caching is introduced.

### `LlmConfigsEndpoints` validation helpers
- [x] Provider base URL normalization per D10 — [LlmConfigValidationTests.cs](../../tests/Clawbot.Api.Tests/LlmConfigValidationTests.cs).
- [x] HTTPS-only + private/loopback literal IP rejection.
- [x] Safe test-connection error string does not expose raw exception text.
- [ ] DNS-based SSRF guard or explicit documented limitation test if DNS resolution remains out of scope.

### `AgentsEndpoints` binding guard
- [x] Cross-provider model compatibility helper blocks `claude-*` on OpenAI and GPT names on Anthropic — [LlmConfigValidationTests.cs](../../tests/Clawbot.Api.Tests/LlmConfigValidationTests.cs).
- [ ] Agent settings update validates `llmConfigId` belongs to tenant.
- [ ] Missing config returns `invalid_llm_config`.
- [ ] Cross-provider bind returns `model_provider_mismatch`.
- [ ] Response echoes `llmConfigId`.

### Provider clients and factory
- [x] Factory selects Anthropic/OpenAI and rejects unsupported providers — [LlmChatClientFactoryTests.cs](../../tests/Clawbot.Agents.Tests/Chat/LlmChatClientFactoryTests.cs).
- [x] `ScopedLlmChatClient` reads ambient `(tenantId, agentCode)` and delegates to resolver/factory — [ScopedLlmChatClientTests.cs](../../tests/Clawbot.Agents.Tests/Chat/ScopedLlmChatClientTests.cs).
- [x] Anthropic adapter request path, headers, streaming deltas, usage, and USD mapping — [AnthropicChatClientTests.cs](../../tests/Clawbot.Agents.Tests/Chat/AnthropicChatClientTests.cs).
- [x] OpenAI adapter complete/stream usage mapping and default-rate fallback — [OpenAiChatClientTests.cs](../../tests/Clawbot.Agents.Tests/Chat/OpenAiChatClientTests.cs).
- [ ] OpenAI transport shape: Bearer auth header, exact URI, message payload, max tokens, and temperature.
- [ ] Runtime cost propagation records resolved provider/model and per-config rates through the ledger.

### gRPC/runtime error translation
- [x] `LlmConfigGrpcInterceptor` maps missing config to gRPC `FailedPrecondition` with `llm_config_not_configured` — [LlmConfigGrpcInterceptorTests.cs](../../tests/Clawbot.AgentService.Tests/Services/LlmConfigGrpcInterceptorTests.cs).
- [x] API gRPC error translation maps typed config errors to safe HTTP responses — [GrpcErrorTranslationMiddlewareTests.cs](../../tests/Clawbot.Api.Tests/GrpcErrorTranslationMiddlewareTests.cs).

## Integration Tests
**How do we test component interactions?**

- [ ] API CRUD suite for `/api/llm-configs`:
  - POST creates encrypted config and normalizes provider/baseUrl.
  - GET/list returns `hasApiKey` only; never `apiKey` or `apiKeyEncrypted`.
  - PUT updates model/baseUrl/defaults/rates without touching key.
  - `rotate-key` changes encrypted key and never returns plaintext.
  - activate/deactivate toggles runtime eligibility.
  - test-connection returns `{ ok, latencyMs, error? }` with sanitized errors.
  - DELETE returns 204 and unbind behavior follows the FK design.
- [ ] RBAC tests:
  - `llm-configs:manage` seeded for Admin/tenant-admin role.
  - Missing permission → 403.
  - Authorized admin → CRUD succeeds.
- [ ] Agent binding flow:
  - Create config → bind via `/api/agents/{code}/settings` → runtime resolver returns bound provider.
  - Cross-tenant config id cannot bind.
  - Inactive config blocks runtime with `llm_config_not_configured`.
- [ ] Audit/security flow:
  - create/rotate/activate/deactivate/delete audit entries do not include key material.

## End-to-End Tests
**What user flows need validation?**

- [ ] Tenant admin creates Anthropic config → tests connection → activates → binds chat agent → chat reply succeeds through scoped config.
- [ ] Tenant admin creates OpenAI-compatible config → binds content agent → content generation succeeds and ledger uses OpenAI model/rates.
- [ ] Unbound or inactive agent path returns `llm_config_not_configured` and UI shows a clear not-configured state.
- [ ] Browser/UI smoke: Model/Provider Settings page lists configs, masks keys, rotates key, and updates agent binding.

## Test Data
**What data do we use for testing?**

- In-memory/SQLite EF fixtures for resolver/domain tests.
- Stub `IEncryptor` that maps `cipher-key` ↔ `plain-key`; never use real secrets.
- Capturing HTTP handlers for Anthropic/OpenAI request assertions.
- Stub `ILlmChatClientFactory` for test-connection success/failure cases.
- Seeded tenant/admin role with `llm-configs:manage` for endpoint integration tests.
- Avoid real external provider calls in CI; live-key smoke belongs to a manual or gated environment.

## Test Reporting & Coverage
**How do we verify and communicate test results?**

- Targeted commands used 2026-06-21:
  - `dotnet test "tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj" --no-restore --filter LlmConfigValidationTests --logger "console;verbosity=minimal"` — passed.
  - `dotnet test "tests/Clawbot.Agents.Tests/Clawbot.Agents.Tests.csproj" --no-restore --filter OpenAiChatClientTests --logger "console;verbosity=minimal"` — passed.
  - `dotnet test "tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj" --no-restore --filter LlmConfigResolverTests --logger "console;verbosity=minimal"` — passed.
- Coverage command:
  - `dotnet test tests --collect:"XPlat Code Coverage"`
- Recommended report step:
  - merge coverage outputs with `reportgenerator` and publish line/branch coverage for `Clawbot.Api`, `Clawbot.Agents.Core`, and `Clawbot.Infrastructure`.
- Coverage gaps blocking 100%: endpoint HTTP surface, agent binding flow, OpenAI request transport, audit/key masking, runtime provider-specific ledger entries.

## Manual Testing
**What requires human validation?**

- Provider setup UX: create/update/delete/rotate flows feel safe and clearly warn about hard cutover.
- Test-connection latency and sanitized error copy.
- Agent settings dropdown shows active configs only and flags unconfigured/inactive binding.
- Maintenance-window smoke: no `Anthropic:*` env fallback; configured tenants recover by binding configs.

## Performance Testing
**How do we validate performance?**

- Resolver overhead: one indexed agent lookup + one config lookup + decrypt per call; measure p95 under normal chat/content load if request-scope caching is added.
- Test-connection: timeout-bound, no retries that can stall UI.
- OpenAI adapter: confirm full-completion fallback for streaming does not violate user-facing latency expectations for OpenAI-compatible models.

## Bug Tracking
**How do we manage issues?**

- HIGH: plaintext API key exposure, cross-tenant config binding, missing hard-cutover error, provider mismatch causing runtime 500.
- MEDIUM: missing endpoint/RBAC/audit coverage, DNS SSRF limitation, provider-specific cost ledger gaps.
- LOW: UI copy/latency polish and optional request-scope cache tests.

## Deferred Follow-ups

1. Add API integration tests for `/api/llm-configs` CRUD, masking, RBAC, and test-connection.
2. Add agent binding HTTP tests in `AgentsEndpoints`.
3. Add OpenAI transport assertion tests with a capturing handler.
4. Add runtime cost-ledger tests for OpenAI-compatible configs.
5. Add audit redaction tests for create/rotate/delete.
