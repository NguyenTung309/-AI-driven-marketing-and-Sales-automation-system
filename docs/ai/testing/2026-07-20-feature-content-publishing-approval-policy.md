---

phase: testing

title: Testing Strategy

description: Define testing approach, test cases, and quality assurance

---



# Testing Strategy — `content-publishing-approval-policy`



## Test Coverage Goals

**What level of testing do we aim for?**



- TDD for every new invariant: write failing tests before implementation.

- At least 80% coverage for changed/new code; target 100% branch coverage for domain transition methods and permission/policy parsing.

- Unit tests: domain, policy versioning, review-task leasing, reviewer capability/completeness, asset ownership/sampling, schedule intent and publish-attempt transitions.

- Integration tests: EF concurrency/indexes, tenant dispatch, endpoints/RBAC, generation → durable review task → approval → schedule intent → publish claim/reconciliation.

- E2E: shared policy control on `/content` and `/agents`, automatic and human-required workflows, stale edit behavior.

- Regression: existing retry/cancel/calendar, ContentReviewSlaJob, publisher, notifications and unrelated Agent approval settings.



## Unit Tests

**What individual components need testing?**



### Phase 0 RED acceptance baseline — 2026-07-20



Initial compile-safe RED coverage was added before production changes:



- `tests/Clawbot.Domain.Tests/Content/ContentWorkflowTests.cs`

  - revision 1/pending defaults;

  - current-revision review and approval preconditions;

  - body/asset invalidation;

  - in-flight stale review result rejection;

  - required and valid human override paths;

  - unconditional publishing backstop;

  - mandatory schedule revision.

- `tests/Clawbot.Domain.Tests/Tenants/TenantOrchestrationTests.cs`

  - `human_required` default;

  - closed policy set;

  - monotonic version/timestamp behavior;

  - same-value idempotency.

- `tests/Clawbot.Api.Tests/ContentPublishingPolicyPermissionTests.cs`

  - canonical policy read/admin-write permissions;

  - `content:approve` human decisions;

  - `content:publish` retry/reconciliation;

  - absence of the legacy `content:write` immediate-publication route.

- `tests/Clawbot.Infrastructure.Tests/Content/ContentAutoSchedulerContractTests.cs`

  - initial compile-safe seam requiring the shared scheduler, golden-hour resolver and revision-bound schedule creation.



Expected RED results:



| Command/filter | Result | Expected missing contract |

|---|---:|---|

| Domain `ContentWorkflowTests|TenantOrchestrationTests` | 19 failed, 27 passed | tenant policy and revision-bound content/schedule APIs |

| API `ContentPublishingPolicyPermissionTests` | 3 failed | canonical policy endpoints and corrected RBAC routes |

| Infrastructure `ContentAutoSchedulerContractTests` | 1 failed | `ContentAutoScheduler` |



All test projects compile; failures are assertions against absent contracts, not compiler failures. Reflection and source-contract checks are temporary Phase 0 seams only. Phase 1 must convert domain cases to strongly typed tests as APIs land. Phase 3 must replace the scheduler source contract with a deterministic fake-`IGoldenHourResolver` behavior test plus transaction integration coverage. Phase 4 must add authenticated HTTP permission tests; source checks are not the authorization acceptance boundary.



### Phase 1 GREEN completion — 2026-07-20



Verified after tenant/item/schedule implementation, publishing fail-closed bridge changes and the manual legacy classifier:



| Suite | Result |

|---|---:|

| Full `Clawbot.Domain.Tests` | 197 passed |

| Focused workflow/retention/audit Infrastructure suites | 16 passed |

| `ContentReviewSlaJobTests` | 4 passed |

| `ContentPublishJobTests` | 14 passed |

| Full `Clawbot.Infrastructure.Tests` | 352 passed, 1 planned Phase 3 RED |

| `dotnet build Clawbot.sln --no-restore` | 0 warnings, 0 errors |

| Disposable SQL Server `0076`/`0077` fresh apply + replay | passed |

| SQL tenant/scope, active-slot, legacy/current pending and trusted-constraint smoke | passed |

| Manual cutover success fixture + zero-mutation replay | passed |

| Manual cutover fail-closed/rollback matrix | passed |

