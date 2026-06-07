# RFC-002 — OpenAI-compatible LLM for content generation

- Status: **ACCEPTED (M18 Phase 2)** — 2026-06-07
- Owner: AI infra
- Resolves: M18 content-research-pipeline task 2.0
- Build gate: NuGetAudit + CA analyzers as errors; no package accepted without a green restore/build.

## Context

M18 content generation must draft platform-specific marketing posts for Facebook, Instagram, TikTok, YouTube, and Zalo. The module decision is explicit: use an OpenAI-compatible endpoint through the official `OpenAI` .NET library, not the existing Anthropic chat client used by chat/sale-assist/docs.

The implementation needs:

1. A vendor-swappable base URL, model, and API key.
2. A narrow project-owned `IContentLlmClient` abstraction so endpoint/gRPC code does not depend on SDK types.
3. Externalized prompt templates via config/KB, not hardcoded prompt literals.
4. Build-gate proof before landing the dependency.

## Options

| | Option A — Official `OpenAI` package | Option B — raw `HttpClient` only |
|---|---|---|
| API shape | Typed SDK request/response model | Project-owned JSON DTOs |
| OpenAI-compatible providers | Supported when provider matches OpenAI chat API and base URL is configured | Fully controllable |
| Token usage | SDK exposes response usage when available | Must map per provider |
| Dependency risk | One new package, must clear audit | No new NuGet |
| Maintenance | Tracks official API changes | We own every wire-shape change |

## Decision

Use **Option A** with a project-owned wrapper:

- Add central package pin: `OpenAI` `2.11.0` (GA).
- Add `IContentLlmClient` and `ContentLlmOptions` in `Clawbot.Agents.Core/Content`.
- Keep SDK types inside `OpenAiCompatibleChatClient`; all callers use plain records/strings.
- Configure endpoint/model/key from `Content:Llm:*`; do not hardcode provider values.
- Document that non-OpenAI providers must be OpenAI-compatible at the chat-completions surface.

NuGet query evidence on 2026-06-07: `https://api.nuget.org/v3-flatcontainer/openai/index.json` lists `2.11.0` as latest GA. The initial pin was `2.1.0-beta.1` because `Microsoft.SemanticKernel.Connectors.OpenAI` 1.22.0 required `OpenAI (= 2.1.0-beta.1)`. Since SemanticKernel was unused in the codebase (no `using Microsoft.SemanticKernel` found), it was removed from `Clawbot.Agents.Core.csproj` and `Directory.Packages.props`, unblocking the GA upgrade. Build + all 156 tests pass on `OpenAI` `2.11.0`.

## Follow-ups

- If provider-specific response metadata is needed, add fields to the wrapper result rather than leaking SDK response types.
- ADR-010 runtime `llm_configs` table remains deferred as a cross-agent refactor.
- A generic LLM cost tracker remains deferred; M18 logs token/latency metadata first.
