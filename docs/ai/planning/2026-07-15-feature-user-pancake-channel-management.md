---
phase: planning
title: User Pancake Channel Management Plan
feature: user-pancake-channel-management
date: 2026-07-15
status: implementation-ready
---

# User Pancake Channel Management — Implementation Plan

## Milestones

- [x] **M1 — Backend contracts protected:** stable `inboxId`, metadata PATCH, hardened owner PUT, and exact unlink DELETE pass HTTP integration tests.
- [x] **M2 — Admin Users UI complete:** every channel renders independently with permission-aware metadata, owner, and unlink flows.
- [ ] **M3 — Regression/runtime verification complete:** add-channel behavior, tenant/RBAC, conversation scope, token secrecy, build, review, and security checks pass.

## Task Breakdown

### Phase 0: Requirements/design review and schema correction

Estimated effort: **done**

- [x] Review requirements and design.
- [x] Confirm metadata uses `users:pancake-token:manage`.
- [x] Confirm owner change/unlink use `admin:inboxes`.
- [x] Confirm matching conversations are unassigned, not transferred automatically.
- [x] Confirm existing-user form keeps blank **Add a new channel** inputs.
- [x] Verify current EF model: composite relation identity plus unique `InboxId`, so each inbox has at most one responsible user.
- [x] Correct the design to reuse existing owner PUT and add only the missing exact unlink contract.

### Phase 1: Stable channel projection — RED/GREEN/REFACTOR

Estimated effort: **0.25–0.5 day**

Critical files:

- `src/api/Clawbot.Api/Endpoints/AdminUsersEndpoints.cs`
- `src/frontend/clawbot-web/src/shared/api/admin.ts`
- `tests/Clawbot.Integration.Tests/UserPancakeChannelManagementTests.cs` (new)

#### RED

- [x] Add an HTTP integration test with users who own multiple inboxes plus deleted/other-tenant controls.
- [x] Assert every active same-tenant channel contains `inboxId`, name, platform, Page ID, and `hasToken`.
- [x] Assert deleted/cross-tenant channels and raw/encrypted token data are absent.
- [x] Run the filtered test and record the expected missing-`inboxId` failure (`KeyNotFoundException`).

#### GREEN

- [x] Add `InboxId = i.Id` to the backend projection.
- [x] Add required `inboxId` to `PancakeChannelInfo`.

#### REFACTOR

- [x] Keep the existing bounded batched query; do not introduce per-user lookups.
- [x] Re-run the targeted test in Release configuration; it passed.
- [x] Debug output was locked by running `Clawbot.Api` PID 2352, so verification used the unlocked Release output without stopping that process.

### Phase 2: Channel metadata PATCH — RED/GREEN/REFACTOR *(done)*

Estimated effort: **0.5 day**

Critical files:

- `src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs`
- `tests/Clawbot.Integration.Tests/UserPancakeChannelManagementTests.cs`

#### RED

- [x] Test name-only update preserves the encrypted token.
- [x] Test token-only replacement changes encrypted storage and returns no secret data.
- [x] Test token-manager success and missing-permission `403`.
- [x] Test cross-tenant inbox `404`.
- [x] Test empty request and blank values as `400` with stable codes.
- [x] Confirm route-not-found RED result: all four targeted tests initially returned `404`.
- [ ] Add explicit deleted-inbox, overlong-name, and oversized-encrypted-token cases during final coverage pass.

#### GREEN

- [x] Add rate-limited `PATCH /api/admin/pancake-channels/{inboxId}` requiring `users:pancake-token:manage`.
- [x] Load only active current-tenant inboxes.
- [x] Validate/trim inputs, update name, encrypt replacement token, and return `204`.
- [x] Never return or log token material.

#### REFACTOR

- [x] Reuse existing domain/encryption methods and nearby error conventions.
- [x] Keep validation local because no clean shared abstraction was needed.
- [x] All four targeted Release integration tests pass.

### Phase 3: Owner assignment hardening and exact unlink — RED/GREEN/REFACTOR *(done)*

Estimated effort: **0.5–0.75 day**

Critical files:

- `src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs`
- `tests/Clawbot.Integration.Tests/UserPancakeChannelManagementTests.cs`

#### RED — existing owner PUT

- [x] Test changing owner replaces the single relation.
- [x] Test same-owner PUT is a no-op and does not unassign conversations.
- [x] Test changing owner unassigns only conversations matching the inbox and former owner.
- [x] Test the former owner's membership in another inbox remains.
- [ ] Add explicit unowned-inbox and cross-tenant replacement cases during final coverage pass.
- [x] Verify implementation emits actual conversation IDs rather than inbox ID.

#### GREEN — existing owner PUT

- [x] Short-circuit when requested owner already matches.
- [x] Preserve single-owner replacement behavior.
- [x] Scope unassignment to affected inbox/former owner.
- [x] Replace the incorrect inbox-ID notification loop with one event per affected conversation using current conversation fields.

#### RED — exact unlink DELETE

Seed Inbox A owned by User 1 and Inbox B owned by User 1, plus conversations that distinguish inbox and assignee scope.

- [x] DELETE Inbox A/User 1 returns `204` and removes only that relation.
- [x] Inbox B/User 1 remains.
- [x] Inbox A remains active with zero members.
- [x] Only Inbox A conversations assigned to User 1 become unassigned.
- [x] Same-inbox conversations assigned to another user and other-inbox conversations remain unchanged.
- [x] Wrong/stale agent ID returns `404` and preserves the actual owner.
- [x] Token-manager-only principal receives `403`; Admin succeeds.
- [ ] Add explicit repeated-delete and cross-tenant DELETE cases during final coverage pass.

#### GREEN — exact unlink DELETE

- [x] Add `DELETE /api/admin/inboxes/{inboxId}/members/{agentId}` under `admin:inboxes`.
- [x] Query the active tenant inbox and exact relation.
- [x] Unassign matching conversations, remove the relation, save, and allow zero members.
- [x] Emit post-save updates with actual conversation IDs.

#### REFACTOR

- [x] Keep notification code local; the two short loops did not justify an abstraction.
- [x] Keep the legacy nullable-owner branch for compatibility, but do not call it from the new UI.
- [x] Fix test isolation by assigning projection channels to a dedicated test user.
- [x] Owner/unlink tests pass 4/4; full backend feature suite passes 9/9 in Release.

### Phase 4: Frontend API contracts and channel list *(done)*

Estimated effort: **0.5 day**

Critical files:

- `src/frontend/clawbot-web/src/shared/api/admin.ts`
- `src/frontend/clawbot-web/src/features/admin/AdminUsersTab.tsx`
- `src/frontend/clawbot-web/src/features/admin/AdminConsolePage.tsx`

No frontend test runner exists; do not add one solely for this feature.

- [x] Add typed `updatePancakeChannel` and `unlinkInboxMember` functions.
- [x] Reuse `updateInboxMember` for owner changes.
- [x] Keep token values request-only.
- [x] Render channel name, platform, Page ID, and token status for every channel.
- [x] Key by `inboxId` and pass source user/channel to callbacks.
- [x] Gate metadata actions with token permission and owner/unlink actions with inbox permission.
- [x] Keep legacy `updateInboxMember` usage in Channel Management working.

### Phase 5: Focused channel modal and add-channel regression *(done for UI; backend regression coverage remains)*

Estimated effort: **0.5–0.75 day**

Critical files:

- `src/frontend/clawbot-web/src/features/admin/AdminPancakeChannelModal.tsx` (new)
- `src/frontend/clawbot-web/src/features/admin/AdminConsolePage.tsx`
- `src/frontend/clawbot-web/src/features/admin/AdminUserModal.tsx`

- [x] Build a focused modal with existing shared UI/form helpers.
- [x] Keep metadata save and owner change as separate actions.
- [x] Load simple users only when modal is open and `canManageInboxOwners` is true.
- [x] Add exact unlink confirmation explaining that the inbox remains and matching conversations are unassigned.
- [x] Reset token input and disable duplicate submissions.
- [x] Invalidate `['admin']` after successful metadata, owner, or unlink mutation.
- [x] Remove `pancakeChannels[0]` seeding from existing-user edit.
- [x] Keep blank, explicit **Add a new channel** fields and submit only when Page ID is entered.
- [ ] Add backend regression tests for create-user initial channel and existing-user second-channel addition.