| Manual cutover exact-schema, marker and audit-integrity hardening matrix | passed |



Additional regressions cover generator/reviewer separation, passed-text plus failed-image override attribution, final human rejection terminality, immutable schedule approval context, terminal posted/canceled states, active-revision-slot transitions, stale schedule revision, tenant-safe durable references, abandoned lease recovery, typed bounded asset snapshots, publish-attempt scope coherence, generic audit payload exclusion, pre-provider `publishing` persistence, bounded machine-safe publisher errors, and the guarantee that `content.publish` queues durable state without calling a provider inline.



The manual classifier was exercised with published history, draft/approved/scheduled/rejected rows, an automatic-policy tenant, legacy NULL-revision pending and current held schedules, current/stale review tasks, a soft-deleted unpublished row, and existing future-dated rows. Replay preserved every item/task/schedule rowversion plus audit and paired marker timestamps. Failure fixtures proved no marker, audit or item mutation for `publishing`/`outcome_unknown` schedules, `claimed`/`transmitted`/`outcome_unknown` attempts, active item attempt IDs, duplicate active schedules, stale revision-bound schedules, posted-plus-active schedule conflicts, active schedules on published items and untrusted required constraints. Exact-schema fixtures reject missing or wrong-scope FKs, wrong FK schema/action, same-name weak checks, wrong-key or wrong-filter indexes, wrong-schema substitute indexes, and `NOT FOR REPLICATION` constraints. Audit fixtures reject orphan/backdated markers, missing resource evidence, NULL/malformed payloads and count mismatches; an audit-tampering trigger causes the entire cutover, marker and workflow mutations to roll back.



The compile-safe `ContentAutoScheduler` source contract remains intentionally RED until Phase 3. Canonical-policy API RED coverage remains deferred to Phase 4 and is not part of the Infrastructure suite.

### Phase 3 approval routing and publishing — GREEN 2026-07-21

Follow-up 2026-07-21: ContentAutoScheduler accepts optional desiredPublishAt; manual HTTP schedule uses it; unit tests cover explicit time + past rejection. ContentPublishTool DI simplified; 95 AgentService content tool/coordinator tests green.


| Suite | Result | Notes |
|---|---|---|
| ContentAutoSchedulerTests + contract | 8 passed | golden hour, revision, idempotent active intent, user-cancel terminal, missing FB target held |
| ContentReviewCoordinatorTests | 77 passed | automatic+passed → approve+schedule; human/non-pass leave draft |
| ContentToolsTests | schedule tool via auto-scheduler | caller time ignored; golden-hour intent only |
| ContentPublishJobTests | expanded | claim → ContentPublishAttempt snapshot; outcome_unknown locks edit; retry sequence |
| ContentWorkflowPersistenceTests | active claim filtered unique index | status claimed/transmitted only |
| Domain PublishAttempt | 4 passed | sequence-aware idempotency key |

Phase 3 wires: ContentAutoScheduler; coordinator automatic routing; human approve/reject endpoints; ContentPublishAttempt claim in ContentPublishJob; reconciliation job (no blind retry); ContentReviewSla distinguishes agent vs human; content.schedule tool delegates to auto-scheduler.





### Phase 2.1–2.2 coordinator acceptance and implementation — 2026-07-21



Strongly typed coordinator coverage now fixes the behavior boundary before implementation:



- running state and deterministic started audit commit before the external review call, with no database transaction held across the call;

- duplicate delivery including a synchronized SQL Server initial-claim race, completed redelivery, tenant isolation, joint task/token matching, exact-expiry/reclaimed lease fencing, final-validation-to-commit old-owner fencing, same-revision rowversion conflicts and stale revision handling;

- reviewer/generator independence through a narrow unattributed fail-closed domain transition, local/foreign same-code reviewer collisions, exact external request snapshots, automatic versus human-required policy snapshots, and policy resolution after the external call inside the completion transaction;

- all four terminal review statuses, exact caller-token forwarding, provider-owned cancellation, provider exception and anchored reason-code sanitization, with no body, prompt fragment, credential-like value or raw provider error persisted;

- exact bounded audit payloads, deterministic event keys/state sequences/timestamps and atomic rollback when started, stale, task completion or completed-audit persistence fails;

