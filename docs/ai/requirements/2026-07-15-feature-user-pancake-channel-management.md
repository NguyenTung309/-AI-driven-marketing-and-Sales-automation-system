---
phase: requirements
title: User Pancake Channel Management
feature: user-pancake-channel-management
date: 2026-07-15
status: reviewed
---

# User Pancake Channel Management — Requirements

## Problem Statement

The **Admin → Users** surface already shows every Pancake channel linked to a user, but the relationships are read-only and existing-channel editing assumes `pancakeChannels[0]` is the channel being edited. Entering another Page ID through the user form adds a new `InboxMember` relation instead of replacing the old one, so users can silently accumulate channels and operators cannot manage an exact channel relationship.

Affected users:

- Administrators managing users, inbox ownership, and Pancake credentials.
- Sales Leads who may update Pancake channel names/tokens but must not gain inbox-membership administration implicitly.

Current workaround:

- Navigate separately to `/system/channels`.
- Search for the channel manually.
- Use the broad owner update, which replaces all current members and therefore cannot safely unlink one exact `(inboxId, agentId)` relation.

## Goals & Objectives

### Primary goals

1. Show each user's Pancake channels independently with channel name, platform, Page ID, token status, and channel-level actions.
2. Give each channel projection a stable `inboxId` so mutations never target a mutable Page ID or array position.
3. Allow authorized operators to update a channel display name and submit a replacement token without exposing the saved token.
4. Allow an inbox administrator to change the single responsible user through the existing inbox-member update contract without affecting that user's relationships to other inboxes.
5. Allow an inbox administrator to unlink exactly the current `(inboxId, agentId)` relation while keeping the `Inbox` intact and allowing it to become unassigned.
6. Preserve tenant isolation, current RBAC boundaries, and existing create-user/add-channel behavior.

### Secondary goals

- Make the existing-user Pancake fields clearly represent **Add a new channel**, not edit the first existing channel.
- Refresh all affected Admin React Query caches after channel mutations.
- Reuse existing admin modal, form, status, alert, and confirmation components.

### Non-goals

- Delete or soft-delete an `Inbox`.
- Edit Page ID or platform for an existing inbox.
- Display, decrypt, or return a stored Pancake token.
- Clear a stored token; only replacement is included.
- Replace the existing `InboxMember` relation table with an owner column.
- Change the existing single-owner database invariant (`InboxId` has a unique index).
- Reassign conversations automatically to the replacement user.
- Redesign `/system/channels` or remove its legacy broad member-replacement endpoint.
- Add batch channel operations or a new frontend test framework solely for this feature.

## User Stories & Use Cases

### Inspect channels

As an authorized user or token manager, I want to see all channels linked to a user as separate rows so that I do not accidentally operate on only the first channel.

### Edit channel metadata

As an Admin or Sales Lead with `users:pancake-token:manage`, I want to rename a channel or replace its token so that the integration remains identifiable and authenticated.

### Transfer responsibility

As an Admin with `admin:inboxes`, I want to change a channel's single responsible user so that the former owner's matching conversations are unassigned and their ownership of other channels is unchanged.

### Unlink one relationship

As an Admin with `admin:inboxes`, I want to unlink one user from one channel so that the channel remains available but is no longer assigned to that user.

### Add another channel intentionally

As a token manager, I want the existing-user form to start with blank channel fields and clearly add a new channel so that it no longer appears to edit `pancakeChannels[0]`.

## Success Criteria

### Listing and identity

- `GET /api/admin/users` returns `inboxId`, `pageId`, `name`, `platform`, and `hasToken` for every active, same-tenant membership.
- Deleted or other-tenant inboxes are excluded.
- No plaintext or encrypted token value is serialized.
- Frontend channel rows use `inboxId` as their stable key and mutation target.
- Existing-channel management contains no `pancakeChannels[0]` assumption.

### Channel metadata update

- A caller with `users:pancake-token:manage` can update name only, token only, or both.
- Omitted fields stay unchanged; a name-only update preserves the token.
- Supplied values are trimmed and validated at the API boundary.
- The replacement token is encrypted before persistence and never returned or logged.
- Blank/empty effective updates and overlong values return `400`.
- Missing, deleted, or cross-tenant inboxes return `404` without leaking tenant existence.
- Success returns `204`.

### Change the responsible user

