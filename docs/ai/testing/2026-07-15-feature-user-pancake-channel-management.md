---
phase: testing
title: User Pancake Channel Management Testing Strategy
feature: user-pancake-channel-management
date: 2026-07-15
status: planned
---

# User Pancake Channel Management — Testing Strategy

## Test Coverage Goals

- Target 100% of new endpoint validation and mutation branches where practical; preserve project minimum 80% coverage.
- Use HTTP integration tests for route mapping, auth, tenant scoping, JSON contracts, EF persistence, encryption, and status codes.
- Use existing API/domain tests only as fast supporting coverage.
- Do not add a React test framework solely for this feature; use TypeScript, ESLint, build, and runtime browser verification.
- Sources:
  - `docs/ai/requirements/2026-07-15-feature-user-pancake-channel-management.md`
  - `docs/ai/design/2026-07-15-feature-user-pancake-channel-management.md`

## Unit Tests

### Existing single-owner relation

- [ ] Preserve `InboxMember` construction and composite identity tests.
- [ ] Preserve the EF unique `InboxId` invariant through integration setup; do not seed two members for one inbox.
- [ ] Add fast persistence coverage only if it provides signal beyond HTTP tests.

### Validation helpers

If endpoint validation is extracted cleanly:

- [ ] Reject no effective metadata change.
- [ ] Reject blank name/token.
- [ ] Reject overlong name.
- [ ] Preserve omitted metadata values.

Do not extract production helpers only to manufacture unit tests.

## Integration Tests

Primary new file:

- `tests/Clawbot.Integration.Tests/UserPancakeChannelManagementTests.cs`

Use `ClawbotWebApplicationFactory`, SQL Server/Testcontainers, fake auth, and existing JSON/database helpers. Extend auth configuration only enough to express Admin, token-manager, and unauthorized principals.

### Admin user projection

- [ ] A user owning multiple inboxes receives every channel.
- [ ] Each channel includes `inboxId`, name, platform, Page ID, and `hasToken`.
- [ ] Deleted and other-tenant inboxes are excluded.
- [ ] Raw/encrypted token text and token properties are absent from JSON.

### Metadata PATCH

- [ ] Token manager updates name only; encrypted token is unchanged.
- [ ] Token manager replaces token only; persisted encrypted value changes and response is `204`.
- [ ] Token manager updates both fields.
- [ ] Missing permission returns `403`.
- [ ] Missing/deleted/cross-tenant inbox returns `404` without mutation.
- [ ] Empty effective request returns `400 channel_update_required`.
- [ ] Blank name returns `400 channel_name_required`.
- [ ] Overlong name returns `400 channel_name_too_long`.
- [ ] Blank/invalid token returns `400 page_access_token_invalid`.
- [ ] No raw token appears in response content or test output.

### Existing owner PUT hardening

Seed Inbox A owned by User 1 and Inbox B owned by User 1. Seed:

- Conversation A1: Inbox A, assigned User 1.
- Conversation A2: Inbox A, assigned User 2.
- Conversation B1: Inbox B, assigned User 1.

Cases:

- [ ] Assigning an unowned inbox adds the selected owner.
- [ ] Changing Inbox A to User 2 replaces the single owner relation.
- [ ] User 1 remains owner of Inbox B.
- [ ] A1 becomes unassigned; A2 and B1 remain assigned.
- [ ] Selecting the current owner returns `204` without unassigning A1.
- [ ] Missing/cross-tenant replacement returns `400 agent_not_found` and preserves state.
- [ ] Missing/cross-tenant inbox returns `404`.
- [ ] Token-manager-only principal receives `403`; Admin succeeds.
- [ ] Captured notifier events, where fixture support exists, use affected conversation IDs rather than inbox ID.

### Exact owner unlink

Using the same two-inbox shape:

- [ ] DELETE Inbox A/User 1 returns `204`.
- [ ] Inbox A has zero members and remains active.
- [ ] Inbox B/User 1 remains.
- [ ] A1 becomes unassigned; A2 and B1 remain assigned.
- [ ] Wrong/stale agent ID returns `404` and preserves the actual owner.
- [ ] Missing relation and repeated delete return `404`.
- [ ] Cross-tenant route IDs cannot mutate data.
- [ ] Token-manager-only principal receives `403`; Admin succeeds.
- [ ] Captured notifier events use affected conversation IDs.

### Backward compatibility

- [ ] Create-user with initial channel still creates the inbox/membership.
- [ ] Existing-user explicit add-channel adds another inbox owned by that user.
- [ ] Adding a second channel does not remove the first.
- [ ] Legacy Channel Management owner PUT continues to work with the hardened semantics.

