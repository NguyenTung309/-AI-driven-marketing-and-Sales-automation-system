---
phase: testing
title: Testing Strategy
description: Define testing approach, test cases, and quality assurance
---

# Testing Strategy — `zalo-content-publishing`

## Test Coverage Goals

- Keep the native Zalo OA publish contract aligned with the documented asynchronous article flow: create returns a process token, verify returns the article ID.
- Target 100% branch coverage for the changed Zalo request sequencing, response validation, safe error mapping, tenant credential selection, and cancellation behavior.
- Prove that the process token is never returned as `PostUrl` or `ExternalId` and that no permalink is synthesized.
- Run the existing Graph publisher suite to guard Facebook and Instagram behavior.

## Unit Tests

### `GraphSocialPublisher` — Zalo OA

Test file: `tests/Clawbot.Infrastructure.Tests/Content/GraphSocialPublisherZaloTests.cs`

- [x] Uses `POST /v2.0/article/create` followed by `POST /v2.0/article/verify`.
- [x] Normalizes the legacy tenant base ending in `/oa` to the documented article API root.
- [x] Sends the OA access token only in the provider header and sends the opaque process token only in the verify body.
- [x] Returns the verified `data.id` as `ExternalId` and keeps `PostUrl` null.
- [x] Stops before verification when create omits `data.token`.
- [x] Fails when verify omits `data.id`.
- [x] Maps provider, HTTP, transport, and malformed-response failures to stable token-free error codes.
- [x] Uses a dedicated no-redirect Zalo client with bounded response buffering and fails closed when it is unavailable.
- [x] Classifies uncertain post-submission create/verify failures as `zalo_outcome_unknown:*` so the job does not blindly recreate an article.
- [x] Rejects malformed response/asset shapes and provider IDs that exceed the downstream persistence limit or contain secrets.
- [x] Prefers the tenant credential resolver over options fallback.
- [x] Propagates caller-requested cancellation.

Existing compatibility coverage: `tests/Clawbot.Infrastructure.Tests/Content/GraphSocialPublisherTests.cs`.

## Integration Tests

- `tests/Clawbot.Infrastructure.Tests/Jobs/ContentPublishJobTests.cs`
  - `RunAsync_persists_provider_external_id_when_no_public_post_url_exists` verifies the article ID is persisted while the schedule URL remains empty.
- No live Zalo credential is required; provider requests are exercised through deterministic `HttpMessageHandler` fakes.

## End-to-End Tests

- No automated live-provider E2E is added because it would require tenant-owned Zalo OA credentials and would create public content.
- Staging smoke test: publish one approved Zalo article with a public cover image, confirm a successful verified article ID, and confirm no process token is stored or displayed as a URL.

## Test Data

- Fake OA access tokens and process tokens contain explicit `secret` markers so leakage assertions are meaningful.
- Provider responses cover successful create/verify, missing token, missing ID, provider error, HTTP error, malformed JSON, transport failure, and cancellation.
- Tenant resolver fixtures use a distinct endpoint, OA ID, and token from options fallback.

## Test Reporting & Coverage

Focused command:

```bash
dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GraphSocialPublisherZaloTests|FullyQualifiedName~GraphSocialPublisherTests.PublishAsync_Zalo|FullyQualifiedName~ContentPublishJobTests.RunAsync_persists_provider_external_id_when_no_public_post_url_exists|FullyQualifiedName~ContentPublishJobTests.RunAsync_uncertain_result_after_claim_keeps_item_locked_for_reconciliation"
```

Regression command for adjacent native publishers:

```bash
dotnet test tests/Clawbot.Infrastructure.Tests/Clawbot.Infrastructure.Tests.csproj --filter "FullyQualifiedName~GraphSocialPublisherTests|FullyQualifiedName~GraphSocialPublisherZaloTests"
```

Current local results:

- Pre-change baseline: `GraphSocialPublisherTests` — 30 passed, 0 failed.
- Regression tests were authored before production edits.
- Strict focused Zalo/job build-and-test filter — 72 passed, 0 failed, 0 skipped (`6 s`).
- Strict Graph publisher build-and-test filter — 92 passed, 0 failed, 0 skipped (`495 ms`).
- Latest post-format `--no-build` reruns after three additional hardening cases — 75/75 focused (65 dedicated Zalo plus 10 adjacent publisher/job) and 95/95 Graph publisher regressions.
- Strict Infrastructure build — succeeded with 0 warnings and 0 errors (`6.24 s`).
- A later attempt to rebuild the latest test assembly was blocked by unrelated concurrent Meta/render compile edits. No warning suppression or repository test-configuration change is part of the final task; the latest compiled test assembly remains fully green for all task filters.

## Manual Testing

- Confirm the tenant Zalo credential remains masked in admin responses and encrypted at rest.
- Confirm a successful publish records the verified article ID, not the process token.
- Confirm the UI does not render a fabricated `zalo.me/p/{token}` link when `PostUrl` is empty.

## Performance Testing

- Not required for this repair. The provider flow adds exactly one documented verification request after creation.
- The dedicated Zalo client keeps the existing 15-second timeout/circuit-breaker policy while adding redirect rejection and bounded response buffering.

## Bug Tracking

- Treat any reintroduction of `/article/verify_only`, process-token persistence, synthesized Zalo permalink, or raw provider error/body persistence as a blocking regression.
- The task-owned source build and compiled task filters are green; later clean test rebuilds may still be intermittently blocked by unrelated concurrent Meta/render edits.
