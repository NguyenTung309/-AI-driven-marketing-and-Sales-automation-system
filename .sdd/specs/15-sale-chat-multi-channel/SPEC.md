# Agent Hub: Sale Agent Command Center

## Overview

Thiet ke lai inbox cho sale agent: multi-conversation tabs, per-sale isolation, AI copilot inline, quick actions, labels & notes.

Sale chi thay conversation cua inbox minh duoc gan. Admin thay tat ca. Layout 3-panel thay bang Agent Hub voi tab bar + side drawer.

---

## Section 1: Per-Sale Conversation Isolation

### Van de
InboxEndpoints.ListAsync tra ve tat ca conversation trong tenant. Sale thay het.

### Giai phap
Dung InboxMembers co san de filter:
- Sale: chi thay conversation thuoc inbox ma ho duoc gan
- Admin: thay tat ca (dung permission admin:inboxes)
- Conversation cu (InboxId = null): sale khong thay, admin thay + canh bao

### Endpoint changes

InboxEndpoints.cs (ListAsync, GetAsync, SearchAsync):
- Lay userId tu claim sub, roleId tu claim role_id
- Resolve permissions qua IPermissionResolver
- Neu co admin:inboxes => khong filter
- Neu khong => query InboxMembers => filter Conversation.InboxId IN (...)
- Conversation InboxId = null: chi admin thay

InboxHub.cs:
- Giu tenant group cho admin
- Them per-user group user:{userId}
- Sale chi join inbox groups ma ho duoc member
### Edge: Backfill du lieu cu

Can migration data backfill:
- Tim tat ca Conversation co InboxId = null
- Map ExternalThreadId -> InboxId qua Inboxes.ExternalPageId + Platform
- Neu khong tim duoc: set InboxId = default inbox cung platform
- Sau backfill, khong con conversation InboxId = null

### Edge: Seed permission admin:inboxes

- Them permission code admin:inboxes vao bang Permissions
- Seed vao bang RolePermissions cho role Admin (khong seed cho role Sale)
- Assertion script kiem tra sau migrate

### Edge: Concurrency

- Them field RowVersion (TIMESTAMP) vao Conversation entity
- Update endpoints (resolve, reassign, snooze, escalate) check RowVersion
- Tra ve 409 Conflict neu version khong khop
- FE hien thi: Trang thai da thay doi, vui long tai lai

---


## Section 2: Agent Hub Layout

### Layout

+------------------------------------------------------------------+
| Topbar: Ctrl+K command palette + search + notification + avatar  |
+--------+-------------------------------------------+-------------+
| List   | Tab bar ----+----+----+----+----+----     | Side Drawer |
| panel  |             |    |    |    |    |          | (slide)     |
|        +------------+----+----+----+----+----------+             |
| smart  | Chat area (active tab)                     | Customer    |
| sort   | - message list + date separator            | timeline    |
| prio   | - typing indicator                         |             |
| filter | - reply-to thread                          | Lead score  |
|        | - quick action bar                         |             |
|        |   [Resolve] [Assign] [Label] [Note]        | Notes       |
|        |                                            |             |
|        | +----------------------------------------+ | QR templates|
|        | | Composer + AI ghost suggestion          | |             |
|        | | [Tab accept, Esc dismiss]               | |             |
|        | +----------------------------------------+ |             |
+--------+-------------------------------------------+-------------+

### Components

| Component | File | Responsibility |
|---|---|---|
| AgentHubLayout | features/agent-hub/AgentHubLayout.tsx | 3-panel layout + tabs + drawer state |
| ConversationTabs | features/agent-hub/ConversationTabs.tsx | Tab bar, max 7 tabs, overflow dropdown |
| TabConversation | features/agent-hub/TabConversation.tsx | 1 tab: name + unread badge + platform icon |
| CommandPalette | features/agent-hub/CommandPalette.tsx | Ctrl+K modal, search conv + actions |
| SideDrawer | features/agent-hub/SideDrawer.tsx | Customer timeline + notes + lead info |
| CustomerTimeline | features/agent-hub/CustomerTimeline.tsx | Timeline events cross-platform |
| QuickActionBar | features/agent-hub/QuickActionBar.tsx | Resolve/assign/label/snooze buttons |
| ComposerWithAI | features/agent-hub/ComposerWithAI.tsx | Composer + ghost text + /menu |
| ChatMessageThread | features/agent-hub/ChatMessageThread.tsx | Message with reply-to + reactions |

