---
phase: implementation
title: Implementation Guide
description: Technical implementation notes, patterns, and code guidelines
---

# Implementation Guide — `content-publishing-approval-policy`

## Development Setup
**How do we get started?**

Prerequisites:

- Use the current checkout; several target files already contain uncommitted work. Read each file before editing and apply narrow changes.
- Recheck the next migration number before creating SQL. Current planning expectation is `0076`, not a durable reservation.
- Confirm SQL Server/document storage/AgentService/API dependencies used by existing focused tests.
- Prepare two reviewer configs for testing: text-only and vision-capable.
- Select a maintained image decoder only if the repo has no suitable existing package; verify license, security history and .NET compatibility before adding it.

Baseline verification:

```text
dotnet test tests/Clawbot.Domain.Tests
dotnet test tests/Clawbot.Agents.Tests
dotnet test tests/Clawbot.AgentService.Tests
dotnet test tests/Clawbot.Infrastructure.Tests
dotnet test tests/Clawbot.Api.Tests
dotnet test tests/Clawbot.Integration.Tests
```

Frontend commands must use the package manager/scripts already defined under `src/frontend/clawbot-web`; run formatter, ESLint, incremental typecheck, tests if configured, and production build.

## Code Structure
**How is the code organized?**

Domain/persistence:

- `src/shared/Clawbot.Domain/Content/ContentItem.cs` — revision, Agent review and publishing approval invariants.
- `src/shared/Clawbot.Domain/Content/ContentSchedule.cs` — mandatory scheduled revision, durable intent/claim state and hold/error reasons.
- New entities: `ContentReviewTask`, lifecycle `ContentAsset`, `ContentPublishAttempt`, `ContentWorkflowMetricsHourly`; extend `AuditLog` with event key/state sequence.
- `src/shared/Clawbot.Domain/Tenants/Tenant.cs` — publishing policy value/version/updated timestamp.
- `src/shared/Clawbot.Infrastructure/Persistence/Configurations/DomainModelConfigurations.cs` — EF mappings/concurrency/indexes.

Review execution:

- `src/agents/Clawbot.Agents.Core/Content/ContentReviewer.cs` — required text review and optional vision review.
- `src/agents/Clawbot.AgentService/Services/ContentReviewCoordinator.cs` — processes leased durable review tasks.
- `src/agents/Clawbot.AgentService/Services/ContentAssetReader.cs` — tenant/item-scoped bounded reads from server-owned assets, validation/GIF sampling.
- Existing `ContentAgentGrpcService.cs` and API edit/upload paths — persist `ContentReviewTask` in the same transaction as item revision changes; no cross-host in-memory call is required.

Scheduling/publishing:

- `src/shared/Clawbot.Infrastructure/Content/ContentAutoScheduler.cs` — creates a revision-bound schedule intent in the approval transaction.
- `src/shared/Clawbot.Infrastructure/Jobs/ContentPublishJob.cs` — tenant-dispatched conditional claim, immutable snapshot, provider call and finalization.
- Publish-attempt reconciliation and review/SLA jobs under `src/shared/Clawbot.Infrastructure/Jobs`.

API/frontend:

- New `ContentPublishingPolicyEndpoints.cs` plus existing `ContentEndpoints.cs` and `ContentDtos.cs`.
- Shared FE control `features/content/ContentPublishingPolicyControl.tsx` used by both content workspace and Agent dashboard.
- Canonical FE API in `shared/api/content.ts`.

Naming:

- Agent verdict internal name: `passed`, not publishing `approved`.
- Publishing policy: `automatic|human_required`.
- Approval modes: `automatic|human|human_override`.
- Tool: `content.review`; permissions: `content:approve` for human decisions and `content:publish` for privileged retry/reconciliation. `content:write` cannot call an external provider. Keep tool names and RBAC codes distinct.

## Implementation Notes
**Key technical details to remember:**

### Revision and transaction boundaries

