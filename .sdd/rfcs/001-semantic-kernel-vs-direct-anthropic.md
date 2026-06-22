# RFC-001 — Semantic Kernel vs direct Anthropic SDK for agent orchestration

- Status: **ACCEPTED / UPDATED** — 2026-06-21
- Owner: AI infra
- Update trigger: `dynamic-agent-orchestration` design review (2026-06-20/21) confirmed Option B and authorizes adding `Microsoft.SemanticKernel` as planner/plugin host only.
- Resolves: plan risk hotspot #1 ([wiggly-wandering-blum.md](../../C:/Users/AdminDatVo/.claude/plans/wiggly-wandering-blum.md))
- Constitution alignment: Article 1 mandates Microsoft Semantic Kernel as the orchestration layer.

## Context

`Clawbot.AgentService` runs 8 gRPC agents. Each agent needs:
1. Retrieval over `kb_versions` (RAG via Qdrant)
2. Chat completion (Claude Sonnet 4.6)
3. Tool/skill invocation (22 utility skills in `Clawbot.Agents.Core/Skills/*`)
4. Cost ledger + trace emission (`agent_traces`, `kpi_daily`)

Question: do we host all four through Microsoft Semantic Kernel (SK), or call Anthropic SDK directly and use SK only where it pays its weight (plugin host, planner)?

## Options

| | Option A — Full SK | Option B — SK for plugin host only |
|---|---|---|
| Chat completion | `IChatCompletionService` via Anthropic connector | `Anthropic.SDK` direct (`AnthropicClient`) |
| RAG | SK `IVectorStore` + `IEmbeddingGenerator` | Our `IVectorStore` + custom `IRagRetriever` |
| Plugin/tool | SK `KernelFunction` | SK `KernelFunction` (same as A) |
| Cost ledger | OTel hooks on SK calls | OTel + custom `IClaudeCostTracker` |
| Streaming | SK streaming API | gRPC server-streaming + `AnthropicClient.Messages.CreateStreamAsync` |
| Maturity | Anthropic connector is community / preview (SK does not ship a first-party Claude connector as of 2026-05) | Anthropic SDK is GA |
| Vendor lock | Soft — hard to migrate off SK abstraction | Light — own thin abstraction |

## Spike findings (updated 2026-06-21)

- `Microsoft.SemanticKernel` is **not currently referenced** in `Clawbot.Agents.Core.csproj`; adding it is part of `dynamic-agent-orchestration` and must use central package management (`Directory.Packages.props`).
- Approved package: `Microsoft.SemanticKernel` **1.77.0** in `Directory.Packages.props`, with `<PackageReference Include="Microsoft.SemanticKernel" />` in `Clawbot.Agents.Core.csproj`.
- Anthropic does not publish an official SK connector; community options exist (`SemanticKernel.Connectors.Anthropic` preview) but remain rejected for this project.
- For RAG, SK's vector abstractions are not used; custom `IRagRetriever` + Qdrant path stays authoritative.
- Cost tracker MUST persist to ledger per Article 6; the dynamic orchestrator adds a separate pre-flight + atomic guard because parallel DAG execution can race the current `DbClaudeCostTracker.RecordAsync` summary-then-insert pattern.

## Decision

**Option B (accepted)**: SK as plugin/planner host only; direct runtime chat completion remains behind ClawBot's `IClaudeChatClient` / `ScopedLlmChatClient`; our own `IRagRetriever` + `IVectorStore` + `IEmbeddingProvider` stay in place.

Rationale:
1. Hard dep on a preview community connector is a Constitution Article 1 risk (adding a dep = RFC).
2. Streaming is first-class in `Anthropic.SDK`; gRPC server-streaming maps directly.
3. SK's vector data abstraction is still moving; freezing on our minimal `IVectorStore` lets us swap later without leaking SK types into Domain.
4. Plugin orchestration (skill calls, planner) is where SK earns its keep — we keep that path.

If Anthropic ships a stable first-party SK connector later, revisit and migrate to Option A behind a feature flag.

## 2026-06-21 update — dynamic-agent-orchestration

`dynamic-agent-orchestration` uses this RFC as its dependency approval:

- Add only `Microsoft.SemanticKernel` (planner/plugin host). Do **not** add `SemanticKernel.Connectors.Anthropic`.
- Implement `ClawbotChatCompletionService : IChatCompletionService` to adapt SK to `ScopedLlmChatClient`, preserving ADR-010 per-tenant LLM resolution.
- Planner runs under `AgentConfig.Code = "orchestrator"`; each tenant must bind it to an active `llm_config`.
- Build must pass NuGetAudit + CA gates after package add.
- Parallel DAG execution requires `IOrchestratorCostGuard`; do not rely on `DbClaudeCostTracker.RecordAsync` alone for cap enforcement.

## Implementation slice landed in this spike

- `QdrantVectorStore` real impl (upsert / search / delete).
- `IEmbeddingProvider` + `HashEmbeddingProvider` (deterministic 384-dim stub) so the wire is testable without a vendor key.
- `IRagRetriever` + `QdrantRagRetriever` returning grounded snippets keyed by KB module code.
- `RagModule.AddClawbotRag()` DI extension; wired in `AgentService/Program.cs`.
- Stubbed chat completion deferred to M10 — `IRagRetriever` already returns the context payload that the agent will hand to the eventual Anthropic chat client.

## Open questions for M10

- Concrete embedding model choice (Voyage AI vs local SBERT vs OpenAI).
- Whether `IClaudeCostTracker` writes synchronously per call or batches every N seconds.
- Cache key for scenario-template responses (Redis): `tenant + scenario_code + kb_version_hash + content_hash`?
