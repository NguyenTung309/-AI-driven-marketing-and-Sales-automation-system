# Channel Selection & Token Management Design

## Context
Sales agents need to see conversations grouped by Zalo/Facebook channel, not interleaved. Admins need to manage page_access_token for each channel. Approved via brainstorming 2026-06-22.

## Motivation
- Current Agent Hub shows all conversations from all channels in one flat list
- No way to associate a channel with its Pancake token
- Sales must know which channel a customer belongs to at a glance

## Design

### 1. Channel Selection Screen (/inbox)
- Landing page before Agent Hub
- Card grid shows channels user has access to (via InboxMembers)
- **Sale:** only sees assigned channels with unread badge
- **Admin:** sees all channels with "Admin — xem tat ca" badge
- Empty state: "Ban chua duoc gan kenh nao. Lien he admin."
- Click card → navigate to /inbox/{channelId}

### 2. Agent Hub Per-Channel (/inbox/{channelId})
- Reuses existing 3-panel Agent Hub layout
- List panel scoped to InboxId = {channelId}
- Header shows channel name + platform icon
- Back button to /inbox
- Chat area, composer, side drawer unchanged

### 3. Token Management (Admin UI)
- Channel Management page enhanced with token field
- Inbox entity gets EncryptedAccessToken property
- Create/edit channel form includes token input (password type)
- Encrypted at rest, decrypted only for outbound message sending
- Token status indicator in channel list (green/red dot)

### 4. API Changes

| Endpoint | Method | Purpose |
|---|---|---|
| GET /api/inbox/channels | GET | List channels user can access + unread count |
| GET /api/inbox/channels/{id}/conversations | GET | Conversations scoped to channel |
| POST /api/admin/inboxes | POST | Add pageAccessToken field |
| PUT /api/admin/inboxes/{id} | PUT | Update channel + token |

### 5. Flow

Login → /inbox → click channel → /inbox/{channelId} → 3-panel Agent Hub (only that channel's conversations)

### 6. Files Changed

**New:**
- src/frontend/.../features/inbox/ChannelListPage.tsx
- src/frontend/.../features/inbox/ChannelCard.tsx
- deploy/migrations/0030_add_inbox_encrypted_token.sql

**Modified:**
- Inbox.cs — add EncryptedAccessToken property
- InboxEndpoints.cs — add /channels endpoint
- AdminInboxEndpoints.cs — accept pageAccessToken
- ChannelsEndpoints.cs — create/update channel with token
- ChannelManagementPage.tsx — token input field
- AgentHubLayout.tsx — accept channelId, filter by InboxId
- shared/api/admin.ts, inbox.ts — API functions
- routes.tsx, lazyPages.tsx — /inbox, /inbox/:channelId

### 7. Security
- Token encrypted at rest using IEncryptor
- Only admin can view/edit token
- Token never returned in non-admin API responses
- Decrypted only at send-outbound time

### 8. Phasing
- **Phase 1:** Backend — Inbox.EncryptedAccessToken, /channels endpoint, filter by InboxId
- **Phase 2:** Frontend — ChannelListPage, route, AgentHub scoping
- **Phase 3:** Admin UI — token field in channel form