- `ContentRevision` starts at 1 and increments only when body/assets materially change.
- Begin/complete review and human approval accept `expectedRevision`.
- `rowversion` protects cross-process edits. Translate `DbUpdateConcurrencyException` into a domain conflict, not a generic 500.
- API edit/generation transaction commits item revision invalidation and a unique `ContentReviewTask` together.
- Save leased/running task state before LLM calls so recovery can resume after process loss.
- Reload after LLM calls and discard results for stale revisions; commit result + policy value/version + audit together.
- Approval and schedule-intent creation are one DB transaction; no external call occurs there.
- Before social publication, conditionally claim the schedule and persist `ContentPublishAttempt` plus immutable payload snapshot. This committed claim is the irreversible boundary; edits are rejected while active.

### Review coordinator

Cross-host entry points write durable state, not in-memory coordinator calls:

- `ContentReviewTask.UpsertPending(tenantId, itemId, revision, nextAttemptAt)` in the same DbContext transaction as generation/edit.
- `ReviewTenantWorker.RunTenantAsync(tenantId)` conditionally leases tasks.
- `ContentReviewCoordinator.ProcessAsync(taskId, leaseToken, ct)` performs review.
- `CompleteReviewAsync(...expectedRevision..., result, ct)` opens a transaction, locks the Tenant row with `UPDLOCK,HOLDLOCK`, reads policy value/version, and commits item/task/audit/schedule before releasing the lock; conditional affected-row mismatch retries.

Generation creates an immediately-due task. Manual body/assets updates create a short quiet-period task so rapid edits/uploads coalesce. Manual retry updates the same item/revision task subject to DB cooldown and tenant concurrency limits.

A system dispatcher only enumerates tenant IDs; every worker query filters and cross-validates explicit tenant ownership.

### Mandatory text completion and parsing

- Use a review-specific completion client returning terminal-success, finish reason, refusal/content-filter and truncation metadata.
- Automatic approval requires explicit successful terminal completion. Responses streaming must observe `response.completed`; EOF/malformed/partial streams fail closed.
- Preserve trusted system/developer instructions in a separate wire role/field. Replace the current OpenAI-compatible fallback that concatenates system and user; incompatible gateways route human.
- Parse the complete trimmed reply as exactly one closed-schema JSON object. Remove first-`{`/last-`}` extraction and reject prose/fences/trailing data/duplicates/unknown fields/type mismatch/oversized reason.
- Provider structured output is preferred where available, but strict local validation is mandatory.

### Optional vision

- Every mandatory text review, including no-assets and vision-unsupported fallback, uses `IContentReviewCompletionClient.CompleteTextAsync`. It may reuse existing config resolution/cost tracking, but never accepts the metadata-poor `ClaudeReply` contract.
- Persist nullable `LlmConfig.SupportsVision` in `llm_configs.supports_vision`, expose it through existing LLM config API/admin form. Capability precedence: explicit override, maintained model registry, otherwise unknown; invalidate on rebind/config version change.
- `unavailable` → skip asset reads, call text reviewer, persist `skipped_unsupported`.
- `available` → read server-owned `ContentAsset` via tenant/item/asset-scoped bounded storage API and call multimodal adapter.
- `unknown` → attempt vision once; typed unsupported falls back to text, other errors route human.
- Adapter creates canonical duplicate-free requested IDs and records sent IDs; reviewer JSON returns `reviewedPartIds`. Persist `reviewed` only when requested == sent == reviewed as sets with equal cardinality plus non-refused/non-filtered/untruncated completion. This is a self-reported completeness guard, not provider proof of semantic attention.
- GIFs use deterministic evenly-spaced sampling with named caps.
- Current assets are server-owned tenant/item rows with `status=ready`; remove/reorder endpoints revise the item. Review/publish snapshots read those rows directly, never `AssetsJson` or client-supplied storage keys/URLs.
- Keep learned reviewer memory/KB/body/OCR/image content out of the system prompt; delimit all as untrusted data.
- Image changes still increment revision even when the current model lacks vision.