### Authorization matrix

| Principal | View projection | Edit name/token | Load owner options | Change owner | Exact unlink |
|---|---:|---:|---:|---:|---:|
| Admin | yes | yes | yes | yes | yes |
| Sales Lead token manager | yes | yes | no | no | no |
| Authenticated without permissions | no | no | no | no | no |
| Anonymous | no | no | no | no | no |

Every new route gets a success and `403` assertion. Cross-tenant tests are mandatory IDOR controls.

## End-to-End Tests

No automated browser harness exists. Validate with the project run/verify workflow:

- [ ] User with multiple channels shows every channel independently.
- [ ] Admin renames a channel; shared name refreshes wherever the channel appears.
- [ ] Admin replaces token; no token value appears in network responses.
- [ ] Sales Lead edits name/token without requesting `/api/admin/users/simple`.
- [ ] Admin changes owner; the channel moves to the selected user after Admin cache refresh.
- [ ] Selecting the same owner does not disturb conversation assignments.
- [ ] Admin exact-unlinks the current owner; the inbox remains visible in `/system/channels` as unassigned.
- [ ] A stale unlink for the previous owner fails safely after ownership changed.
- [ ] Existing-user form starts with blank **Add a new channel** fields and adds another channel intentionally.
- [ ] Matching conversations become unassigned; unrelated conversations remain unchanged.

## Test Data

- Generate unique tenant/user/inbox/conversation IDs per test.
- Respect the unique index: at most one `InboxMember` per inbox.
- Use at least two inboxes owned by the same user to prove operations do not affect other channels.
- Seed same-tenant and other-tenant controls for all ID-based mutations.
- Seed a recognizable fake token and assert it never appears in serialized output.
- Scope assertions to generated IDs; do not rely on global database emptiness.

## Test Reporting & Coverage

### Targeted RED/GREEN

```powershell
dotnet test "tests/Clawbot.Integration.Tests/Clawbot.Integration.Tests.csproj" --filter "FullyQualifiedName~UserPancakeChannelManagementTests"
```

### Supporting and CI checks

```powershell
dotnet test "tests/Clawbot.Api.Tests/Clawbot.Api.Tests.csproj" --filter "FullyQualifiedName~AdminInboxEndpointsTests"
dotnet restore "Clawbot.sln"
dotnet build "Clawbot.sln" --no-restore --configuration Release
dotnet test "Clawbot.sln" --no-build --configuration Release --filter "FullyQualifiedName!~Integration" --collect:"XPlat Code Coverage" --results-directory ./TestResults
dotnet format "Clawbot.sln" --verify-no-changes
npm --prefix "src/frontend/clawbot-web" run lint
npm --prefix "src/frontend/clawbot-web" run build
./deploy/ci/verify-testcontainers.ps1 -RunIntegrationTests
```

Record exact commands, results, skipped tests, coverage gaps, Docker/environment blockers, and manual verification evidence.

## Manual Testing

### UI/accessibility

- [ ] Modal labels identify the selected user and channel.
- [ ] Page ID/platform are clearly read-only.
- [ ] Copy explains that name/token changes affect the shared channel.
- [ ] Owner-change/unlink copy explains matching conversation unassignment.
- [ ] Unlink copy states that the channel is not deleted.
- [ ] Escape, cancel, close, submit, and confirmation work.
- [ ] Pending states prevent duplicate submissions.
- [ ] Replacement-token input resets on success, close, and channel switch.
- [ ] Keyboard/focus behavior remains usable.
- [ ] No overflow at 320, 375, 768, 1024, and 1440 widths.

### Security inspection

- [ ] Responses expose `hasToken` only.
- [ ] Sales Lead does not call Admin-only owner APIs.
- [ ] Cross-tenant IDs return generic not-found behavior.
- [ ] Raw token is absent from logs and notices.

## Performance Testing

No load harness is required for bounded Admin mutations.

- [ ] User listing remains batched without N+1 queries.
- [ ] Owner/unlink queries are scoped to one inbox and matching assignments.
- [ ] Owner options load only for an open Admin modal.
- [ ] Broad `['admin']` invalidation does not create a refetch loop.

## Bug Tracking

- **CRITICAL:** cross-tenant mutation, auth bypass, token leakage, wrong-owner deletion, or inbox deletion.
- **HIGH:** incorrect conversation unassignment, same-owner destructive behavior, wrong realtime conversation ID, stale UI, or add-channel regression.
- **MEDIUM:** validation inconsistency, permission UX, or accessibility regression.
- **LOW:** copy/spacing polish.

Every CRITICAL/HIGH issue blocks completion and receives a regression test before the fix where practical.