### Edge: Reassign khi dang mo tab (#12)

Notifier can ban rieng event conversation:reassigned toi user:{oldAssignee}
FE lang nghe event, tu dong dong tab + chuyen composer sang read-only
Khong cho gui tin nhan sau khi bi go quyen

### Edge: Multi-device sync (#20)

SignalR broadcast outbound message toi chinh user:{userId} cua sale do
Cac tab/device khac tu dong sync composer state
Tra ve trang thai dang co nguoi gui de tranh gui trung

---


## Section 3: AI Copilot - Inline Suggest

### Nguyen ly
Copilot song trong compose box. Sale go => AI suggest ghost text.

### API

| Endpoint | Method | Purpose |
|---|---|---|
| POST /api/inbox/conversations/{id}/copilot/suggest | POST | AI draft suggestion |
| POST /api/inbox/conversations/{id}/copilot/summarize | POST | Summarize conversation |

### Suggest flow
1. Sale go >=3 ky tu => debounce 400ms => POST copilot/suggest
2. Backend: ChatAgent + conversation context => tra ve suggestion (hoac null)
3. Frontend: ghost text ngay sau cursor (mau xam, italic)
4. Sale nhan Tab => accept ghost, Esc => dismiss
5. Khong suggest khi draft > 200 ky tu

### Composer commands
Go trong compose box:
- / => mo command menu (resolve, assign, label, snooze, note)
- // => mo quick reply library

### Edge: Race condition giua nhieu suggest (#13)
Dinh kem requestId / draftVersion tang dan
FE chi ap dung response neu requestId khop voi request moi nhat
Bo qua response tre

### Edge: PII redaction cho Copilot (#14)
Copilot suggest phai tai su dung pipeline PII redaction cua ChatAgent
Khong bo qua buoc PII khi gui history len model
Redact truoc khi gui, khong redact tren response ve sale

### Edge: Token cost rieng cho Copilot (#15)
Tach quota / rate-limit rieng cho copilot suggest
Toi da N request/phut/agent
Uu tien budget cho ChatAgent chinh khi gan cap thang

---


## Section 4: Data Model

### Label

Label: Id, TenantId, Name (NVARCHAR 128 unique/tenant), Color (NVARCHAR 7 hex), CreatedAt, DeletedAt (soft-delete)

ConversationLabel: ConversationId (FK), LabelId (FK), PK (ConversationId, LabelId)

ConversationNote: Id, TenantId, ConversationId (FK), CreatedByUserId (FK), Content (NVARCHAR 2000), Type (private|summary), CreatedAt, UpdatedAt

### RowVersion (concurrency)
Them RowVersion (TIMESTAMP) vao Conversation. Tat ca update endpoint check.

### Indexes

CREATE UNIQUE INDEX ix_labels_tenant_name ON Labels (TenantId, Name) WHERE DeletedAt IS NULL;
CREATE INDEX ix_notes_conv ON ConversationNotes (ConversationId);
CREATE INDEX ix_conv_labels_label ON ConversationLabels (LabelId);

### Edge: User bi xoa (#19)
Dung soft-delete cho User hoac luu snapshot CreatedByDisplayName tai thoi diem tao note
Khong phu thuoc join song vao bang User de render lich su note

---


## Section 5: Conversation Status Lifecycle

### Status flow

open <-> pending <-> resolved
  ^        v
  +-- snoozed (SnoozeUnSnoozeJob revert => open sau khi het han)

Field SnoozedUntil (DATETIMEOFFSET?) trong Conversation.

### Edge: Auto-unsnooze khi co tin nhan moi (#17)
Khi nhan inbound message moi, bat ky trang thai snooze hoac resolved:
- Tu dong revert Status = open ngay (event-driven)
- Clear SnoozedUntil (neu co)
- Giu nguyen AssignedTo (sale cu khong bi unassign)
- Notifier broadcast cho inbox group + assigned sale
- Job SnoozeUnSnoozeJob chi la fallback cho het han tu nhien

