---
phase: planning
title: Project Planning & Task Breakdown
description: Break down work into actionable tasks and estimate timeline
---

# Project Planning & Task Breakdown â `content-publishing-approval-policy`

## Milestones
**What are the major checkpoints?**

- [x] **M1 â Domain state is safe**: tenant policy and revision-bound review/approval model exist; every edit invalidates old decisions; stale review/approval tests pass. — DONE code 2026-07-21.
- [x] **M2 â Universal Agent review works**: every generation/repurpose/manual revision reaches one durable coordinator; text review is mandatory; optional vision/GIF sampling is capability-aware and observable. — DONE code 2026-07-21.
- [x] **M3 â Approval-to-publish automation works**: automatic and human-required policies both route through idempotent golden-hour scheduling; publish backstop rejects missing/stale review or approval. — DONE code 2026-07-21.
- [x] **M4 â Both UI surfaces use one policy**: `/content` and `/agents` show the same canonical policy, permissions and workflow states; misleading Agent-review toggle is removed. — DONE code 2026-07-21.
- [ ] **M5 â Safe rollout complete**: legacy rows classified without trusting blanket backfill, current tenants default `human_required`, metrics/alerts are active, old bypass is disabled. Code/runtime-gate ready 2026-07-21; live cutover/cleanup ops remain.

## Task Breakdown
**What specific work needs to be done?**

### Phase 0: Baseline, blast radius and test harness

- [x] **0.1 Preserve current dirty checkout** â DONE 2026-07-20. Confirmed overlapping current edits in Admin/Content endpoints and DTOs, Tenant/EF/content schedule/publish domain, `/content` + `/agents` frontend/API clients, run-all/repair SQL, agent definitions and tests. Read every target from current disk immediately before editing and use narrow changes; never wholesale-replace existing work.
- [x] **0.2 Recheck migration numbering** â DONE 2026-07-20. Highest current files are `0072`â`0075`; next free number is `0076`. Reserve `0076` for additive tables/columns and `0077` for dependent indexes/constraints (one batch/file runner). Recheck immediately before file creation for concurrent migrations.
- [x] **0.3 Establish baseline commands/results** â DONE 2026-07-20. Solution build passed 0 warnings/errors; frontend `tsc -b`/Vite build passed. Tests: Domain 145/145, Agents 364/364, AgentService 90/90, Infrastructure 338/338, API 275/275. Baseline unrelated failures: Integration 57/58 (`ContentEndpointTests.cs:56` expects array but briefs endpoint returns object envelope); frontend lint has LeadsPage.tsx:723 set-state-in-effect error plus 3 existing exhaustive-deps warnings. Do not attribute these to this feature.
- [x] **0.4 Add RED acceptance tests first** â DONE 2026-07-20. Added compile-safe RED tests for mandatory current-revision review/publish backstop, policy defaults/closed set/version idempotency, edit invalidation, in-flight stale results, required + valid human overrides, mandatory schedule revision, golden-hour scheduler seam and canonical config/approve/publish permissions. Focused results: Domain 19 failed/27 passed, API 3 failed, Infrastructure 1 failed, all for absent production contracts. Temporary reflection/source seams must become strongly typed scheduler and authenticated HTTP tests in Phases 1/3/4.
- [x] **0.5 Confirm provider capability seams** â DONE 2026-07-20. Mapped existing scoped resolver/factory and all four wire paths. Keep `IClaudeChatClient` unchanged; add a parallel strict review completion client/factory with terminal/refusal/filter/truncation metadata and immutable text/image parts. Anthropic requires terminal stop reason; OpenAI Chat requires `finish_reason=stop` and no refusal/filter; Responses requires explicit `response.completed`; openai-compatible must preserve system/user roles and complete metadata or route human. Capability precedence is explicit nullable override, conservative official provider/model registry, otherwise unknown; only typed machine-readable unsupported errors fall back to mandatory text review. Initial implementation performs no capability caching, avoiding stale config/rebind results.