- Phase 2 never grants publishing approval or creates a schedule.



Phase 2.2 GREEN results:



| Suite | Result | Verified behavior |

|---|---:|---|

| Focused `ContentReviewCoordinatorTests` | 77 passed | closed status/reason validation; once-per-lease claim; running commit before external execution; late lease, revision and rowversion fencing; policy snapshot and audit atomicity; terminal disposition for permanently ineligible work; no approval/schedule |

| SQL Server `ContentPublishingPolicyLockTests` | 10 passed | `UPDLOCK`/`HOLDLOCK` retained through completion; initial-claim and final-fence races; replacement lease completes; database-time claim/completion fences reject expired leases even with a stale application clock; database-valid leases complete when the application clock is ahead |

| Full `Clawbot.Domain.Tests` | 210 passed | claim lifecycle, policy-snapshot-only transition and aggregate image-status/count invariants remain green |

| Full `Clawbot.AgentService.Tests` | 167 passed | no AgentService regressions |

| `dotnet build Clawbot.sln --no-restore` | 0 warnings, 0 errors | full solution compiles cleanly |

| Migration `0078` + repair | fresh apply, replay and repair passed | `claimed_lease_token` is created idempotently for fresh and existing databases |



SQL Server claim and completion use conditional updates with `lease_expires_at > SYSDATETIMEOFFSET()` at the persisted linearization point. Lease transition timestamps on SQL Server are read from `SYSDATETIMEOFFSET()` so domain `Complete`/`Fail` cannot reject a lease that the database fence just accepted. Integration fixtures seed lease expiry relative to database time rather than a fixed wall clock.



Phase 2.2 remains deliberately incapable of approval or scheduling. Exact requested/sent/reviewed asset-set and cardinality binding is still an explicit Phase 2.7–2.12 acceptance gate and must be green before Phase 3 automatic approval is enabled.



Phase 2.3 GREEN results:



| Suite | Result | Verified behavior |

|---|---:|---|

| `ContentDurableEntitiesTests` | 20 passed | exact-expiry reclaim; `FailExhausted` accepts pending/expired-at-limit and rejects active/under-limit |

| Focused worker/dispatch/registration | 23 passed | active-tenant enumeration, fresh scope per tenant, explicit tenant filters without ambient HTTP tenant, exact-expiry reclaim, lease-before-dispatch, backoff/final fail, cancellation leaves lease for recovery |

| Focused `ContentReviewCoordinatorTests` | 77 passed | no coordinator regression after tenant-aware `ProcessAsync` overload |

| AgentService build | 0 warnings/errors | dispatch pipeline and temporary fail-closed executor resolve |



Phase 2.3 still does not approve or schedule. The temporary executor always returns `failed`/`reviewer_error` until the strict provider completion contract is implemented in 2.5–2.12.



### Domain: `ContentItem`



- [ ] New item starts revision 1 with review pending and no publishing approval.

- [ ] `BeginAgentReview` only starts current revision and increments bounded attempts.

- [ ] Current-revision `passed` result is recorded without implying human approval.

- [ ] Stale review result is rejected/ignored.

- [ ] Automatic policy + passed review approves current revision.

- [ ] Automatic policy + reject/needs_human/failed never auto-approves.

- [ ] Human-required policy remains human-pending after any completed review.

- [ ] `human_approval_requirement_reason=migration_cutover` forces human even for automatic tenant and clears only after human decision or a new post-cutover revision.

- [ ] Human override of non-pass requires reason.

- [ ] Human pass approval records `human`; non-pass override records `human_override`.

- [ ] Body edit increments revision and clears review/approval.

- [ ] Asset edit increments revision and clears review/approval even when vision is unavailable.

- [ ] Published content cannot be edited.

- [ ] Scheduling/publishing require reviewed and approved current revision.

- [ ] Final reject prevents scheduling.



### Domain: tenant policy



- [ ] Default is `human_required`.

- [ ] Accepts `automatic` and `human_required` only.

- [ ] Legacy content-review flag cannot disable the invariant.



### Reviewer and capability



- [ ] No assets uses mandatory text client and returns `not_applicable`.

