---
phase: deployment
title: Deployment Strategy
description: Define deployment process, infrastructure, and release procedures
---

# Deployment Strategy — `content-publishing-approval-policy`

## Infrastructure
**Where will the application run?**

No new service is introduced. The feature uses existing:

- SQL Server for tenant/item/schedule/audit state.
- API host for HTTP endpoints and existing Hangfire registration.
- AgentService for content generation/review coordination.
- Document storage for uploaded content assets.
- Existing LLM provider bindings, cost ledger and social publishers.
- React frontend deployment.

Environment separation follows current local/staging/production conventions.

## Deployment Pipeline
**How do we deploy changes?**

### Build Process

Required gates before staging:

1. Focused .NET unit/integration suites.
2. Full solution build with NuGet audit/analyzers clean.
3. Frontend format/lint/incremental typecheck/build.
4. Migration/repair smoke tests on fresh and existing schema.
5. Playwright critical flows.
6. Security review for RBAC, asset loading and tenant/job scope.

### CI/CD Pipeline

Recommended staged artifacts:

- Schema-expand migration/repair bundle.
- Compatibility backend binary that can read legacy columns but enforces new invariants; deploy only after old binaries are drained, not concurrently for publication.
- Frontend using canonical policy API.
- Later cleanup release removing legacy fields/aliases.

Do not deploy cleanup in the same release as expansion.

## Environment Configuration
**What settings differ per environment?**

### Development

- Text-only stub reviewer by default; optional local vision-capable stub for mapping tests.
- Small review/recovery intervals for deterministic testing.
- Local document storage with known internal upload prefix.

### Staging

- At least one real text-only and one real vision-capable reviewer binding.
- Test Meta page or safe publisher sandbox.
- Metrics/logs/audit available before enabling automatic policy.
- Verify built-in grants exactly: Admin has read/write/approve/publish + system:config; Marketer has read/write/approve only; `content:publish` is admin-only and never inferred from legacy grants.

### Production

- Existing/new tenant policy defaults `human_required`.
- Do not bulk-enable `automatic` during migration.
- Review and schedule recovery concurrency/caps configured conservatively.
- `ContentWorkflowHealthJob`, persisted hourly rollups, numeric thresholds, owners and admin/system-error notification channels are active before publication resumes.

No new secrets are required beyond existing provider/social/storage credentials.

## Deployment Steps
**What's the release process?**

1. **Pre-deployment**
   - Recheck migration number/current file overlaps and build a rollback binary that already contains the unconditional backstop.
   - Back up database; inventory unpublished/scheduled content, old backfill effects, reviewer bindings and active worker versions.
   - Verify all tenants have a usable text reviewer; vision is optional.
2. **Schema expand + bridge release**
   - Apply additive policy/revision/task/asset/schedule/attempt schema plus `content_workflow_runtime_gate`.
   - Apply dependent indexes/constraints/triggers in later numbered migrations; update repair path.
   - Deploy a bridge binary to every API/AgentService/Hangfire instance that sets `SESSION_CONTEXT('clawbot_content_writer_version')` and respects `publication_paused`; keep minimum permissive until all live writers report a version.
3. **Begin cutover maintenance window**
   - Set `publication_paused=1`, raise `minimum_writer_version`; SQL triggers reject absent/lower writers.
   - Fence the actual outbound boundary: block social-provider egress at firewall/service mesh or temporarily revoke/deactivate publisher credentials.
   - Disable AdminJobs/manual Hangfire trigger endpoints; pause due job, HTTP retry and direct Agent publish/schedule.
   - Remove/supersede the blanket review backfill invocation; drain/stop old instances. Reads remain available.
4. **Classify legacy fail-closed**
   - Already-published rows → history-only `legacy_exempt`.
   - Every unpublished/scheduled row loses inherited Agent/human approval, receives a fresh review task and `human_approval_requirement_reason='migration_cutover'`.
   - Preserve desired time only as a held revision-bound intent; legacy NULL revision is never assumed revision 1.
