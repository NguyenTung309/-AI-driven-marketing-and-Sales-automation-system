# Agent Hub Redesign: Sale Agent Command Center

> **Design doc** for the Agent Hub layout, per-sale conversation isolation, AI copilot, and agent tooling improvements.
> Based on Chatwoot analysis and Clawbot existing architecture.

**Date:** 2026-06-21
**Status:** Draft
**Spec location:** `.sdd/specs/15-sale-chat-multi-channel/SPEC.md` (Sections 7-11)
**Plan location:** `.sdd/specs/15-sale-chat-multi-channel/PLAN.md` (Phases 6-9)

---

## 1. Motivation

Clawbot's current inbox is a 3-column fixed layout (list | chat | context) where:

1. **Sale sees all conversations** -- no per-sale filtering, no privacy
2. **Single conversation at a time** -- no tabs, switching loses context
3. **AI draft in separate panel** -- copy-paste workflow, not inline
4. **No quick actions** -- resolve/assign/label require multiple clicks
5. **No customer timeline** -- context panel is static, no notes/labels

These gaps slow down sale agents who manage many leads simultaneously.

---

## 2. Design Principles

1. **Speed over features** -- every interaction should be 1-2 clicks or a keystroke
2. **AI as copilot, not pilot** -- suggest, don't auto-send. Sale always reviews
3. **Context without clutter** -- side drawer slides, not a permanent column
4. **Privacy by default** -- sale sees only their assigned inboxes. Admin sees all
5. **Keyboard-first** -- Ctrl+K for everything, / for commands in composer

---

## 3. Architecture Overview

### 3.1 Permission Model

```
JWT Claims: sub (userId), role_id, tenant_id, tenant_slug
         |
         v
IPermissionResolver -> IReadOnlySet<string> (permission codes)
         |
         +-- "admin:inboxes" -> ListAsync: NO filter -> show all conversations
         +-- else            -> ListAsync: filter by InboxMembers
```

Existing infrastructure reused:
- `IPermissionResolver` -- already resolves role permissions at runtime
- `InboxMembers` table -- already defined in Section 1
- `Conversation.AssignedTo` -- already exists

New permission code: `admin:inboxes` (seed for admin role, not for sale role)

### 3.2 Realtime Model

```
SignalR Hub:
  - Tenant group "tenant:{id}" -> admin sees everything
  - Inbox groups "inbox:{id}" -> members of that inbox
  - User group "user:{id}" -> personal notifications
```

Notifier sends events to all 3 groups (deduped client-side).

### 3.3 Frontend Component Tree

```
AgentHubLayout
  +-- ConversationList (left panel)
  +-- RightPanel
  |     +-- ConversationTabs
  |     +-- TabConversation (x N tabs)
  |     |     +-- ChatMessageThread
  |     |     +-- QuickActionBar
  |     |     +-- ComposerWithAI
  +-- SideDrawer (conditional)
  |     +-- CustomerTimeline
  |     +-- NotesSection
  +-- CommandPalette (modal overlay)
```

---

## 4. Component Specifications

### 4.1 AgentHubLayout

- **State**: `openTabs: ConvTab[]`, `activeTabId: string | null`, `drawerOpen: boolean`, `drawerConvId: string | null`
- **Grid**: `grid-cols-[280px_1fr]` (list 280px, flexible chat area)
- When a conversation is selected from list:
  - If already in tabs -> switch to it
  - If tabs < 7 -> add new tab
  - If tabs >= 7 -> show dropdown overflow
- Command palette registered at this level via `useEffect` keyboard listener

### 4.2 ConversationTabs

- Horizontal scroll container, overflow-x-auto
- Each tab: platform icon (16px) + customer name (truncated 120px) + unread badge (if > 0) + close button
- Active tab: `border-b-2 border-primary bg-surface-container`
- Close tab: remove from tabs array, if activeTabId was closed -> switch to nearest sibling or null

### 4.3 CommandPalette

- **Keyboard**: Ctrl+K / Cmd+K to open, Esc to close
- **Search scope**: 
  - Conversation search (name/phone/id) -> navigate to that conversation
  - Action search (/, /resolve, /assign @me, /label urgent, /snooze 1h, /note, /summarize) -> execute action
  - AI search (/draft reply, /suggest upsell) -> trigger AI endpoint
- Loading state: spinner while searching
- Empty state: "No results found"
- Error state: toast notification + keep palette open

### 4.4 QuickActionBar

| Button | Icon | Click behavior |
|---|---|---|
| Resolve | check_circle | Confirm dialog -> PUT resolve + optional note |
| Escalate | warning | Confirm dialog -> PUT escalate |
| Assign | person_add | Dropdown list of sale agents -> PUT assign |
| Snooze | snooze | Modal: 30m / 1h / 4h / EOD / Custom -> POST snooze |
| Note | note_add | Popover textarea -> POST note |
| Label | label | Dropdown list of labels (with color dots) -> POST labels |

API calls are mutations with TanStack Query, on success -> toast + refetch conversations list.

### 4.5 ComposerWithAI