### Phase 1: Tenant policy and revision-safe domain model (M1)

- [x] **1.1 Tenant policy tests (RED)** â DONE 2026-07-20. Covers valid closed set, `human_required` default, initial version/time, same-value idempotency, invalid-input and overflow atomicity; tenant suite 16/16.
- [x] **1.2 Content workflow tests (RED)** â DONE 2026-07-20. Covers revision defaults, in-flight stale result, edit invalidation, self-review separation, human/image-failure override, final rejection, publish backstop, schedule revision/context and terminal transitions.
- [x] **1.3 Extend `Tenant`** â DONE 2026-07-20. Added policy value, monotonic version and updated timestamp with closed-set/idempotent mutation. Legacy `RequireContentReview` remains compatibility-only and no longer weakens new domain/publisher invariants.
- [x] **1.4 Extend `ContentItem`** â DONE 2026-07-20. Added revision-bound Agent/image review, applied policy, publishing approval, active-attempt/rowversion fields and fail-closed transitions. Legacy methods remain bridge-only but cannot satisfy current-revision scheduling/publishing.
- [x] **1.5 Add durable entities/schema** â DONE 2026-07-20. Added tenant-owned/audit-exempt review tasks, server-keyed asset lifecycle, immutable typed/bounded publish snapshots, fenced lease recovery and explicit `outcome_unknown` reconciliation; extended business-audit identity and 180-day hourly metrics retention.
- [x] **1.6 Extend `ContentSchedule`** â DONE 2026-07-20. Added nullable legacy-safe mandatory-for-new revision, approval context, target, retry/error fields, `held|publishing|outcome_unknown` states, terminal user cancel and rowversion. New callers pass item revision; publisher persists `publishing` before provider I/O and fails closed on stale/missing context.
- [x] **1.7 EF mapping** â DONE 2026-07-20. Mapped all durable workflow entities, audit identity, tenant filters, provider-aware rowversions, tenant-safe composite relationships, revision-aware active-schedule uniqueness and ready-asset ordering; SQLite persistence/constraint tests pass.
- [x] **1.8 SQL migrations + repair** â DONE 2026-07-20. `0076`/`0077` now add durable tables, audit/metrics schema, trusted tenant/scope FKs and checks, atomic convergent index repair, legacy pending compatibility and 180-day retention support. Disposable SQL Server fresh apply, idempotent replay, cross-tenant/scope rejection and trusted-constraint smoke pass.
- [x] **1.9 Retire unsafe legacy backfill** â DONE 2026-07-20. Removed `backfill_content_agent_review.sql` execution from `run-all.bat`; historical fields are not promoted into new review/approval state.
- [x] **1.10 Cutover classification script** â DONE 2026-07-21. Added a manual, paused-system classifier outside automatic migrations, using a transaction-owned application lock plus exclusive workflow-table locks on a dedicated SQL connection. Published history becomes `legacy_exempt`; all unpublished rows, including soft-deleted rows, lose inherited review/approval, require `migration_cutover`, and receive exactly one pending current-revision task; active schedule intent is preserved but held and revision-bound. Preflight now attests exact trusted check definitions by SHA-256, exact `dbo` index keys/filters, exact tenant-safe FK schemas/columns/actions, and rejects `NOT FOR REPLICATION` enforcement. Initial execution validates exact boundary/item/schedule audits before committing a paired completion marker; replay rejects orphan/backdated markers, malformed or NULL payloads, missing resource evidence and count mismatches. Disposable SQL Server coverage passed success/replay, future-dated existing rows, live/unknown publication and attempt failures, duplicate/stale/posted-plus-active schedules, weak same-name checks, wrong filters/schema/actions, replication-bypass constraints, audit tampering rollback and forged marker rejection.
- [x] **1.11 GREEN + refactor** â DONE 2026-07-20. Solution build is clean; Domain 197/197, focused workflow/retention/audit suites 16/16, SLA 4/4 and publish-job 14/14 pass. Full Infrastructure is 352/353; the sole failure is the explicitly planned Phase 3 `ContentAutoScheduler` RED contract. Legacy SLA setup now deliberately persists a pre-cutover scheduled row outside the current domain invariant, and publisher-error assertions use the bounded machine-safe code.