5. **Deploy compatibility build**
   - Unconditional review/approval/scheduled-revision checks.
   - Durable tenant review tasks, server-owned assets, policy versioning, schedule intents, publish claims/attempts and transactional audit/outbox.
   - Canonical non-publishing tool/retry semantics and guarded outbound transports.
6. **Classify/drain urgent content while publication remains paused**
   - Prioritize due/scheduled rows; verify fresh review and required human approval.
7. **Smoke invariants before resume**
   - Automatic text pass, human-required approve, non-pass override, stale edit, user cancel, concurrent claim, outcome_unknown reconciliation, vision reviewed/skipped.
8. **Resume publication**
   - Confirm old processes/manual triggers cannot run, restore provider egress/credentials, release pause only for the new writer version and start tenant-dispatched workers.
9. **Deploy frontend**
   - Both `/content` and `/agents` use canonical endpoint/query key; default remains `human_required`.
10. **Observe and cleanup later**
   - Hold cleanup until queues/outcomes/metrics are stable and no old client remains; remove legacy fields/routes/tool aliases in a separate release.

## Database Migrations
**How do we handle schema changes?**

- Use one SQL command per migration file; no `GO`.
- Add the migration to fresh-database replay and existing-schema repair paths.
- Use additive columns/tables first: tenant policy value/version/time, item revision/review/approval, review tasks, lifecycle assets, mandatory schedule revision/status, publish attempts, runtime gate, `llm_configs.supports_vision`, audit event key/sequence and hourly workflow metrics.
- Because this runner executes one batch per file, create dependent indexes/constraints in a later numbered migration unconditionally. Intentionally replace/migrate existing `ix_content_schedule_pending_item` on singular `content_schedule`.
- Recheck current next number immediately before implementation; expected `0076` is provisional and more than one file is expected.
- Cutover classification is idempotent, but remove/supersede the old blanket review backfill and its invocation.
- Never set new review/approval fields from legacy `ApprovedByAgentId`/`ApprovedBy`; every unpublished row requires fresh decisions.

Backup:

- Full DB backup before schema/backfill.
- Snapshot counts by item lifecycle/review/approval state before and after.

Rollback constraints:

- Expanded schema remains; never drop columns/tables during emergency rollback.
- Roll back only to a prebuilt compatibility binary that understands new schema and keeps unconditional review/approval/claim checks.
- If that binary is unavailable, keep all publication entry points paused. Never deploy an old binary that trusts `ApprovedByAgentId` or calls provider directly.
- Backlog is handled by forced human-safe hold; never restore an unreviewed publish bypass.

## Secrets Management
**How do we handle sensitive data?**

- Reuse encrypted LLM/social credentials and existing secret management.
- Never store provider API keys in policy/review rows.
- Asset review uses internal storage keys, not signed/public URLs copied to logs.
- Audit/logs contain identifiers and short reasons only.
- No secret rotation is required unless deployment reveals an existing credential leak.

## Rollback Plan
**What if something goes wrong?**

Rollback triggers:

- Unreviewed/stale content can publish.
- Duplicate schedules/posts occur.
- Cross-tenant policy/asset access is observed.
- Review backlog threatens scheduled operations.
- Migration/repair corrupts state.

Rollback steps:

1. Before stopping/restarting anything: set `publication_paused=1`, disable AdminJobs/Hangfire/manual triggers, and block provider write egress or deactivate publisher credentials.
2. Stop affected binaries; preserve new state/audit/attempt rows.
3. Deploy only the prepared compatibility rollback build with unconditional backstop and SQL minimum-writer gate. If unavailable, remain paused and fenced.
4. Force human-safe hold for pending revisions; do not restore optional Agent review.
5. While outbound remains fenced, reconcile all `publishing|outcome_unknown` attempts and duplicate intents/external posts.
6. Verify rollback build writer version, claims, permissions and smoke checks; only then restore provider egress/credentials.
7. Start tenant-dispatched publisher and clear database publication pause last.
8. Communicate affected tenants and handle urgent content only through privileged claim/reconciliation.

A database restore is last resort and only after reconciling external posts that cannot be rolled back atomically.
