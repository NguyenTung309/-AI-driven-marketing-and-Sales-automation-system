# RFC-001 — Semantic Kernel vs direct Anthropic SDK for agent orchestration

- Status: **DRAFT (M09 spike)** — 2026-05-28
- Owner: AI infra
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

## Spike findings (this commit)

- SK 1.22 NuGet referenced in `Clawbot.Agents.Core.csproj`.
- Anthropic does not publish an official SK connector; community options exist (`SemanticKernel.Connectors.Anthropic` preview).
- For RAG, SK's new vector abstractions (`Microsoft.Extensions.VectorData`) are still preview; our custom `IVectorStore` already targets Qdrant cleanly and is the closer fit per Constitution Article 1 ("Qdrant primary; SQL Server JSON snapshot").
- Cost tracker MUST persist to SQLite ledger per Article 6 — direct OTel attribute capture is straightforward in both options.

## Decision (provisional — confirm after M10 lands)

**Option B**: SK as plugin/planner host only; Anthropic SDK direct for chat completion; our own `IRagRetriever` + `IVectorStore` + `IEmbeddingProvider`.

Rationale:
1. Hard dep on a preview community connector is a Constitution Article 1 risk (adding a dep = RFC).
2. Streaming is first-class in `Anthropic.SDK`; gRPC server-streaming maps directly.
3. SK's vector data abstraction is still moving; freezing on our minimal `IVectorStore` lets us swap later without leaking SK types into Domain.
4. Plugin orchestration (skill calls, planner) is where SK earns its keep — we keep that path.

If Anthropic ships a stable first-party SK connector later, revisit and migrate to Option A behind a feature flag.

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
