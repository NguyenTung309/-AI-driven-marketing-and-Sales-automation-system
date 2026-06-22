# Channel Isolation & Token UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use writing-plans to create the plan, then use superpowers:subagent-driven-development or superpowers:executing-plans to implement. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Add channel selection screen before Agent Hub, scope conversations per-channel, and let admin manage channel tokens.

**Architecture:** New route /inbox lists channels from InboxMembers; /inbox/{channelId} loads 3-panel Agent Hub scoped to that InboxId. Inbox entity gets EncryptedAccessToken. Admin UI gets single-select agent + token input.

**Tech Stack:** .NET 8, EF Core, SQL Server, React + TanStack Query, Tailwind CSS.

---

## File Structure

### New files
| File | Responsibility |
|---|---|
| deploy/migrations/0030_add_inbox_encrypted_token.sql | Add EncryptedAccessToken column to Inboxes |
| deploy/migrations/0031_unique_inbox_members.sql | Unique constraint on InboxMembers(InboxId) |
| src/frontend/.../features/inbox/ChannelListPage.tsx | Channel selection screen |
| src/frontend/.../features/inbox/ChannelCard.tsx | Single channel card |
| src/frontend/.../features/agent-hub/ConversationTabs.tsx | Tab bar (max 7, overflow dropdown) |
| src/frontend/.../features/agent-hub/TabConversation.tsx | Single tab: name + unread badge + icon |
| src/frontend/.../features/agent-hub/CommandPalette.tsx | Ctrl+K modal, search conv + actions |
| src/frontend/.../features/agent-hub/SideDrawer.tsx | Customer timeline + notes + lead info |
| src/frontend/.../features/agent-hub/CustomerTimeline.tsx | Timeline events cross-platform |

### Modified files
| File | Change |
|---|---|
| src/shared/Clawbot.Domain/Channels/Inbox.cs | Add EncryptedAccessToken property |
| src/shared/Clawbot.Domain/Conversations/Conversation.cs | Add ReopenIfNeeded, SnoozedUntil field |
| src/shared/Clawbot.Infrastructure/Data/AppDbContext.cs | Add unique index on InboxMembers(InboxId) |
| src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs | Add GET /channels, InboxId query param, admin view-only check |
| src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs | Single-select agent, reassign endpoint, accept pageAccessToken |
| src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs | Block admin from creating labels |
| src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs | Block admin from creating notes |
| src/api/Clawbot.Api/Endpoints/PublicWidgetEndpoints.cs | Call ReopenIfNeeded on inbound message |
| src/frontend/.../features/admin/ChannelManagementPage.tsx | Single-select dropdown, token input |
| src/frontend/.../features/agent-hub/AgentHubLayout.tsx | Accept channelId, filter by InboxId |
| src/frontend/.../shared/api/admin.ts | API fns for token, single-select member |
| src/frontend/.../shared/api/inbox.ts | API fn for GET /channels |
| src/frontend/.../app/routes.tsx | Route /inbox + /inbox/:channelId |
| src/frontend/.../app/lazyPages.tsx | Lazy load ChannelListPage |

---

## Phase 1: Backend - Token + Channel API

### Task 1.1: Add EncryptedAccessToken to Inbox entity

**Files:**
- Modify: src/shared/Clawbot.Domain/Channels/Inbox.cs
- Create: deploy/migrations/0030_add_inbox_encrypted_token.sql

- [ ] **Step 1: Add property to Inbox.cs**

Add EncryptedAccessToken property + SetAccessToken() method. Property is nullable string. Method takes encrypted token + DateTimeOffset, sets both fields.

- [ ] **Step 2: Create migration**

ALTER TABLE Inboxes ADD EncryptedAccessToken NVARCHAR(1024) NULL;

- [ ] **Step 3: Commit**

git add src/shared/Clawbot.Domain/Channels/Inbox.cs deploy/migrations/0030_add_inbox_encrypted_token.sql
git commit -m "feat: add EncryptedAccessToken to Inbox entity"

### Task 1.2: GET /api/inbox/channels endpoint

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] **Step 1: Add route grp.MapGet("/channels", ListChannelsAsync)**

- [ ] **Step 2: Implement ListChannelsAsync**

Resolve isAdmin via permResolver. Admin sees all channels. Sale sees only InboxMembers channels.
Return: id, name, platform, externalPageId, isActive, hasToken, unreadCount, memberDisplayName.
Order by unreadCount desc, then name.

- [ ] **Step 3: Add Guid? inboxId query param to ListAsync, GetAsync, SearchAsync**

Filter query: if (inboxId.HasValue) query = query.Where(c => c.InboxId == inboxId.Value);