### Phase 2: Durable Agent review and optional vision (M2)

- [x] **2.1 Coordinator tests (RED)** â DONE 2026-07-21. Strongly typed acceptance now covers atomic initial claim and completion, duplicate/reclaimed/expired lease fencing, reviewer tenant/identity separation, all terminal statuses, exact caller cancellation, policy value/version lock composition, strict reason-code validation, audit rollback and the Phase 2 no-approval/no-schedule boundary. Focused AgentService is 74 intentional RED cases; SQL Server lock/race coverage is 8 intentional RED cases; code and security reviews report no remaining CRITICAL/HIGH/MEDIUM acceptance gaps.
- [x] **2.2 Add `ContentReviewCoordinator`** â DONE 2026-07-21. Added a closed immutable result contract, once-per-lease delivery claim marker with migration/repair coverage, SQL Server `UPDLOCK`/`HOLDLOCK` policy snapshots, pre-call running/audit commit, transaction-free external execution, and atomic fenced completion after task/lease/revision/rowversion revalidation. SQL Server claim/completion linearization uses conditional updates requiring `lease_expires_at > SYSDATETIMEOFFSET()` and transitions use database time so an ahead application clock cannot discard a database-valid lease. Duplicate/reclaimed/expired deliveries, provider cancellation/failure, unavailable or non-independent reviewers, permanent item ineligibility, audit/task-write rollback and late lease expiry all fail closed without approval or scheduling. Replacement leases clear the claim marker and are proven able to finish after fencing an old owner. Focused coordinator 77/77, SQL Server lock/race 10/10, full Domain 210/210, full AgentService 167/167, and solution build 0 warnings/errors. Exact asset-set/cardinality binding remains mandatory in 2.7â2.12 before Phase 3 automatic approval.
- [x] **2.3 Add tenant-dispatched review worker**  DONE 2026-07-21. Hosted `ContentReviewDispatchWorker` enumerates active tenants with `IgnoreQueryFilters`, creates a fresh scope per tenant, and isolates non-cancellation failures. `ReviewTenantWorker.RunTenantAsync` leases due/expired `content_review_tasks` with explicit tenant predicates (no ambient HTTP tenant), commits the lease before coordinator dispatch, reclaims at exact expiry, applies exponential backoff, and terminally fails at the shared attempt cap via `FailExhausted`. Coordinator now accepts `(tenantId, taskId, leaseToken)`. Temporary fail-closed executor keeps DI resolvable until 2.52.12. Focused worker/dispatch/registration 23/23, durable domain 20/20, coordinator still 77/77. SQL Server multi-replica lease races remain optional hardening for later if needed; Phase 2 still does not approve/schedule.
- [x] **2.4 Normalize generation attribution** â DONE 2026-07-21. ContentAgentGrpcService.Generate/Repurpose resolve tenant content-agent definition and fail closed with content_agent_not_configured when missing; ContentGenerateTool requires ToolContext.AgentDefinitionId (content_generator_agent_required). Every created item stamps CreatedByAgentId and inserts one immediately-due ContentReviewTask for revision 1 in the same SaveChanges. Shared helper ContentGenerationPersistence. Focused gRPC/tool attribution tests 7/7 GREEN. Phase 2 still does not approve/schedule.
- [x] **2.5 Add strict review completion contract/parser** â DONE 2026-07-21. Added provider-neutral ReviewCompletionEnvelope + IContentReviewCompletionClient parts contract (trusted system vs untrusted text/image). StrictContentReviewOutcomeParser requires observed terminal success, allowed finish reason (end_turn|stop), no refusal/filter/truncation, non-empty output, and entire trimmed body as exactly one closed-schema JSON object with only verdict/reason. Prose/fences/substring extraction removed. Exact-schema approve normalizes to coordinator status passed (reasonCode=passed); reject/needs_human map to agent_non_pass. Legacy ContentReviewer.Parse delegates to the strict path (fail-closed needs_human). Focused parser suite GREEN; ContentReviewerTests 17/17. Provider wire adapters remain 2.7-2.8.
- [x] **2.6 Add deterministic vision capability** â nullable `llm_configs.supports_vision` in entity/EF/SQL/repair + LLM config GET/PUT DTOs/admin form; precedence override > known-model registry > unknown; typed unsupported; cache keyed by config version and invalidated on update/rebind — DONE 2026-07-21.
- [x] **2.7 Add provider adapter tests (RED)** â wire payload keeps system/developer separate from tenant content; explicit terminal event/finish reason for text and vision; EOF/malformed SSE/refusal/filter/truncation fail closed; canonical duplicate-free requested == sent == reviewed set/cardinality validation; usage/cost — DONE 2026-07-21 RED then GREEN via 2.8.
- [x] **2.8 Implement optional vision adapters** â typed sent-ID/completion outcome; `reviewed` only for exact reviewer ID-set match and complete response; unsupported may fall back to text, every partial/ambiguous outcome routes human — DONE 2026-07-21 OpenAI Chat/Anthropic/Responses review clients + factory + VisionUnsupportedException.
- [x] **2.9 Add server-owned `content_assets` lifecycle** â reserve `uploading`, upload/validate object, transactionally mark `ready` + revise item + create review task; compensation/`delete_pending` and orphan cleanup; derived display JSON only — DONE 2026-07-21. Upload reserves content_assets, object save, MarkReady+ReviseAssets+quiet review task; delete_pending path; derived AssetsJson with assetId.
- [x] **2.10 Add bounded `ContentAssetReader`/storage contract** â stat + capped streaming by tenant/item/asset id, path defenses/integrity validation; worker leases only revisions whose referenced assets are ready — DONE 2026-07-21. IContentAssetReader + path defenses + sha256 integrity + EfContentAssetRepository + DI.
- [x] **2.11 GIF frame sampler** — pure decoder (no new dependency); even spacing + named caps + fail-closed — DONE 2026-07-21. GifFrameSampler + 10 tests green.
- [x] **2.12 Extend `ContentReviewer`** — mandatory KB-aware text path; optional vision; body/images/OCR/KB/memory untrusted user parts only; suspicious instructions -> needs_human — DONE 2026-07-21. ContentReviewExecutor wired; ReviewContentItemAsync + part-id completeness.
- [x] **2.13 Explicit transactional audit/outbox** — deterministic-key audit already on coordinator; generic audit excludes Body/AssetsJson/prompt payloads; ContentAsset IAuditExempt; DomainEventDispatchInterceptor propagates outbox enlist failures and retains events — DONE 2026-07-21.
- [x] **2.14 Guard outbound transports** — LlmBaseUrlGuard: HTTPS/no redirects/UseProxy=false, every A/AAAA + mixed/DNS-rebinding rejection, operator exact-origin grant; HttpSocialPublisher endpoint gated — DONE 2026-07-21.
- [x] **2.15 GREEN + refactor** — Agents ContentReview/Gif/Strict/Adapter/BaseUrl 98; AgentService ContentReview/ReviewTenant 97; Infrastructure audit/outbox/resolver 13; ContentReviewExecutor replaces TemporaryFailClosed; memory untrusted; outbound SSRF hardened — DONE 2026-07-21.