- Only callers with `admin:inboxes` can change responsibility.
- The existing `PUT /api/admin/inboxes/{inboxId}/members` contract is reused because the database enforces at most one owner per inbox.
- The replacement user must belong to the current tenant.
- If the selected user is already responsible, the operation is an idempotent `204` and does not unassign conversations.
- If another user is responsible, only that inbox's owner relation is replaced and only matching conversations for the former owner are unassigned.
- If the channel is currently unassigned, the selected user is added without affecting conversations.
- The former owner's relationships to other inboxes remain unchanged.
- A missing inbox returns `404`; an invalid replacement returns `400 agent_not_found`.

### Unlink one membership

- Only callers with `admin:inboxes` can unlink.
- Only the requested `(inboxId, agentId)` relation is removed.
- Other memberships remain unchanged.
- The inbox remains active and is not deleted or soft-deleted.
- Removing the final membership is allowed and leaves the channel unassigned.
- Missing inboxes or relations return `404`; success returns `204`.

### Conversation assignment safety

For transfer and unlink:

- Only conversations where both `InboxId == inboxId` and `AssignedTo == removedAgentId` are unassigned.
- Conversations in another inbox or assigned to another user remain unchanged.
- Conversations are not automatically reassigned to the replacement owner.
- Any real-time update uses the affected conversation's ID, not the inbox ID.

### Permissions and UX

- `admin.system` continues to govern user-account management.
- `users:pancake-token:manage` governs channel name/token updates.
- `admin:inboxes` governs owner options, transfer, and unlink.
- Sales Leads can edit name/token without triggering an unauthorized `/api/admin/users/simple` request.
- Users without `admin:inboxes` do not see transfer/unlink controls.
- Successful mutations invalidate the `['admin']` React Query prefix.
- The unlink confirmation explicitly states that the channel itself will not be deleted.

### Backward compatibility

- Create-user with an optional initial channel still works.
- Existing-user add-channel still adds a membership and does not remove prior memberships.
- The existing backend user-update channel-connect contract remains available.
- The new Admin Users flow does not call the destructive legacy `updateInboxMember` operation.

## Constraints & Assumptions

### Technical constraints

- `InboxMember` uses composite identity `(InboxId, AgentId)` and the EF model also enforces a unique index on `InboxId`, so each inbox has at most one responsible user while one user may own multiple inboxes. No schema migration is required.
- The frontend has no React unit-test or browser-E2E harness. Behavioral automation will focus on HTTP integration tests, with TypeScript/lint/build and manual browser verification for UI states.
- Existing broad Admin query invalidation should be reused instead of adding optimistic updates to a paginated, cross-user relationship list.
- The API must remain tenant-scoped even in Hangfire/non-HTTP contexts; this feature's endpoints execute in authenticated HTTP requests and must explicitly validate tenant-owned records.

### Business assumptions

- The existing data model and UX both enforce at most one responsible user per channel. Exact unlink still includes both IDs to prevent stale or incorrect deletion.
- Transfer/unlink unassign work; they do not silently move active conversations to another user.
- Page ID and platform identify the external channel and are immutable in this scope.

## Approaches Considered

1. **Extend the existing user update** — smallest apparent frontend change, but keeps treating a shared channel as a user property and cannot identify an exact existing relation safely.
2. **Extend the existing inbox metadata update and reuse member replacement** — fewer routes, but the existing inbox group is Admin-only and would block Sales Lead metadata/token management.
3. **Dedicated metadata endpoint, existing single-owner reassignment, and exact unlink endpoint** — keeps least-privilege metadata access, reuses the correct owner contract, and adds only the missing safe unlink operation.

**Recommendation:** approach 3.

## Questions & Open Items

All product-level decisions required for implementation are resolved for this scope. Confirmed on 2026-07-15:

- Feature name: `user-pancake-channel-management`.
- No inbox deletion or soft deletion.
- Owner transfer/unlink require `admin:inboxes`.
- Sales Leads with `users:pancake-token:manage` may edit both channel name and replacement token, but not memberships.
- Transfer/unlink unassign only conversations matching the affected inbox and removed user; they do not auto-assign the replacement user.
- The existing-user form keeps a clearly labeled, blank **Add a new channel** section; existing channels are edited in the focused channel modal.
- The existing single-owner database invariant is reused; no ownership schema change is introduced.