- [ ] **Step 4: Commit**

### Task 1.3: ReopenIfNeeded + SnoozedUntil

**Files:**
- Modify: src/shared/Clawbot.Domain/Conversations/Conversation.cs
- Modify: src/api/Clawbot.Api/Endpoints/PublicWidgetEndpoints.cs

- [ ] **Step 1: Add to Conversation.cs**

public DateTimeOffset? SnoozedUntil { get; private set; }

public void ReopenIfNeeded()
{
    if (Status != "snoozed" && Status != "resolved") return;
    Status = "open";
    SnoozedUntil = null;
}

- [ ] **Step 2: Call from inbound ingestor after upsert**

if (conv.Status == "snoozed" || conv.Status == "resolved")
{
    conv.ReopenIfNeeded();
    await db.SaveChangesAsync(ct);
    await notifier.NotifyConversationUpdatedAsync(...);
}

- [ ] **Step 3: Commit**

### Task 1.4: Admin view-only enforcement

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs (SendOutboundAsync)
- Modify: src/api/Clawbot.Api/Endpoints/LabelsEndpoints.cs (CreateAsync)
- Modify: src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs (CreateAsync, UpdateAsync)

- [ ] **Step 1: SendOutboundAsync**

After resolving permissions, if user has admin:inboxes AND is not InboxMember of that inbox -> return Results.Forbid()

- [ ] **Step 2: LabelsEndpoints.CreateAsync**

If perms contains admin:inboxes -> return Results.Forbid()

- [ ] **Step 3: InboxNotesEndpoints.CreateAsync + UpdateAsync**

Same pattern: if admin:inboxes -> Forbid()

- [ ] **Step 4: Commit**

### Task 1.5: Single-select agent + reassign + token

**Files:**
- Modify: src/api/Clawbot.Api/Endpoints/AdminInboxEndpoints.cs

- [ ] **Step 1: Change UpdateMembers to Single Guid? AgentId**

Replace UpdateMembersRequest(Guid[] AgentIds) with UpdateMemberRequest(Guid? AgentId).
On save: remove all existing members, add new one.
If AgentId is null and members exist -> 400 "inbox_must_have_member".
Unassign conversations from old members.

- [ ] **Step 2: Add POST /api/admin/inboxes/{id}/reassign**

Replace members, unassign conversations, write audit log, notify old members.
Return: inboxId, oldAgentIds, newAgentId, unassignedConversationCount.

- [ ] **Step 3: Accept pageAccessToken in channel create/update**

Add PageAccessToken field to CreateChannelRequest. If provided, encrypt via IEncryptor then call inbox.SetAccessToken().

- [ ] **Step 4: Commit**

### Task 1.6: Unique constraint InboxMembers(InboxId)

**Files:**
- Create: deploy/migrations/0031_unique_inbox_members.sql
- Modify: src/shared/Clawbot.Infrastructure/Data/AppDbContext.cs

- [ ] **Step 1: Migration**

CREATE UNIQUE INDEX uq_inbox_members_inbox ON InboxMembers (InboxId);

- [ ] **Step 2: EF Core config**

modelBuilder.Entity<InboxMember>(e => {
    e.HasIndex(m => m.InboxId).IsUnique().HasDatabaseName("uq_inbox_members_inbox");
});

- [ ] **Step 3: Commit**

---

## Phase 2: Frontend - Channel Selection + Agent Hub

### Task 2.1: ChannelListPage + ChannelCard

**Files:**
- Create: src/frontend/.../features/inbox/ChannelListPage.tsx
- Create: src/frontend/.../features/inbox/ChannelCard.tsx
- Modify: src/frontend/.../app/routes.tsx, lazyPages.tsx

- [ ] **Step 1: ChannelListPage.tsx**

- useQuery to GET /api/inbox/channels
- Grid card layout (sm:2, lg:3 columns)
- Empty state: "Ban chua duoc gan kenh nao. Lien he admin."
- Loading state: spinner centered

- [ ] **Step 2: ChannelCard.tsx**

- Platform icon (Z for zalo, F for facebook) in circular background
- Channel name, member display name
- Unread badge (red circle, max 99+)
- Link to /inbox/{channel.id}

- [ ] **Step 3: Routes**

/inbox -> ChannelListPage
/inbox/:channelId -> AgentHubLayout
Lazy load both.

- [ ] **Step 4: Commit**

### Task 2.2: AgentHubLayout - channelId scope + admin read-only

**Files:**
- Modify: src/frontend/.../features/agent-hub/AgentHubLayout.tsx

- [ ] **Step 1: Read useParams<{ channelId }>() from route**