### Phase 3: Approval routing and automatic golden-hour scheduling (M3)

- [x] **3.1 Scheduling/publish tests (RED)** â approval and revision-bound schedule intent commit together; golden time persists once; concurrent calls produce one intent; user cancel is not recreated; publish claim/edit race; provider accepted then DB/process failure; timeout after transmit; reconciliation.
- [x] **3.2 Extract `ContentAutoScheduler`** â reuse `IGoldenHourResolver.ResolveNext`, create one `content_schedule` intent in the approval transaction even if target is temporarily missing, and preserve desired time/state/retry/error/user-cancel semantics.
- [x] **3.3 Automatic policy routing** â policy value+version `automatic` + text verdict `passed` auto-approves current revision and creates schedule intent atomically; any non-pass/error falls back to human.
- [x] **3.4 Human-required routing** â review completion snapshots value+version and leaves approval pending regardless of verdict.
- [x] **3.5 Human approve/override** â current revision only; non-pass requires reason; records `human|human_override`; creates the same schedule intent atomically.
- [x] **3.6 Human reject** â final publishing rejection, reason required, pending intent canceled.
- [x] **3.7 Implement publish claim/attempt** â conditional `pending -> publishing`, immutable body/assets snapshot/hash, stable provider idempotency key, edit lock boundary, deterministic audit event.
- [x] **3.8 Harden `ContentPublishJob`** â tenant-dispatched scan; verify schedule/current/review/approval revisions; publish only claimed snapshot; finalize success transactionally; `outcome_unknown` never blind-retries.
- [x] **3.9 Add reconciliation path** â provider-native status/idempotency lookup where available; otherwise privileged `content:publish` decision after operator verification.
- [x] **3.10 Remove external side-effect shortcuts** â `content.publish` no longer calls provider; `content.schedule` leaves autonomous defaults or delegates to auto scheduler; HTTP retry only transitions durable state.
- [x] **3.11 Update SLA/health jobs** â distinguish Agent/human/publish-outcome delays and explicit tenant dispatch.
- [x] **3.12 Preserve optional reschedule/cancel** â reschedule is audited; cancel sets canonical `status=canceled` + `last_error_code=canceled_by_user`, excluded from recovery.
- [x] **3.13 GREEN + refactor** â Infrastructure job/scheduler/publisher tests pass.