- [ ] Nullable `llm_configs.supports_vision` override beats known-model registry; null uses registry/unknown; LLM config/rebind invalidates cache.

- [ ] Vision unavailable skips asset reads, uses text review client and returns `skipped_unsupported`.

- [ ] Vision available returns `reviewed` only when canonical duplicate-free `requestedPartIds == sentPartIds == reviewedPartIds` as sets with equal cardinality; completion is non-refused/non-filtered/untruncated.

- [ ] Duplicate/colliding requested IDs, missing/extra/duplicate sent or reviewed IDs, unsupported MIME, `finish_reason=length`, empty output, refusal or content filter becomes human fallback.

- [ ] Vision unknown + typed unsupported falls back to text; generic provider error becomes human fallback.

- [ ] Mandatory text review requires explicit terminal success, allowed finish reason, no refusal/filter/truncation; EOF without completed event, malformed SSE, max-token finish, empty output and unknown state fail closed.

- [x] Entire trimmed output must be one closed-schema JSON object; reject prose/fences/prefix/suffix/trailing data/multiple objects/duplicate or unknown properties/type mismatches/oversized reason.

- [ ] Serialized Anthropic/OpenAI/Responses/openai-compatible requests keep trusted instructions in a distinct role/field; incompatible gateways route human.

- [ ] KB evidence and learned reviewer memories are delimited untrusted data, never system instructions.

- [ ] Poisoned memory/KB/body/Unicode-encoded instructions and instructions embedded in image text force `needs_human`, not approve.

- [ ] Reviewer identity differs from generator identity.

- [ ] Usage/cost is recorded for text and vision paths.



### Assets/GIF



- [ ] Upload lifecycle: reserve `uploading`; success transaction marks ready + revises item + creates task; object/DB failures produce compensation/delete_pending; cleanup removes abandoned/orphan objects.

- [ ] Ready tenant/item rows are the authoritative asset set; remove transitions out of ready and increments revision; review/publish snapshot never reads editable `AssetsJson`.

- [ ] Review worker will not lease a revision until the current asset set is ready/stable.

- [ ] Client-supplied storage key, cross-item/cross-tenant asset ID, absolute path, dot segment, encoded separator, backslash, query/fragment is rejected.

- [ ] Legacy external/ambiguous URL is never fetched or auto-published; import/reconciliation is required.

- [ ] MIME/magic mismatch rejected.

- [ ] Size/dimension/asset-count caps enforced.

- [ ] GIF sampler chooses deterministic evenly-spaced frames up to cap.

- [ ] Single-frame GIF behaves as one frame.

- [ ] Corrupt/oversized GIF fails safely.

- [ ] Cancellation/timeout releases buffers.



### Scheduler



- [ ] Uses `IGoldenHourResolver.ResolveNext` for platform.

- [x] Approval and schedule intent commit atomically with mandatory revision and persisted golden time.

- [x] Concurrent inserts result in one active intent.

- [x] Missing target leaves the same intent held; retry does not move golden time.

- [ ] User cancel sets `status=canceled` + `last_error_code=canceled_by_user`; recovery does not recreate it.

- [x] Reschedule is explicit/audited and updates the intended time safely.

- [x] Schedule stores/validates current revision.



### Publish claim/idempotency



- [x] Conditional claim and edit race: exactly one wins; edit cannot commit after active claim.

- [x] Claimed attempt freezes exact body/assets snapshot and revision.

- [x] Stable idempotency key is reused for the same attempt.

- [ ] Provider success then process/DB failure is recovered without blind repost.

- [ ] Timeout after transmission becomes `outcome_unknown`.

- [ ] Outcome unknown requires provider reconciliation or `content:publish` decision.

- [ ] Concurrent manual and recurring retries produce one active attempt.

- [ ] Provider-native idempotency/reconciliation mapping is tested where supported.



## Integration Tests

**How do we test component interactions?**



### Database and migration



- [ ] Tenant policy value/version/updated-at exist with `human_required` default.

- [ ] Review/approval/revision columns and image status defaults exist.

- [ ] `content_review_tasks`, lifecycle `content_assets`, `content_publish_attempts`, runtime gate, `audit_logs.event_key/state_sequence`, filtered unique audit index and `content_workflow_metrics_hourly` exist.