States:
- **Idle**: empty textarea, placeholder text
- **Typing**: user typing, no suggestion yet (debounce 400ms)
- **Suggesting**: ghost text visible after cursor, italic + gray
- **Command**: user typed / -> dropdown of commands
- **QR**: user typed // -> dropdown of quick reply templates
- **Sending**: mutation pending, textarea disabled
- **Error**: toast below composer, textarea re-enabled

Ghost text rendering:
- Use a hidden overlay div with the same dimensions as textarea
- Ghost text is the user text + suggestion rendered in gray
- When user presses Tab, append suggestion to actual textarea value

### 4.6 SideDrawer

- Slide from right: `translate-x-0` (open) vs `translate-x-full` (closed)
- Width: 320px (w-80)
- Close: click backdrop overlay or X button
- Sections (scrollable):
  1. Customer info: name, phone, email, lead score, platform
  2. Timeline: all messages from this customer grouped by date
  3. Notes: private notes list + add note form
- Loading: skeleton while fetching
- Error: "Could not load customer context" with retry button

---

## 5. Data Flow Diagrams

### 5.1 Inline AI Suggest Flow

```
ComposerWithAI                CopilotEndpoints              ChatAgent              Frontend
     |                              |                          |                      |
     |-- gõ >=3 ký tự ------------->|                          |                      |
     |                              |-- Request context ------->|                      |
     |                              |<-- ChatAgentReply --------|                      |
     |<-- Suggestion ---------------|                          |                      |
     |-- render ghost text                                                                 |
     |-- Tab accept -> set text                                                            |
     |-- Enter send                                                                        |
     |-----------------------------------------------> SendOutboundAsync -> channel        |
```

### 5.2 Permission Check Flow

```
HTTP Request -> RequirePermission("conversations:read")
  -> PermissionAuthorizationHandler (JWT perm claim check)
  -> PermissionEndpointExtensions (runtime role resolution)
  -> InboxEndpoints.ListAsync
       -> Extract sub (userId) + role_id
       -> Resolve permissions via IPermissionResolver
       -> If "admin:inboxes" -> no filter
       -> Else -> query InboxMembers -> filter InboxId IN (...)
       -> Return filtered ConversationListResponse
```

---

## 6. Error Handling Strategy

| Scenario | User-facing message | HTTP code |
|---|---|---|
| Sale has no inboxes | "No conversations found" + CTA to contact admin | 200 with empty list |
| Conversation not found | "Conversation not found" | 404 |
| Permission denied | "You don't have access to this conversation" | 403 |
| AI suggest fails | No ghost shown, composer works normally | 200 with null suggestion |
| Label creation fails | "Could not create label" + toast | 500 |
| Note save fails | "Could not save note" + toast | 500 |
| Snooze fails | "Could not snooze conversation" + toast | 500 |

---

## 7. Security & Privacy

1. **Per-sale isolation**: backend-enforced via InboxMembers filter (not client-side)
2. **SignalR isolation**: sale receives events only for inboxes they're assigned to
3. **Private notes**: Type="private" notes are never sent to external channels
4. **AI suggest context**: only conversation history + KB, no notes
5. **Admin override**: admin sees all, can reassign conversations between sales

---

## 8. Testing Strategy

### Backend unit tests

| Test | What |
|---|---|
| InboxEndpointsTests | ListAsync filters by InboxMembers for non-admin |
| InboxEndpointsTests | ListAsync returns all for admin |
| InboxEndpointsTests | GetAsync returns 403 for unauthorized |
| CopilotEndpointsTests | Suggest returns null for empty draft |
| LabelsEndpointsTests | CRUD label |
| NotesEndpointsTests | Create/read note |

### Frontend component tests (vitest)

| Test | What |
|---|---|
| AgentHubLayout | Opens conversation in new tab |
| ConversationTabs | Closes tab, switches to sibling |
| CommandPalette | Filters actions by query |
| ComposerWithAI | Shows/hides ghost text |
| SideDrawer | Loads customer timeline |

### E2E tests (Playwright)

| Test | What |
|---|---|
| Sale login | Sees only assigned conversations |
| Admin login | Sees all conversations |
| Assign agent | Admin assigns sale to inbox, sale sees conversations |
| AI suggest | Ghost text appears, Tab accepts |

---

## 9. Migration & Deployment

### Database migrations

1. `0020_inboxes_channels_inboxmembers.sql` -- Inboxes, Channels, InboxMembers, ConversationReadState
2. `0021_alter_conversations_messages.sql` -- ALTER Conversations + Messages
3. `0022_labels_conversation_labels_notes.sql` -- Labels, ConversationLabels, ConversationNotes

### Seed data

- Insert permission `admin:inboxes` into seeded permissions
- Link to admin role via role_permissions

### Feature flag

Add feature flag `agent_hub_redesign` in tenant config:
- ON: use AgentHubLayout
- OFF: use old ConversationsPage (fallback)

### Rollback

- Revert frontend routing to old ConversationsPage
- Revert backend filters (remove InboxMembers filter)
- No data loss: new tables are additive

---

## 10. Open Questions

1. Should sale see unassigned conversations too, or only their assigned ones? (Current design: assigned only + admin sees all)
2. Quick reply templates -- reuse existing `quick_replies` table or integrate with Label system?
3. Copilot suggest -- use the existing ChatAgent or create a lighter, cheaper model endpoint for suggestions?