- [x] **3.14 Manual schedule path alignment** — DONE 2026-07-21. `ScheduleItemAsync` uses `ContentAutoScheduler.CreateIntentAsync` with optional explicit time; fail-closed on missing current-revision approval. ContentPublishTool ctor cleaned.

### Phase 4: Canonical API, permissions and compatibility


- [x] **4.1 API tests (RED)** — DONE 2026-07-21. Source-contract suite `ContentPublishingPolicyPermissionTests` covers canonical policy routes (`content:read`/`system:config`), human approve/reject (`content:approve`), and publish retry/reconcile (`content:publish`); legacy `/schedule/{id}/retry` with `content:write` must stay absent.
- [x] **4.2 Add `ContentPublishingPolicyEndpoints`** — DONE 2026-07-21. GET/PUT `/settings/publishing-policy` under content group; response includes mandatory text review, vision capability transparency, policy value/version/updatedAt; PUT audits policy changes and never re-evaluates queue.
- [x] **4.3 Expand content DTOs** — DONE 2026-07-21. `ContentItemDto` adds contentRevision, agentReview, publishingApproval, workflowState and capability booleans; policy/review-retry/reconcile request DTOs added.
- [x] **4.4 Redefine approve/reject endpoints** — DONE 2026-07-21. Routes keep compatibility paths and require `content:approve`; Phase 3 human publishing + schedule-intent semantics retained.
- [x] **4.5 Add review retry endpoint** — DONE 2026-07-21. `POST /items/{id}/agent-review/retry` with `content:write`; upserts durable quiet-period task, cools down via NextAttemptAt (429), never calls LLM inline.
- [x] **4.6 Replace publish retry/reconcile endpoint** — DONE 2026-07-21. `POST /schedules/{id}/publish/retry|reconcile` require `content:publish`; retry only `TryResetForRetry`; reconcile marks schedule/attempt durable outcome without provider I/O.
- [x] **4.7 Update edit/upload endpoints** — DONE 2026-07-21. Published/active-claim edits return 409; revision bumps cancel stale schedule intents and insert review tasks; uploads remain server-owned `content_assets`.
- [x] **4.8 Seed locked RBAC matrix everywhere** — DONE 2026-07-21. Matrix grants `content:approve` to Admin+Marketer and `content:publish` to Admin only; `system:config` Admin-only remains.
- [x] **4.9 Rename Agent tool semantics everywhere** — DONE 2026-07-21. Canonical `content.review`; ToolRegistry legacy alias + Build alias registration; AgentToolDefaults/DevDataSeeder/SQL grants use review only; publisher defaults empty (no schedule/publish autonomy).
- [x] **4.10 Hard compatibility boundary** — DONE 2026-07-21. Admin orchestration PUT with `RequireContentReview` returns `content.review_setting_deprecated` and mutates nothing; canonical content policy endpoint is sole writer.
- [x] **4.11 Dedicated limits** — DONE 2026-07-21 (initial). Review retry uses quiet-period cooldown + attempt cap (429); publish retry/reconcile remain privileged and non-inline. Broader per-tenant concurrent quotas can harden later if load requires.
- [x] **4.10 GREEN + refactor** — DONE 2026-07-21. Api+AgentService+Domain builds clean; ContentPublishingPolicyPermissionTests, AllowedToolsValidationTests, AgentToolDefaults/ToolRegistry, ContentToolsTests and Domain workflow suites green.