### Provider capability seams confirmed — 2026-07-20

Keep the existing general chat contract intact for its many callers. Add a parallel review-specific path:

```text
IContentReviewCompletionClient
  CompleteTextAsync(trustedInstructions, untrustedTextParts, ct)
  CompleteVisionAsync(trustedInstructions, untrustedContentParts, ct)

IContentReviewCompletionClientFactory.Create(ResolvedLlmConfig)
ILlmVisionCapabilityResolver.ResolveAsync(tenantId, agentCode, ct)

ReviewCompletionEnvelope
  RawText
  ObservedTerminalSuccess
  FinishReason
  IsRefusal
  IsContentFiltered
  IsTruncated
  RequestedPartIds
  SentPartIds
  Usage/Model/Cost

VisionCapability = available | unavailable | unknown
VisionUnsupportedException = typed provider/model rejection only
```

`ScopedLlmChatClient`/`ILlmCallScope` already provide the tenant + agent binding seam. Add a scoped review counterpart that resolves the same binding and delegates to a review factory; do not widen `ClaudeReply` or change all existing chat consumers. Extend `ResolvedLlmConfig` for the review path with config identity/update stamp and nullable `SupportsVision`. Initially do not cache capability resolution, so model/config/rebinding changes are observed on every review. If caching is added later, key it by config id + config updated timestamp + effective model + binding updated timestamp.

| Provider path | Current seam | Review-specific requirement |
|---|---|---|
| Anthropic Messages | Direct `HttpClient`; trusted system text is already a separate `system` field | Add typed text/image content parts and parse `stop_reason`/`stop_details`. Initial allowlist is terminal `end_turn` only. `max_tokens`, context-window exhaustion, `refusal`, `tool_use`, `pause_turn`, missing/unknown stop reason or empty output route human. Prefer non-stream review calls; if streaming is later used, require terminal `message_delta.stop_reason` and `message_stop`, not EOF. |
| OpenAI Chat, official | SDK path preserves a real system message but `ClaudeReply` drops finish/refusal/filter metadata | Use a review adapter that exposes the full completion metadata. Require exactly one choice, `finish_reason=stop`, no refusal/content filter, non-empty text. `length`, `content_filter`, tool calls, missing/unknown finish reason or multiple choices route human. |
| OpenAI-compatible Chat | Current direct fallback concatenates system + user into one user message and parses no finish/refusal metadata | Never reuse that fallback for automatic review. Send separate system and user messages. Require the gateway to return the strict metadata above; role rejection, omitted metadata or ambiguous response routes human. |
| OpenAI Responses | Direct HTTP already uses separate `instructions` and `input`, but current SSE parser ignores malformed events and returns success at EOF | Require explicit `response.completed` with `status=completed`. `response.incomplete`, `response.failed`, `error`, refusal events/content, malformed SSE, `[DONE]`/EOF before completion, `incomplete_details`, or empty output route human. Non-SSE JSON must also report completed status. |

Vision capability precedence is fail-closed:

1. Explicit `llm_configs.supports_vision` (`true` or `false`) wins.
2. A maintained provider/model registry is used only for official provider origins; use explicit exact/prefix entries with tests, not broad substring guesses.
3. Custom base URLs and `openai-compatible` are `unknown` unless explicitly overridden.
4. `unknown` attempts vision once for that review task. Only a machine-readable, known unsupported-model/content-part response becomes `VisionUnsupportedException` and falls back to mandatory text review. Authentication, permission, transport, malformed-response and generic 4xx/5xx errors do not masquerade as unsupported; they route human.

Provider adapters receive immutable typed parts. They record canonical requested IDs before serialization and actual sent IDs from the serialized request. Automatic acceptance additionally requires the strict reviewer JSON `reviewedPartIds` to match requested and sent IDs exactly as duplicate-free sets with equal cardinality.