- [ ] **Step 2: Pass inboxId query param to TanStack Query for conversations**

queryKey: ["inbox", "conversations", channelId]
queryFn: apiClient.get("/api/inbox/conversations", { params: { inboxId: channelId } })

- [ ] **Step 3: Add header bar**

Left: back link "Tat ca kenh" -> /inbox
Center: channel name

- [ ] **Step 4: Admin read-only mode**

If hasPermission('admin:inboxes'):
- Hide ComposerWithAI
- Hide QuickActionBar
- Show warning badge: "Xem chi doc - Ban co quyen admin"

- [ ] **Step 5: Commit**

### Task 2.3: Missing Agent Hub components

**Files:**
- Create: src/frontend/.../features/agent-hub/ConversationTabs.tsx
- Create: src/frontend/.../features/agent-hub/TabConversation.tsx
- Create: src/frontend/.../features/agent-hub/CommandPalette.tsx
- Create: src/frontend/.../features/agent-hub/SideDrawer.tsx
- Create: src/frontend/.../features/agent-hub/CustomerTimeline.tsx
- Modify: src/frontend/.../features/agent-hub/index.ts

- [ ] **Step 1: ConversationTabs.tsx**

Horizontal scrollable tab bar, max 7 visible tabs. Overflow shown in hover dropdown "+N".
Active tab highlighted with primary color border.

- [ ] **Step 2: TabConversation.tsx**

Button: platform letter (Z/F) + truncated name + optional unread badge (red circle).

- [ ] **Step 3: CommandPalette.tsx**

Ctrl+K listener. Modal overlay with search input. Filter commands by label.
Esc or click outside to close. On select: run action + close.

- [ ] **Step 4: SideDrawer.tsx**

Slide panel (w-80, border-left). Sections: Lead score bar, Notes textarea, CustomerTimeline.
Close button top-right.

- [ ] **Step 5: CustomerTimeline.tsx**

Vertical timeline: dot + time + description for each event.
Static data for MVP (render from conversation events when available).

- [ ] **Step 6: Update index.ts**

Export all new components.

- [ ] **Step 7: Commit**

### Task 2.4: ChannelManagementPage - token + single-select agent

**Files:**
- Modify: src/frontend/.../features/admin/ChannelManagementPage.tsx
- Modify: src/frontend/.../shared/api/admin.ts

- [ ] **Step 1: Update admin.ts**

getSimpleUserList(), updateInboxMember(inboxId, agentId | null), reassignInbox(inboxId, newAgentId), createInbox(data with optional pageAccessToken)

- [ ] **Step 2: ChannelManagementPage.tsx - gan sale section**

Replace multi-select AgentIds checkboxes with single-select dropdown.
Label: "Gan kenh nay cho sale"
Options: list users from getSimpleUserList().
When saving, call updateInboxMember() with single agentId.

- [ ] **Step 3: ChannelManagementPage.tsx - token input**

Add field to create/edit form: input type=password for pageAccessToken.
Label: "Page Access Token (tu Pancake)"
Sub-text: "Token duoc encrypt va luu tru bao mat."

- [ ] **Step 4: Commit**

---

## Phase 3: Post-MVP (Spec Section 7)

### Task 3.1: Daily summary API + job

- [ ] **Step 1: Backend - GET /api/inbox/daily-summary**

Count conversations handled today, messages sent, open conversations, close rate (last 30d) for the current sale.

- [ ] **Step 2: Backend - DailySummaryJob (Hangfire)**

Recurring job at 21:00 GMT+7. Query all sales, push notification with summary URL.

- [ ] **Step 3: FE - Daily summary popup**

TanStack Query to GET /api/inbox/daily-summary. Show in a floating card: conversations handled, messages sent, open, close rate.

- [ ] **Step 4: Commit**

### Task 3.2: Pipeline stage suggestion (UC-C06)

- [ ] **Step 1: Backend - pipeline stage calculation**

Stage based on lead score + conversation history:
- Moi tiep can (score < 30)
- Dang tu van (30-69, >1 message)
- Sap chot (>= 70, has price/booking intent)
- Da chot (resolved + lead won)

- [ ] **Step 2: FE - pipeline bar in SideDrawer**

Show current stage + suggested action buttons.

- [ ] **Step 3: Commit**

### Task 3.3: Tone warning (UC-C09)

- [ ] **Step 1: Backend - content check on send**

Regex + word blacklist + basic sentiment. Return warning if issues found.
Does not block sending, just warns.

- [ ] **Step 2: FE - warning toast before send**

If backend returns warning, show toast: "Tin nhan co tu ngu co the gay hieu lam"
Sale can still send or edit.

- [ ] **Step 3: Commit**