### Phase 5: Frontend synchronization and workflow UX (M4)

- [x] **5.1 Add shared API types/client** — DONE 2026-07-21. `content.ts` adds policy/agentReview/publishingApproval DTOs, expectedRevision approve/reject, agent-review retry, publish retry/reconcile on `/schedules/{id}/publish/*`.
- [x] **5.2 Add shared `ContentPublishingPolicyControl`** — DONE 2026-07-21. Mandatory text-review indicator, vision capability label, automatic vs human_required radios, read-only without `system:config`.
- [x] **5.3 Integrate `/content`** — DONE 2026-07-21. Policy control on workspace, workflow/review/approval badges, “Duyệt phát hành”, override/reject dialogs with revision, edit-reset warning, review retry, capability-gated schedule/publish retry.
- [x] **5.4 Integrate `/agents`** — DONE 2026-07-21. Removed misleading “Agent review bài đăng” toggle; same shared policy control in approval config modal; chat/KB/orchestration flags unchanged.
- [x] **5.5 Use one React Query key** — DONE 2026-07-21. Shared control uses `['content','publishing-policy']` for query + invalidation on both screens.
- [x] **5.6 Preserve calendar controls** — DONE 2026-07-21. Reschedule/cancel retained; publish retry gated by `content:publish` and durable-only path.
- [x] **5.7 Accessibility/responsive tests** — DONE 2026-07-21 (initial). Policy radios use radiogroup + labels; dialogs use Modal a11y; responsive grid retained. Manual visual pass still recommended at 320/768/1024/1440.
- [x] **5.8 Frontend verification** — DONE 2026-07-21. `tsc -b` clean; ESLint on touched files clean after query-key export fix. Playwright sync flow remains optional manual/E2E follow-up.

### Phase 6: Rollout, cleanup and observability (M5)