- [ ] SESSION_CONTEXT writer version trigger permits bridge/new version and rejects absent/lower version after minimum is raised; publication pause blocks outward workflow writes.

- [ ] Rowversion and policy-version concurrency raise conflicts for stale writers.

- [ ] Revision-aware active schedule uniqueness works; legacy NULL revisions are held.

- [ ] Column/table migration and later index/constraint migration both replay on fresh DB; repair script handles existing schema.

- [ ] Unsafe `backfill_content_agent_review.sql` is no longer invoked.

- [ ] Published legacy rows become history-only `legacy_exempt`; every unpublished/scheduled row loses inherited review/approval and is forced human after fresh review.



### Generation/review pipeline



- [x] gRPC Generate stamps generator Agent and transactionally inserts exactly one review task.

- [x] Every Repurpose variant gets a unique item/revision task.

- [ ] API body/upload edit atomically increments revision, cancels stale intent and inserts/coalesces the new task.

- [ ] Tenant dispatcher and lease allow one winner across replicas; abandoned lease is recovered.

- [ ] Two-tenant fixtures with deliberately mismatched task/item/asset/policy references are rejected.

- [ ] Concurrent edit while reviewer runs discards stale result.

- [ ] Policy change versus review completion is tested in both commit orders; applied version is deterministic.

- [ ] Explicit audit row and outbox message commit with transition; simulated outbox enlistment failure rolls back or retains retryable event instead of silently clearing it; payload contains no body/assets/raw provider error.

- [ ] Generic audit excludes Body, AssetsJson, prompts, signed URLs and provider payloads.

- [ ] Guarded publisher/vision transport rejects loopback/RFC1918/link-local/metadata IPs, URI credentials, redirects, DNS changes and mixed A/AAAA answers; `UseProxy=false` by default and credentials never redirect.

- [ ] Private gateway exception is operator-only exact-origin+CIDR (not tenant/blanket), validates every connection address; system/environment proxy cannot bypass checks and an approved proxy proves equivalent validation.



### API/RBAC

- [x] Policy GET requires `content:read`; policy PUT requires `system:config`. (`ContentPublishingPolicyEndpoints` + source-contract tests GREEN 2026-07-21)
- [x] LLM config GET/PUT persists nullable `supportsVision` under existing `llm-configs:manage` permission and validates tri-state input. (Phase 2.6)
- [x] Invalid policy receives 400 stable error code. (`content.publishing_policy_invalid`)
- [x] Changing policy does not reschedule/re-evaluate waiting items. (PUT only mutates tenant + audit)
- [x] Human approve/reject requires `content:approve`; publish retry/reconciliation requires `content:publish`.
- [x] `content:write` alone cannot approve, retry delivery or cause an external post. (legacy `/schedule/{id}/retry` removed; new routes gate publish)
- [x] Stale `expectedRevision` and edits during active publish claim receive 409. (approve/reject/edit/upload guards)
- [x] Non-pass approval without override reason receives 400. (Phase 3.5 retained)
- [x] Review retry requires `content:write`, uses DB cooldown/idempotency and only upserts task state.
- [x] Legacy admin GET is compatible; legacy PUT returns stable deprecation error and mutates nothing. (`content.review_setting_deprecated`)
- [x] Canonical `content.review` and any temporary alias are non-publishing; direct publish/schedule tools are absent from autonomous defaults.
- [x] Built-in truth table on default and non-default tenants: Admin gets read/write/approve/publish+system:config; Marketer gets read/write/approve only; other roles no inferred grant; legacy `content.approve` does not grant publish. (RbacSeeder Matrix 2026-07-21)



### Frontend (Phase 5) — GREEN 2026-07-21

- [x] Shared client exposes policy GET/PUT, agentReview/publishingApproval on ContentItem, expectedRevision approve/reject, agent-review retry, durable publish retry/reconcile.
- [x] Shared `ContentPublishingPolicyControl` uses React Query key `['content','publishing-policy']` and is mounted on both `/content` and `/agents`.
- [x] `/content` shows workflow + agent review + publishing approval badges, “Duyệt phát hành”, override dialog for non-pass, reject dialog, edit-reset warning, review retry (`content:write`), publish retry gated by `content:publish`.
- [x] `/agents` no longer mutates deprecated `requireContentReview`; policy is canonical content endpoint only.
- [x] `tsc -b` clean; ESLint clean on touched files (query-key kept module-private for react-refresh).
- [ ] Playwright dual-screen policy sync and full operator flows remain open under End-to-End Tests.