Gop logic ReopenIfNeeded(conversation) xu ly ca 2 case (snoozed, resolved) de tranh duplicate code.
Goi tu Inbound Ingestor (PublicWidgetEndpoints hoac ChannelMessageIngestor).

---


## Section 6: Assignee scope

### Edge: Dropdown Assign chi hien agent trong InboxMembers (#18)
Endpoint list assignee phai loc theo InboxMembers cua dung InboxId thuoc conversation do
Khong lay toan bo user tenant
Khi sale assign, validate AssignedTo thuoc InboxMembers truoc khi ghi

---


## Edge: Lag cache permission khi thay doi quyen (#11)

IPermissionResolver co InvalidateAsync(roleId). InboxHub co IMemoryCache inboxIds.
Khi admin thay doi quyen, can goi ca InvalidateAsync va evict cache inboxIds cung luc.
Neu khong, agent van giu quyen cu cho toi khi reconnect hoac cache het han.

**Giai phap:** Them 1 endpoint POST /api/admin/flush-permission-cache goi ca 2 invalidate.
Goi endpoint nay tu admin UI sau khi sua role. Khong tu dong sync (toi gian).

---


## Section 7: Bo sung UC thieu trong FT-04

### 7.1 Pipeline stage suggestion (UC-C06)

**Van de:** Sale dang chat ma khong biet khach dang o giai doan nao trong pipeline (moi tiep can/dang tu van/sap chot) va buoc tiep theo la gi.

**Giai phap:** Backend tinh toan pipeline stage dua tren lead score + conversation history + intent. Frontend hien thi pipeline bar trong SideDrawer.

Pipeline stages:
1. Moi tiep can (lead score < 30)
2. Dang tu van (30-69, co tu van > 1 tin)
3. Sap chot (>= 70, co hoi gia/dat lich)
4. Da chot (resolved + co lead chot)

Moi stage co goi y action:
- Moi tiep can => Tim hieu nhu cau, gui brochure
- Dang tu van => Gui bao gia, dat lich hoc thu
- Sap chot => Goi y upsell, nhan manh uu dai
- Da chot => Gui feedback survey, referral program

### 7.2 Tone warning (UC-C09)

**Van de:** Sale dung tu ngu khong phu hop (gap gao, thieu chuyen nghiep, sai chinh ta) => mat thiem cam voi khach.

**Giai phap:** Khi sale nhan Enter gui tin, kiem tra content qua 1 service nhe (regex + word blacklist + basic sentiment). Neu co van de:
- Hien warning toast: Tin nhan co tu ngu co the gay hieu lam
- Sale co the gui lai hoac bo qua
- Khong chan gui, chi canh bao
- Log vao audit de training

### 7.3 Daily summary (UC-C10)

**Van de:** Sale het ngay khong biet minh da handle bao nhieu, ti le chot, conversation nao can follow-up.

**Giai phap:** Job chay cuoi ngay (21:00 GMT+7), tong hop:
- So conversation da xu ly
- So tin nhan da gui
- Lead moi capture
- Conversation con open can follow-up
- Ti le chot (resolved / total)
Goi toi sale qua:
- Telegram bot
- Notification trong Agent Hub (notification bell + popup)
- Timeline /dashboard personal

**API:** GET /api/inbox/daily-summary => tra ve summary cua ngay hom nay
**Background job:** DailySummaryJob (Hangfire, 21:00 hang ngay)

---

---

## Section 8: Business Model Constraints (Pancake model)

### 8.1 Moi channel co dung 1 sale phu trach

**Rang buoc nen (confirmed by product owner):**
- Moi kenh Zalo OA / Facebook page gan voi 1 page_access_token tu Pancake
- Moi channel (Inbox) co **dung 1 sale** lam InboxMember
- **Khong co** chuyen 2 sale cung xu ly 1 channel
- Khach thuoc channel nao -> sale do cham tu dau den duoi
- Chuyen kenh la KHONG hop le trong model nay