### Phase 6: Full verification and reviews *(in progress)*

Estimated effort: **0.5–1 day**

- [ ] Run targeted integration tests throughout RED/GREEN cycles.
- [ ] Run existing Admin Inbox/permission tests.
- [ ] Run solution build and non-integration suite with coverage.
- [ ] Run Docker/Testcontainers integration verification when available.
- [x] Run frontend lint and production build. Build passed; ESLint reported 0 errors and 3 unrelated pre-existing hook warnings.
- [ ] Launch and exercise Admin and Sales Lead flows via `/verify`.
- [ ] Inspect network/log output for token secrecy and forbidden owner-option calls.
- [ ] Verify owner change/unlink conversation scope and channel persistence.
- [ ] Run `code-reviewer` and `security-reviewer`; fix all CRITICAL/HIGH findings.
- [ ] Run `/check-implementation` and update implementation/testing docs with evidence.

## Dependencies and Order

1. Projection tests/contract.
2. Metadata endpoint tests/implementation.
3. Existing owner PUT tests/hardening.
4. Exact unlink tests/implementation.
5. Frontend API/types/list.
6. Focused modal and add-channel regression.
7. Full build, integration, runtime verification, and reviews.

No new npm/NuGet dependency or database migration is planned.

## Estimates

| Phase | Estimate |
|---|---:|
| Projection | 0.25–0.5 day |
| Metadata endpoint | 0.5 day |
| Owner hardening + unlink | 0.5–0.75 day |
| Frontend API/list | 0.5 day |
| Modal/regression | 0.5–0.75 day |
| Verification/reviews | 0.5–1 day |
| **Total** | **2.75–4 days** |

## Risks & Mitigation

### Same-owner reassignment regression

Risk: Current remove/re-add behavior unassigns conversations even when the selected owner did not change.

Mitigation: Add a RED test and short-circuit identical owner before mutation.

### Incorrect real-time identity

Risk: Current owner PUT emits the inbox ID where clients expect a conversation ID.

Mitigation: Notify once per affected conversation using its persisted state after save.

### Stale unlink request

Risk: An operator opens User A, ownership changes to User B elsewhere, then the old modal unlinks the channel.

Mitigation: DELETE includes both inbox and source agent; mismatched current relation returns `404` and preserves User B.

### Token leakage

Mitigation: write-only request field, encryption before persistence, `204` response, no secret logging, reset frontend input.

### Permission coupling

Mitigation: separate metadata and owner permissions; enable owner-options query only for `admin:inboxes`.

### Missing frontend behavioral harness

Mitigation: HTTP integration coverage, TypeScript/lint/build gates, explicit manual permission/accessibility matrix, and runtime verification.

## Verification Commands

```powershell
dotnet test "tests/Clawbot.Integration.Tests/Clawbot.Integration.Tests.csproj" --filter "FullyQualifiedName~UserPancakeChannelManagementTests"
dotnet test "tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj" --filter "FullyQualifiedName~AdminInboxEndpointsTests"
dotnet restore "Clawbot.sln"
dotnet build "Clawbot.sln" --no-restore --configuration Release
dotnet test "Clawbot.sln" --no-build --configuration Release --filter "FullyQualifiedName!~Integration" --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet format "Clawbot.sln" --verify-no-changes
./deploy/ci/verify-testcontainers.ps1 -RunIntegrationTests
npm --prefix "src/frontend/clawbot-web" run lint
npm --prefix "src/frontend/clawbot-web" run build
```

## References

- `docs/ai/requirements/2026-07-15-feature-user-pancake-channel-management.md`
- `docs/ai/design/2026-07-15-feature-user-pancake-channel-management.md`
- `docs/ai/testing/2026-07-15-feature-user-pancake-channel-management.md`
- `src/shared/Clawbot.Infrastructure/Persistence/AppDbContext.cs`
- `src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs`
- `src/api/Clawbot.Api/Endpoints/AdminUsersEndpoints.cs`