The current `ContentReviewer` must stop placing tenant-derived memory into the system persona. Body, KB evidence, learned memory, OCR-visible text and images are delimited untrusted parts. Replace first-`{`/last-`}` extraction with a dedicated strict whole-response parser shared by text and vision paths.

### Approval and scheduling

- Automatic approval only follows text verdict `passed` under snapshotted `automatic` policy.
- Any non-pass result under automatic policy becomes human-pending.
- Human-required policy always waits for a human after review completes.
- Non-pass human override requires a non-empty reason.
- Both approval modes call the same `ContentAutoScheduler` inside the approval transaction.
- Persist one mandatory-revision schedule intent and initial golden time even when a publish target is temporarily missing; hold/retry from that row.
- Uniqueness conflict means another transaction created the same intent; load the winner.
- User cancel writes `status=canceled` with `last_error_code=canceled_by_user`; recovery never recreates it. Reschedule is explicit/audited.
- `content.schedule` cannot choose caller-controlled times from autonomous Agent defaults; human reschedule is the only explicit-time path.

### Publish backstop

`MarkPublished` and `ContentPublishJob` must require:

- `AgentReviewedRevision == ContentRevision`;
- text review is a completed result;
- `ApprovedRevision == ContentRevision`;
- schedule revision matches current revision;
- item is not finally rejected/deleted.

Remove the optional `requireAgentReview` argument. A tenant setting must never weaken this invariant.

Due publication sequence:

1. Transactionally verify all revisions and conditionally move schedule `pending -> publishing`.
2. Create `ContentPublishAttempt` with stable idempotency key and immutable body/assets snapshot/hash.
3. Commit claim, then call the provider with that snapshot.
4. On definitive success, commit external ID, item/schedule final state, business audit and outbox event.
5. On timeout/process loss after transmission, mark or recover as `outcome_unknown`; do not automatic retry until provider reconciliation or privileged `content:publish` decision.

HTTP retry and Agent tools only request durable state transitions; they never call the provider inline.

### Compatibility rollout

- Add schema and first deploy a bridge binary that sets a content writer version in SQL `SESSION_CONTEXT` and respects `content_workflow_runtime_gate`.
- After every live writer is bridge/new, set publication pause and raise minimum writer so SQL triggers reject absent/lower versions.
- Pause all publication entry points; remove/supersede the legacy review backfill invocation; drain and stop old binaries.
- Never infer real review or approval revision from historical fields. Already-published rows may be history-only `legacy_exempt`; every unpublished/scheduled row requires fresh Agent review and forced human approval.
- Deploy unconditional backstop/claim build before publication resumes. Rollback must use a prepared compatible build or keep publishing paused.
- Old `RequireContentReview` cannot disable review and is removed only in cleanup.

## Integration Points
**How do pieces connect?**

- Generation/edit transaction → item revision + `ContentReviewTask`.
- Asset upload → reserve DB asset/key → upload object → finalize transaction marks ready + updates server-managed asset list + increments revision + creates task; compensation/cleanup handles failures.
- Tenant review worker → leased task → coordinator → binding/capability resolver → text or optional vision client.
- Vision path → server-owned `ContentAsset` → tenant/item-scoped bounded storage reader.
- Coordinator transaction → review result + policy value/version + automatic approval or human queue + audit/outbox.
- Automatic/human approval transaction → `ContentAutoScheduler` → persisted golden-time `ContentSchedule` intent.
- Tenant publish worker → conditional `ContentPublishAttempt` claim/snapshot → guarded social publisher → finalize/reconcile.
- `/content` and `/agents` → sole canonical policy writer and shared query key.
- Explicit audit writer commits with transitions; generic audit excludes body/assets/provider data.

No new external service is required.

## Error Handling
**How do we handle failures?**