**He qua thiet ke:**
- InboxMembers se co toi da 1 member/inbox (sale). Admin khong can add lam member vi admin co dmin:inboxes thay duoc tat ca
- Auto-assign/Claim khong can - moi channel da co chu duy nhat
- Claim lock khong can - khong co conflict vi chi 1 sale thay
- Handoff/Transfer Inbox khong ap dung
- Escalate vo dung - admin khong nhan duoc, sale khac khong thay

### 8.2 Admin chi xem, khong nhan tin

Admin co quyen dmin:inboxes de **xem** tat ca conversation, nhung **khong duoc**:
- Gui tin nhan outbound
- Gan label cho conversation
- Tao/sua note
- Claim conversation

Admin chi lam duoc:
- Xem danh sach conversation
- Quan ly channel (tao Inbox, gan sale, reassign)
- Xem thong ke

### 8.3 Channel-Token mapping

Moi Inbox (channel) co 1 ExternalPageId + Platform (zalo/fb) + page_access_token (encrypted).
Token duoc nhap tu Pancake khi admin tao channel.
Token quyet dinh: khach tu platform nao -> channel nao -> sale nao.

---

## Section 9: Resolved -> Auto-reopen on Inbound

### Van de
Conversation da 
esolved. Khach nhan lai. Spec cu chi auto-unsnooze (tu snoozed -> open), khong auto-reopen tu resolved -> sale khong biet co tin moi vi UI thuong an conversation da resolved.

### Giai phap
Mo rong logic ReopenIfNeeded (da co trong inbound ingestor) de xu ly ca 2 case:

`csharp
// Trong inbound message flow, sau khi upsert conversation
if (conv.Status == "snoozed" || conv.Status == "resolved")
{
    conv.Status = "open";
    conv.SnoozedUntil = null;
    // Giu nguyen AssignedTo (sale cu van xu ly)
    // Notify ca inbox group + assigned sale
}
`

### API
Khong co endpoint moi - sua logic internal inbound ingestor.

### Edge: Resolved qua lau (vd 30 ngay)
MVP: luon reopen conversation cu, khong tao moi.
Khong co auto-archive trong phase nay.

---

## Section 10: Admin View-Only Enforcement

### Van de
Admin co dmin:inboxes co the goi SendOutbound endpoint va nhan tin, nhung theo business model admin KHONG duoc phep nhan.

### Giai phap

**Backend - SendOutbound check:**
`csharp
// Trong SendOutboundAsync
var isAdmin = perms.Contains("admin:inboxes");
var isMember = await db.InboxMembers.AnyAsync(m => m.AgentId == userId && m.InboxId == conv.InboxId, ct);
if (isAdmin && !isMember)
    return Results.Forbid(); // Admin view-only, khong phai member cua inbox nay
`

**Backend - Label/Note create check:**
- POST /api/inbox/conversations/{id}/labels: Forbid neu admin
- POST /api/inbox/conversations/{id}/notes: Forbid neu admin

**Frontend - AgentHubLayout:**
- Neu user la admin (hasPermission('admin:inboxes')) -> Composer an, QuickActionBar an, chi show chat read-only
- Them badge "Xem chi doc" (read-only) o header

### Edge: Admin co the van can note cho muc dich giam sat
-> Mo cho phase sau neu co yeu cau. Phase 1 admin read-only tuyet doi.

---

## Section 11: Channel Assignment & Reassignment

### 11.1 UI gan channel cho sale

**Van de:** Channel Management hien tai dung InboxMembers cho phep add nhieu member. Voi model 1-channel-1-sale, can validate chi 1 member.

**Giai phap:**
- Channel Management form: dropdown chon 1 sale (thay vi multi-select checkbox)
- Validate backend: PUT /api/admin/inboxes/{id}/members chi cho phep toi da 1 AgentId

**API sua:**
`
PUT /api/admin/inboxes/{id}/members
Body: { "agentId": "guid" }  // Thay vi AgentIds array
`

**Frontend:**
- Dropdown chon 1 sale (searchable)
- Hien thi ten sale hien tai (neu co)
- "Khong gan" -> unassign (set AssignedTo = null cho tat ca conversation dang assign cho sale cu)

### 11.2 Reassign channel khi sale nghi

**Van de:** Sale A nghi. Admin can chuyen channel cua A cho sale B. Conversations dang AssignedTo = A se treo.

