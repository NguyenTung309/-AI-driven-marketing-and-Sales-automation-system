---
phase: monitoring
title: Monitoring & Observability
description: Define monitoring strategy, metrics, alerts, and incident response
---

# Monitoring & Observability — `content-publishing-approval-policy`

## Key Metrics
**What do we need to track?**

### Performance Metrics

- Agent text review latency p50/p95/p99.
- Optional vision review latency and decoded bytes/frames.
- Review task pending/leased counts, oldest age and expired leases by tenant.
- Review retry/lease recovery success/failure.
- Approval-to-schedule-intent latency and held-intent age.
- Canonical schedule `status`: pending/held/publishing/outcome_unknown/posted/failed/canceled; `last_error_code=canceled_by_user` marks explicit cancel.
- Publish claim latency, transmitted duration, reconciliation age and attempts by provider.
- LLM input/output tokens and USD cost per review path.

### Business Metrics

- Content revisions reviewed per tenant/day.
- Review verdict distribution: passed/rejected/needs_human/failed.
- Image review distribution: reviewed/not_applicable/skipped_unsupported/failed.
- Publishing policy adoption: automatic vs human_required tenants.
- Automatic approval rate.
- Human fallback rate under automatic policy.
- Human override count/rate and reasons category.
- Human rejection count/rate.
- Auto-created schedules and optional manual reschedules.
- Successful published posts by approval mode.
- Policy changes by tenant/admin.

### Error Metrics

- Stale review results discarded.
- Stale human approval conflicts.
- Text reviewer timeout/provider/parse errors.
- Vision capability unsupported and vision-path load/decode/provider errors.
- GIF sampling failures/cap violations.
- Missing reviewer Agent/binding.
- Missing/inactive publish target.
- Duplicate schedule-intent conflicts and publish-claim conflicts.
- `outcome_unknown`, reconciliation failures and provider idempotency mismatches.
- Publish blocks by reason: Agent review, human approval, stale revision, active claim, final reject.
- Cross-tenant task/item/asset/policy/schedule guard violations.
- SQL minimum-writer gate/outbound-fence violations and duplicate audit event-key conflicts.

## Monitoring Tools
**What tools are we using?**

Use existing Clawbot infrastructure plus feature-specific durable health reporting:

- `System.Diagnostics.Metrics` meter `Clawbot.ContentWorkflow` for counters/histograms when an OTLP exporter is configured.
- A `ContentWorkflowHealthJob` every 5 minutes as the production-independent alert source: query tenant-scoped DB state, persist system/admin error logs and send admin notifications.
- Dedicated `content_workflow_metrics_hourly` rollups with unique tenant/hour rows and 180-day retention; no unbounded event table.
- Existing `audit_logs` for business decisions/config changes, with deterministic event keys.
- Hangfire dashboard for dispatcher/worker/reconciliation jobs.
- Existing notification system for tenant-facing workflow issues.

Deployment is not blocked on adding a new external observability vendor, but it is blocked until the health job, numeric thresholds and notification owners/channels are configured.

## Logging Strategy
**What do we log and how?**

Structured event names:

- `content.agent_review.started`
- `content.agent_review.completed`
- `content.agent_review.stale_result_discarded`
- `content.agent_review.vision_skipped_unsupported`
- `content.agent_review.vision_failed`
- `content.publishing_approval.automatic`
- `content.publishing_approval.human`
- `content.publishing_approval.override`
- `content.publishing_rejected`
- `content.publishing_policy.changed`
- `content.auto_schedule.created`
- `content.auto_schedule.held`
- `content.publish.claimed`
- `content.publish.external_accepted`
- `content.publish.outcome_unknown`
- `content.publish.reconciled`
- `content.publish.blocked`
- `content.publish.completed`

Common fields:

- tenantId, contentItemId, contentRevision, reviewedRevision, scheduledRevision;
- reviewerAgentId, generatorAgentId;
- reviewStatus, imageReviewStatus, reviewedImageCount;
- publishingPolicyApplied, publishingPolicyVersionApplied, approvalMode;
- scheduleId, publishAttemptId, attemptToken/idempotency hash (not raw secret), outcome state, platform, non-secret target ID;
- eventKey/stateSequence, durationMs, attemptCount, stable errorCode;
- actorUserId for human/config decisions.

Never log full body, AssetsJson, prompts/responses, image bytes, signed URLs/query strings, provider bodies, raw exception messages, credentials or access tokens. Short reasons are redacted, truncated and mapped to stable codes.

Retention follows existing system/audit log policy; do not create a separate unbounded review log table.