| Failure | Required behavior |
|---|---|
| Text reviewer timeout/parse/provider error | Persist `needs_human|failed`, human fallback, no auto approval |
| Vision unsupported | Continue text review, `imageReviewStatus=skipped_unsupported` |
| Vision asset read/decode error | `imageReviewStatus=failed`, human fallback |
| Stale review result | Discard, audit, leave new revision pending |
| Stale human approve | Return HTTP 409 `content.revision_changed` |
| Missing reviewer definition/binding | Human fallback + operational warning |
| Missing publish target | Keep revision-bound schedule intent held at persisted golden time, notify, retry bounded |
| Concurrent schedule insert | Load and return winning revision-bound intent |
| Provider timeout after transmit | Mark `outcome_unknown`; reconcile before any retry |
| Active publish claim during edit | Return 409; claim snapshot is irreversible boundary |
| Publish invariant failure | Hold with precise reason; never publish |
| Policy update invalid value/permission | 400/403 with stable error code |

Log structured IDs/revisions/reasons. Do not log full body, image bytes, credentials or LLM secrets.

## Performance Considerations
**How do we keep it fast?**

- One review per revision; duplicate queue messages coalesce.
- Manual edits use a short server-side quiet period rather than one LLM call per keystroke/upload.
- Do not fetch images if vision is unavailable.
- Cap assets, decoded dimensions, GIF frame count and aggregate bytes.
- Resize/re-encode review copies in memory; never mutate the original asset.
- Use indexed tenant-scoped scans for due review tasks, held schedule intents and publish attempts requiring reconciliation.
- Keep policy resolution as a direct tenant lookup initially; avoid cache drift across hosts.
- All LLM usage remains under cost ledger/monthly cap.

## Security Notes
**What security measures are in place?**

- Built-in grants are locked: Admin gets read/write/approve/publish + system:config; Marketer gets read/write/approve only; `content:publish` is admin-only and never inherited from content:write or legacy content.approve.
- Reviewer agent differs from generator agent when attribution exists; human override requires reason and audit.
- Server-owned tenant/item asset records plus bounded namespaced storage reads prevent cross-tenant access/path traversal/SSRF.
- Guard publisher/provider endpoints with HTTPS, no redirects/URI credentials, `UseProxy=false`, validation of every resolved A/AAAA address and mixed-answer/DNS-rebinding rejection. Private exceptions require operator-owned exact-origin+CIDR allowlists; any proxy must enforce equivalent validation.
- Validate image hash/magic/MIME/dimensions/size/frame count at review time.
- Treat body, KB, learned reviewer memory, OCR-visible text, images and metadata as delimited untrusted data outside system instructions.
- Tenant-dispatched jobs cross-validate tenant on every related row.
- Published content and active publish-claim snapshots are immutable.
- Enforce DB-backed per-item cooldown/per-tenant concurrency and low-volume policy/publish reconciliation limits.
- Explicit audit stores stable codes/IDs/hashes only; generic audit excludes body/assets/prompts/provider payloads and raw exceptions.

## Phase 6 runtime gate (code)

- `Content:WorkflowWriter:Version` (default 1) stamped via `ContentWorkflowWriterSessionInterceptor` into SESSION_CONTEXT key `clawbot_content_writer_version`.
- Singleton `dbo.content_workflow_runtime_gate` + triggers on `content_publish_attempts` / `content_schedule` reject paused or under-version writers.
- `IContentWorkflowRuntimeGate` is cached 15s: missing table is publication-permissive (expand/bridge); unreadable gate fails closed to paused.
- `ContentPublishJob` early-returns before provider when paused.
- `ContentWorkflowHealthJob` every 5 minutes logs debt/pause Warnings (system_logs via Warning sink); cooldown uses `IMemoryCache`.
- Operator helpers: `deploy/manual_content_workflow_runtime_gate_ops.sql` and deployment runbook. Live pause/raise-minimum/fence/drain/classify/resume is ops-only.