**Giai phap:**
- Them action "Chuyen giao" trong Channel Management
- Chon sale moi tu dropdown
- Quy tac:
  1. Doi InboxMembers: xoa A, them B
  2. Conversations dang AssignedTo = A -> set AssignedTo = NULL (sale B nhan lai qua UI)
  3. Ghi audit log: ai chuyen, tu ai sang ai, bao nhieu conversation bi anh huong
  4. Notify inbox:{id} group + user:{oldAssignee} group

**API moi:**
`
POST /api/admin/inboxes/{id}/reassign
Body: { "newAgentId": "guid" }
`

**API sua:**
`
PUT /api/admin/inboxes/{id}/members
`
Sua thanh nhan gentId don thay vi array, validate max 1 member.

### 11.3 Validate khong cho go member cuoi

Neu Inbox hien tai co 1 member (sale) va request PUT /members gui gentId khac (reassign) -> OK.
Neu request gui gentId = null (unassign) -> tra ve 400: "Kenh phai co it nhat 1 sale phu trach".

Cach kiem tra:
`csharp
if (body.AgentId == null)
    return Results.BadRequest(new { error = "inbox_must_have_member", message = "Kenh phai co it nhat 1 sale phu trach" });
`

### 11.4 Frontend - Channel Management sua

- Dropdown single-select (thay vi multi-select)
- Label "Gan kenh nay cho sale:"
- Searchable user list
- Khi change: confirm dialog neu da co sale cu
"Ban sap chuyen kenh nay tu [Sale A] sang [Sale B]. Conversations dang xu ly boi [Sale A] se duoc bo gan. Tiep tuc?"

---

## Section 12: Channel-Token Mapping UI

### Van de
Admin them channel can nhap page_access_token tu Pancake. Hien tai khong co UI cho viec nay.

### Giai phap
- Them field page_access_token (encrypted) trong Channel form
- Backend: kiem tra entity Inbox da co EncryptedAccessToken field chua
- Frontend: them text input cho token o Channel Create/Edit form

### API
`
POST /api/admin/inboxes
Body: { "platform": "zalo"|"facebook", "externalPageId": "...", "name": "...", "pageAccessToken": "..." }
`


## Section 13: Channel Selection Screen

### Van de
Sale vao Agent Hub thay tat ca conversations tu moi channel tron lan.

### Giai phap
Them man hinh channel selection:
- Route /inbox: danh sach channel (card grid)
- Sale: chi thay channel duoc gan (InboxMembers)
- Admin: thay tat ca + badge
- Click card -> /inbox/{channelId} -> Agent Hub scope theo channel

### API moi
GET /api/inbox/channels - list channels + unread count

### Components
- ChannelListPage.tsx
- ChannelCard.tsx

### Routes
- /inbox -> ChannelListPage
- /inbox/:channelId -> AgentHubLayout


## Section 13: Channel Selection Screen

### Van de
Sale vao Agent Hub thay tat ca conversations tu moi channel tron lan.

### Giai phap
Them man hinh channel selection truoc Agent Hub:
- Route /inbox: danh sach channel (card grid)
- Sale: chi thay channel duoc gan (InboxMembers)
- Admin: thay tat ca + badge Admin
- Click card -> /inbox/{channelId} -> Agent Hub scope channel

### API moi
GET /api/inbox/channels - list channels + unread count

### Components
- ChannelListPage.tsx - grid layout, empty state
- ChannelCard.tsx - platform icon, name, unread badge

### Routes
- /inbox -> ChannelListPage
- /inbox/:channelId -> AgentHubLayout

---

## Section 14: Agent Hub Per-Channel

Giu nguyen 3-panel layout, sua:
- List panel: InboxId filter
- Header: back button to /inbox
- Admin: an Composer + QuickActionBar, badge Xem chi doc

---

## Section 15: Channel-Token Mapping (bo sung)

Inbox entity them EncryptedAccessToken.
Admin UI: token input trong channel form.
Token encrypt truoc khi luu, decrypt khi gui outbound.
Migration: ALTER TABLE Inboxes ADD EncryptedAccessToken NVARCHAR(1024) NULL.