## Alerts & Notifications
**When and how do we get notified?**

### Critical Alerts

- Invariant-violating successful publish count > 0 in any 5-minute health run → pause publisher/automatic workflow immediately.
- Duplicate external post evidence for same tenant/schedule/revision > 0 → pause affected provider/tenant and reconcile.
- Cross-tenant task/item/asset/policy/schedule guard violation > 0 → security incident.
- SQL writer-version violation, disabled manual trigger use, or provider call while outbound fence is active > 0 → pause writes/publication.
- Migration/classification count mismatch > 0 for unpublished rows → halt rollout.

### Warning Alerts

- Oldest pending review task > 15 minutes; high severity > 60 minutes.
- Text reviewer failure rate > 20% over 15 minutes with at least 5 attempts.
- Held schedule intent remains unresolved > 15 minutes after desired time.
- `outcome_unknown` count > 0 or reconciliation age > 10 minutes.
- Missing reviewer binding for any active tenant with pending content.
- Missing publish target for any due/approved Facebook intent.
- Human fallback rate doubles baseline and exceeds 30% over one hour after provider/model change.
- Vision failure rate > 20% over one hour with at least 5 vision attempts; `skipped_unsupported` is informational unless config claims vision available.
- Review cost reaches 80% of tenant monthly cap or exceeds configured per-day anomaly threshold.

Health-job alerts use a 15-minute dedupe cooldown per `(tenant,errorCode)` and route critical events to the existing admin/system-error channel plus tenant admin notification; warning owner is the content/Agent operations owner.

Tenant-facing notifications:

- Item moved to human review with a short reason.
- Approved item has a held schedule intent (for example missing target).
- Review/schedule recovery exhausted or publish outcome requires reconciliation.
- Do not notify merely because vision is unsupported; show the status in UI unless the tenant explicitly expects vision capability.

## Dashboards
**What do we visualize?**

Operational dashboard:

- Review queue by state/age/tenant.
- Reviewer success/failure/latency/cost.
- Image review status and provider capability.
- Schedule intents by canonical pending/held/canceled/publishing/outcome_unknown state.
- Publish claims, reconciliation age, blocks and privileged retries.

Product dashboard:

- Policy adoption.
- Automatic approval and human fallback rates.
- Human override/reject rates.
- Time from content creation → review → approval → schedule → publish.
- Manual reschedule rate after golden-hour selection.

Admin content detail should expose a revision timeline with review, approval and scheduling timestamps without full sensitive prompt data.

## Incident Response
**How do we handle issues?**

### On-Call Rotation

Use the existing application on-call/escalation process. Assign ownership by subsystem:

- Domain/API/RBAC: API owner.
- Review/provider/vision: AgentService owner.
- Scheduler/publisher/Meta: Infrastructure integration owner.
- UI state mismatch: frontend owner.

### Incident Process

1. Detect and classify: invariant/security, duplication, backlog, provider, scheduling or UI-only.
2. Contain:
   - force effective human-required if automatic routing is unsafe;
   - pause recovery/scheduler if duplicating work;
   - keep publish backstop active.
3. Query by tenant/item/revision across content item, schedule, audit and logs.
4. Verify whether external publish occurred before changing database state.
5. Resolve the root cause and add a regression test.
6. Reconcile held/duplicate schedule intents and `outcome_unknown` publish attempts explicitly.
7. Document timeline, affected tenants and prevention changes.

## Health Checks
**How do we verify system health?**

Automated checks:

- API policy endpoint authorization/readiness.
- Database schema/version, SQL minimum writer gate and required task/asset/schedule/attempt indexes.
- Reviewer Agent definition/binding availability per tenant.
- Text review synthetic test with stub or controlled provider.
- Optional vision capability mapping test; absence is not unhealthy by itself.
- Tenant/item-scoped bounded storage read for a known server-owned asset; cross-tenant negative probe fails.
- Tenant dispatcher/Hangfire registration and last successful review/SLA/publish/reconciliation jobs.
- Default Meta target availability for tenants using Facebook automation.
- Reconciliation: mandatory schedule revision matches current reviewed/approved revision; no expired claims without attempt state; every outcome_unknown is tracked.

Post-deploy smoke:

- One automatic text-only item.
- One human-required item.
- One non-pass override.
- One edit while review is running (stale result discarded).
- One image post on vision-capable binding.
- One image post on text-only binding showing `skipped_unsupported`.
- One GIF frame-sampling review.
- One user-canceled schedule that recovery does not recreate.
- One edit-versus-publish-claim race.
- One simulated transmitted timeout reconciled from `outcome_unknown` without duplicate post.