### Rollout / runtime gate (Phase 6) — code GREEN 2026-07-21

- [x] Migration 0080 + repair create singleton content_workflow_runtime_gate and claim/attempt writer triggers.
- [x] run-all existing-DB repair applies 0079_llm_config_supports_vision.sql and 0080_content_workflow_runtime_gate.sql after 0078.
- [x] AppDbContext connections stamp SESSION_CONTEXT clawbot_content_writer_version via interceptor.
- [x] ContentPublishJob no-ops provider calls when publication_paused=1 (unit test).
- [x] ContentWorkflowRuntimeGate: missing table / SQLite expand path stays permissive; snapshot cached 15s.
- [x] ContentWorkflowHealthJob registered every 5 minutes; emits Warning debt signals with IMemoryCache cooldown (unit tests).
- [ ] Live cutover (pause, raise minimum, outbound fence, drain old binaries, classify, resume) remains operator-run.
- [ ] Rollback drill on staging remains operator-run.

### End-to-end backend flows



- [ ] Automatic + text passed → automatic approval → next golden hour schedule → publish.

- [ ] Automatic + text non-pass/error → human queue, no schedule.

- [ ] Human-required + passed → human approve → next golden hour schedule → publish.

- [ ] Human-required + non-pass → human override with reason → schedule → publish.

- [ ] Edit after review cancels stale schedule, increments revision and blocks publish until re-reviewed/re-approved.

- [ ] Vision unavailable + image → text review can auto schedule with `skipped_unsupported` visible.

- [ ] Vision available + PNG/JPEG/WebP → images are reviewed.

- [ ] Vision available + GIF → sampled frames are reviewed.

- [ ] Vision path asset failure → human fallback.

- [ ] Missing target → revision-bound schedule intent remains held at the original golden time.

- [ ] Edit racing with publish claim → either edit wins and claim fails, or claim wins and edit receives 409; no stale live-item read.

- [ ] Provider accepts then process/DB fails → attempt becomes/reconciles from `outcome_unknown` without duplicate post.

- [ ] HTTP and Agent tool paths cannot bypass the due worker/claim pipeline.

- [ ] With pause/minimum/outbound fence active, dormant old/no-version worker, direct tool, HTTP retry and manual AdminJobs trigger make zero provider calls.

- [ ] Rollback drill starts from a resumed system, reactivates outbound fence before process changes, reconciles attempts while fenced, restores provider access only after rollback verification, and makes zero provider calls during rollback.



## End-to-End Tests

**What user flows need validation?**



Using Playwright against the running app:



- [ ] Tenant admin changes policy on `/content`; `/agents` reflects the same value after navigation/refetch.

- [ ] Tenant admin changes policy on `/agents`; `/content` reflects it.

- [ ] Non-admin sees read-only policy on both screens and cannot mutate via UI/API.

- [ ] Automatic text-only item progresses through review to an auto-created golden-hour schedule.

- [ ] Human-required item shows Agent result, waits for human, then schedules after one approval click.

- [ ] Non-pass item opens override-reason dialog and records override.

- [ ] Editing body or uploading/removing an asset resets review/approval badges and warns the user.

- [ ] Vision unavailable displays “Ảnh chưa được Agent review do model không hỗ trợ”, not a false success label.

- [ ] Calendar still permits optional reschedule/cancel after auto scheduling.

- [ ] Retry/failure states show actionable messages without leaking internals.



Visual/responsive checkpoints: 320, 375, 768, 1024, 1440 and both configured themes if applicable.



Accessibility:



- [ ] Policy options form a labeled keyboard-operable radio group.

- [ ] Focus returns correctly after approve/override dialogs.

- [ ] State is not communicated by color alone.

- [ ] Error messages are associated with fields.

- [ ] Reduced-motion mode does not hide workflow feedback.



## Test Data

**What data do we use for testing?**



Fixtures:



- Tenant default human policy and tenant automatic policy.

- Users with `system:config`, `content:approve`, `content:publish`, `content:write`, and read-only combinations.

- Generator and distinct reviewer Agent definitions.

- Reviewer bindings: text-only, vision-capable, unknown/unsupported, missing and throwing.

- Content: text-only, one PNG, multiple JPEG/WebP, animated GIF, corrupt image, external URL legacy asset.

- Meta assets: default active page, no page, inactive page.

- Concurrency: two DbContexts loading the same item/revision.

- Legacy rows: published with blanket Agent ID, scheduled, human-approved draft and plain draft.



Use in-memory/stub provider transports for request mapping tests; do not require live LLM credentials in CI.



## Test Reporting & Coverage

**How do we verify and communicate test results?**



- Record each suite command, pass/fail count and unrelated baseline failures.

- Generate coverage with repo-supported .NET collector and frontend runner if present.

- Block completion when changed/new business logic is below 80% or critical branch tests are missing.

- Critical zero-tolerance branches: stale revision/policy version, non-pass auto prevention, edit invalidation, tenant/asset ownership, publish claim race, outcome_unknown reconciliation, admin policy permission and external side-effect authorization.

- Attach Playwright screenshots/traces for failed critical flows.

- Do not report a suite as passed if it was skipped or could not compile.



## Manual Testing

**What requires human validation?**



- Review Vietnamese copy on both pages for the distinction between Agent review and publishing approval.

- Verify vision capability/skipped messaging is understandable and not misleading.

- Confirm policy change warning explains non-retroactive behavior.

- Confirm human override reason is visible in audit/admin logs.

- Test a real staging social publish at the system-selected golden hour or a controlled accelerated clock.

- Confirm existing custom reschedule/cancel workflow remains usable.



## Performance Testing

**How do we validate performance?**



- Measure text-only review latency/cost before and after coordinator change.

- Measure vision path for max allowed static images and max sampled GIF frames.

- Load test duplicate review queue messages to verify coalescing.

- Simulate backlog recovery with multiple tenants and ensure bounded concurrency.

- Simulate concurrent schedule creation and verify one row/post.

- Verify image decode memory stays within cap and cancellation returns memory promptly.



Suggested acceptance targets:



- Policy GET p95 comparable to other tenant settings endpoints.

- Coordinator adds no extra LLM call for text-only review.

- Duplicate queue/retry requests do not multiply LLM calls for the same revision.

- Schedule uniqueness prevents duplicate intents; durable publish claim/idempotency/reconciliation prevents blind duplicate external attempts.



## Bug Tracking

**How do we manage issues?**



Severity:



- Critical: unreviewed text publishes; duplicate external post; cross-tenant asset/policy access; stale revision publishes.

- High: non-pass auto-approves; non-admin changes policy; edit does not invalidate; schedule silently disappears.

- Medium: incorrect workflow label/capability status; recovery delay; audit metadata incomplete.

- Low: copy, layout or optional reschedule polish.



Every bug fix adds a regression test at the lowest effective layer plus an integration/E2E test when it crosses boundaries.





### Phase 2.4 Normalize generation attribution — GREEN 2026-07-21



| Suite | Result | Notes |

|---|---|---|

| ContentAgentGrpcServiceTests | 3/3 | Generate stamps generator + one review task; missing content-agent fails closed; Repurpose stamps every variant + unique task |

| ContentGenerateTool attribution | 2/2 | stamps CreatedByAgentId + pending task; rejects null AgentDefinitionId |

| ContentGenerateTool existing persist smoke | 2/2 | still green with required AgentDefinitionId |



Domain ContentWorkflowTests still 60/60 after legacy-bridge/migration_cutover hardening.





### Phase 2.5 Strict review completion contract/parser — GREEN 2026-07-21



| Suite | Result | Notes |

|---|---|---|

| StrictContentReviewOutcomeParserTests | passed | terminal/refusal/filter/truncation envelope; exact closed-schema JSON; approve→passed; prose/fences rejected |

| ContentReviewerTests.Parse_* | passed | legacy path fail-closed; prose no longer approved |

| ContentReviewerTests full | 17/17 | KB suggestion + evidence paths still green |