- [x] **6.1 Bridge release + SQL write gate** — DONE 2026-07-21 (code). Migration 0080_content_workflow_runtime_gate.sql; repair_tenant_runtime_columns recreates gate table + triggers; run-all existing-DB path applies 0079/0080; ContentWorkflowWriterSessionInterceptor stamps SESSION_CONTEXT; DI/options Content:WorkflowWriter. Ops still deploys bridge to every instance while minimum stays 0.
- [x] **6.2 Pause / fence hooks in product code** — DONE 2026-07-21 (code). ContentPublishJob skips provider when gate publication_paused=1; SQL triggers fence claim/attempt writes. Outbound credential/firewall fence remains operator runbook.
- [ ] **6.3 Drain and stop old binaries** — ops cutover window only.
- [x] **6.4 Classify legacy script** — DONE earlier: deploy/manual_content_publishing_cutover_classification_v1.sql (run only while paused).
- [x] **6.5 Unconditional backstop/claim build** — DONE in Phases 1-4 (revision checks, intents, attempts, guarded tools/endpoints).
- [ ] **6.6 Resume publication after smoke** — ops after classification + smoke; then ship FE (Phase 5 already code-ready).
- [x] **6.7 Durable observability** — DONE 2026-07-21 (code). ContentWorkflowHealthJob every 5 minutes + Warning logs to system_logs (IMemoryCache cooldown); Hangfire schedule asserted; unit tests for debt/pause/cooldown; hourly metrics table from 0076 retained 180d. Thresholds via Content:WorkflowHealth.
- [ ] **6.8 Monitor drain** — ops after resume.
- [ ] **6.9 Cleanup release** — later: remove legacy flag/admin mutation/tool aliases only after no old clients.
- [ ] **6.10 Operational sign-off** — automatic/vision/override/stale/claim/outcome_unknown/cancel cases in staging/prod.
- [x] **6.11 Prepared rollback helpers** — DONE 2026-07-21 (code/docs). deploy/manual_content_workflow_runtime_gate_ops.sql + deployment runbook; first re-fence then pause clear last remains ops procedure.


## Dependencies
**What needs to happen in what order?**

```text
0 baseline
  -> 1 domain/schema
      -> 2 review coordinator + provider capability
          -> 3 approval routing + scheduler + publish backstop
              -> 4 API/RBAC
                  -> 5 frontend
                      -> 6 rollout/cleanup
```

Parallelizable work after contracts stabilize:

- 2.7 provider adapter tests can run in parallel by provider.
- 3.1 scheduler tests can start after Phase 1 domain contracts are fixed while Phase 2 provider work continues.
- 5.1 shared frontend API types can start after Phase 4 DTO contracts are frozen.
- Deployment/monitoring docs can be prepared in parallel with frontend implementation.

Hard dependencies:

- `ContentAutoScheduler` must exist before automatic/human approve endpoints can be finalized.
- `content:approve` and `content:publish` must be seeded/granted before enforcing endpoints.
- Publishing is paused during legacy classification; the unconditional backstop is deployed before publication resumes.
- Review correctness depends on durable `content_review_tasks`, server-owned `content_assets` and tenant dispatch.
- Optional vision adapters require explicit config override/known-model capability registry and bounded asset reads.
- GIF sampling requires a vetted decoder library and resource caps.
- External publish safety depends on durable claim/attempt state and provider reconciliation strategy.

External/runtime dependencies:

- Reviewer Agent definition and valid LLM binding per tenant.
- A social publish target (Meta page for Facebook); when missing, the revision-bound schedule intent remains held and recoverable.
- Document storage readable from review worker host for vision-capable tenants.

## Timeline & Estimates
**When will things be done?**

Estimated engineering effort for one full-stack .NET/React developer, excluding unrelated dirty-branch conflict resolution:

| Phase | Estimate | Notes |
|---|---:|---|
| Phase 0 | 0.5â1 day | Baseline and RED test skeletons |
| Phase 1 | 2.5â3.5 days | Domain, durable task/asset/attempt entities, EF, migrations, cutover classification |
| Phase 2 | 4â5 days | Tenant worker, coordinator, providers, optional vision, GIF, audit/outbox |
| Phase 3 | 3â4 days | Schedule intent, publish claim/idempotency/reconciliation, tools/SLA |
| Phase 4 | 1.5â2 days | API, distributed/business limits, RBAC/seed compatibility |
| Phase 5 | 1.5â2 days | Shared control and workflow UX on two pages |
| Phase 6 | 2â3 days | Paused cutover, mixed-version prevention, metrics and rollback drill |
| **Total** | **15â21 days** | Add 25â35% buffer for provider/gateway and dirty-file conflicts |

Recommended delivery slices:

1. **Schema/safety build**: Phase 1 code and tests; do not enable mixed-version publication.
2. **Review build**: Phase 2 with durable tasks and forced human approval in staging.
3. **Publish-safety build**: Phase 3 claim/idempotency/reconciliation plus canonical API.
4. **UX build**: Phase 5.
5. **Coordinated cutover**: Phase 6 pauses publication, drains old binaries, classifies legacy and resumes only on the compatibility build; cleanup later.

## Risks & Mitigation
**What could go wrong?**

| Risk | Impact | Mitigation |
|---|---|---|
| Historical blanket `ApprovedByAgentId` is mistaken for real review | Unreviewed legacy content publishes | Never backfill new reviewed fields from it blindly; queue real review for unpublished rows; `legacy_exempt` only for already-published history |
| Edit/review race | Old review approves new content | Revision + rowversion + expectedRevision on every review/approve; stale result audit and discard |
| Duplicate schedule intent under concurrent workers | Duplicate internal work | Revision-aware unique intent + conditional insert/load winner |
| Provider accepts post but process/DB finalization fails | Blind retry creates duplicate external post | Durable publish attempt, immutable snapshot, stable idempotency key, `outcome_unknown` reconciliation; never blind retry |
| Reviewer binding missing/LLM outage | Queue accumulates, auto stops | Fail to human, bounded recovery, SLA alert, dashboard metric, pre-deploy binding check |
| Model lacks vision | Images are not reviewed | Explicit `skipped_unsupported` in DTO/UI/audit; text review still mandatory; capability metric; admin can choose a vision model later |
| Vision path asset/decode failure | False confidence or blocked posts | Never claim reviewed; route human; typed accepted-part completeness |
| Client-controlled/cross-tenant asset key | Data leak or SSRF/path traversal | Server-owned `content_assets`, namespaced key, tenant/item-scoped bounded reads, no external URL fetch |
| Poisoned reviewer memory/KB/image instructions | Automatic approval manipulation | Keep all tenant-derived material out of system prompt; delimit as untrusted data; adversarial tests |
| GIF decoder resource exhaustion | CPU/memory pressure | Byte/dimension/frame caps, even sampling, cancellation and timeout, vetted library |
| Auto scheduling lacks Meta target | Due intent cannot publish | Keep the revision-bound schedule intent held at persisted time, notify and retry bounded; do not erase approval or recreate intent |
| Policy change mass-publishes old queue | Unexpected external side effects | Snapshot at review completion; no retroactive scan; explicit warning in UI |
| Permission code mismatch (`content.approve` vs `content:approve`) | 403 or unauthorized approvals | Canonical colon code, exact seed tests, temporary legacy alias only |
| API/AgentService/Hangfire mixed versions | Old binary bypasses new invariants | Pause publication, SQL minimum-writer gate + outbound fence, drain/stop old binaries, resume only on compatibility build |
| Cost/latency rises from universal review | Slow queue and spend growth | One review per revision, coalesced edits, caps, ledger, metrics, recovery concurrency limit |
| Review starts on every keystroke/upload | Wasted calls and stale results | Generation reviews immediately; manual edits mark pending and use a short server-side quiet-period/coalescing worker before review |
| Existing dirty-file changes are overwritten | Lost unrelated work | Read exact current file before each edit, use small replacements, review changed files before commit |

## Resources Needed
**What do we need to succeed?**

- One .NET/React developer familiar with Domain/EF/Hangfire and the AgentService/API split.
- Reviewer-agent LLM configurations for at least one text-only and one vision-capable provider in staging.
- Existing SQL Server, Hangfire, document storage, Meta publishing, notification and audit infrastructure.
- A maintained image/GIF decoder dependency selected through package registry/security review.
- Test data: text-only post, PNG/JPEG/WebP, animated GIF, unreadable legacy asset, reviewer timeout, missing Meta page, concurrent edit/review and duplicate scheduler calls.
- Reference docs:
  - `docs/ai/requirements/2026-07-20-feature-content-publishing-approval-policy.md`
  - `docs/ai/design/2026-07-20-feature-content-publishing-approval-policy.md`
  - existing mandatory review-gate plan for reviewer separation/fail-closed conventions.