Trusted/untrusted role-preserving wire adapters still deferred to 2.7–2.8.

### Phase 2.6 Deterministic vision capability — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| LlmConfigTests.SupportsVision_* | passed | nullable tri-state Create/UpdateConnection |
| LlmVisionCapabilityResolverTests | 13 passed | override > registry > unknown; openai-compatible unknown; cache key by config version |
| Migration 0079 + run-all repair | present | supports_vision BIT NULL |
| FE LlmProvidersPage / llmConfigs types | present | tri-state select + payload mapping |

### Phase 2.7 Provider adapter tests — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| ContentReviewCompletionAdapterTests | 20 passed | system/user separation; finish flags; SSE completed/EOF/malformed; requested/sent IDs; usage/cost; factory routing |

### Phase 2.8 Optional vision adapters — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| ContentReviewCompletionAdapterTests | 20 passed | OpenAiChat/Anthropic/OpenAiResponses review clients; trusted system separate; envelope usage/cost |

### Phase 2.9 Server-owned content_assets lifecycle — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| ContentAssetLifecycleTests | 4 passed | sha256, derived JSON assetId ordering, quiet review task, sort order |
| ContentDurableEntitiesTests.Asset_* | 3 passed | reserve/ready/delete terminal |
| ContentEndpoints UploadItemAssetAsync | present | reserve → upload → MarkReady + ReviseAssets + quiet task; fail marks failed |



### Phase 2.10 Bounded ContentAssetReader — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| ContentAssetReaderTests | 16 passed | path defenses, integrity, size/type, sort order, caps |

### Phase 2.11 GIF frame sampler — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| GifFrameSamplerTests | 10 passed | even indexes, single non-GIF, multi-frame part IDs, size/frame caps, corrupt fail-closed |

Pure GIF splitter in Agents.Core (no ImageSharp). Caps: `MaxInputBytes=5MB`, `MaxDetectedFrames=64`, default sample 4.

### Phase 2.12 Extend ContentReviewer — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| ContentReviewerTests | expanded | memory/KB in untrusted user parts; suspicious instructions → needs_human; vision completeness; unknown unsupported fallback |
| StrictContentReviewOutcomeParserTests | expanded | vision `reviewedPartIds` completeness |
| ContentReviewCompletionAdapterTests | 20 passed | still green |
| GifFrameSamplerTests | 10 passed | used by vision path |
| AgentService ContentReview/ReviewTenant | 97 passed | ContentReviewExecutor wired |
| LlmConfigResolverTests | 7 passed | SupportsVision/ConfigId/UpdatedAt on ResolvedLlmConfig |

Semantics locked: system persona is trusted-only; automatic review never reuses ClaudeReply; unavailable vision → text + `skipped_unsupported`; unknown typed unsupported → text fallback; other errors fail closed.

### Phase 2.13 Explicit transactional audit/outbox — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| DomainEventDispatchInterceptorTests | 2 passed | success clears events; enlist failure propagates + retains |
| AuditSaveChangesInterceptorTests | 4 passed | Body/AssetsJson excluded; ContentAsset IAuditExempt |
| ContentReviewCoordinator audit helpers | present | EventKey content-review:{taskId}:{transition} + stateSequence |

### Phase 2.14 Guard outbound transports — GREEN 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| LlmBaseUrlGuardTests | 7 passed | private DNS reject, mixed answers, DNS fail-closed, operator private grant, userinfo reject, CreateGuardedHttpClient |
| HttpSocialPublisher | endpoint gate | publisher_endpoint_not_allowed when base URL fails LlmBaseUrlGuard |

### Phase 2.15 GREEN + refactor — 2026-07-21

| Suite | Result | Notes |
|---|---|---|
| Agents review/asset/guard focused | 98 passed | ContentReviewer + GIF + strict parser + adapters + BaseUrl + asset reader |
| AgentService ContentReview/ReviewTenant | 97 passed | ContentReviewExecutor wired into coordinator path |
| Infrastructure audit/outbox/resolver | 13 passed | DomainEvent retain-on-fail; LlmConfig SupportsVision fields |

Phase 2 did not approve or schedule. Phase 3 publishing auto-approval and schedule intent is GREEN 2026-07-21.

