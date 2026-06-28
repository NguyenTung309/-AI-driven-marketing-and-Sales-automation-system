# SPEC v2 — Pancake Chat Display & Routing Fix

## Overview

Spec v1 (Agent Hub) covered per-sale isolation, tabs, copilot, labels. Sau khi chay thuc te phat hien **6 van de** hien thi chat tu Pancake/Zalo:

1. **Ten cuoc hoi thoai** hien `pzl_g_xxx` thay vi ten nguoi/nhom (VD: "Phạm Hiền", "Nhóm đồ án")
2. **Avatar mac dinh** (chu cai trong vong tron) thay vi anh that tu Zalo CDN
3. **Khong hien ten nguoi gui** tren tung tin nhan - khong biet ai nhan cai gi
4. **Tin nhan owner sai canh** — tin owner hien ben trai nhu khach, trong khi dung ra owner phai ben phai (khong can ten/avatar), khach ben trai (co ten + avatar)
5. **Sale B khong thay conversation nao** — `ResolveInboxIdAsync` uu tien inbox co member (Sale A), bo qua Sale B
6. **Ten kenh hien "Sale A", "Sale B"** thay vi ten Zalo that (VD: "Lê Minh Thắng")

## Goc re

`PancakePollingService` chi lay `conv.Id` + `snippet` + `message_count` tu API conversations. **Khong lay** `from.name`, `from.avatar_url`, `customers[]`, `last_sent_by.id`, `last_sent_by.name`. Do do:

- Contact duoc tao voi displayName = `ExternalThreadId` (= `pzl_g_xxx`)
- Khong co avatar_url de luu
- Khong co last_sent_by.id de phan biet owner vs contact
- Khong co sender name de hien thi
- Khong co page admin name de update inbox name

## Giai phap tong the

### Layer 1: Backend — PancakePollingService fetch them data

Khi poll conversation list, BO SUNG parse `from.name`, `from.avatar_url`, `customers`, `last_sent_by` tu response:

- `from.name` → conversation display name
- `from.avatar_url` → contact avatar
- `customers[].name` → group member names
- `customers[].avatar_url` → group member avatars
- `last_sent_by.id` + `page_id` → direction (owner neu id == page_id, contact neu khac)
- `last_sent_by.name` → sender display name
- `last_sent_by.name` khi `last_sent_by.id == page_id` → page admin name (dung de update Inbox.Name)

### Layer 2: Backend — ChannelMessageIngestor xu ly thong tin

- `UpsertContactAsync`: luu `display_name` tu `from.name`, luu `avatar_url`
- `IngestAsync`: xac dinh `direction` dua tren `sender_id == page_id` → "out" (owner), khac → "in" (contact)
- `ResolveInboxIdAsync`: xoa uu tien inbox co member, dung page_id exact match
- **Update Inbox.Name**: cap nhat ten kenh tu page admin name

### Layer 3: Backend — API tra ve avatar + sender name

- `ConversationListItemDto`: them `ContactAvatarUrl`
- `MessageDto`: them `SenderDisplayName`
- `InboxChannel` response: them `pageName` (ten Zalo that)
- `ListAsync`: join contact.avatar_url vao query
- `GetAsync`: join sender name vao message query
- `ListChannelsAsync`: tra ve page_name tu inbox external_page_name

### Layer 4: Frontend — ChatMessageThread + ConversationList + ChannelCard

- `ChatMessageThread`: message direction="out" → canh phai, khong ten/avatar. direction="in" → canh trai, co avatar + ten nguoi gui
- `ConversationList`: avatar_url thay vi fallback div
- `ConversationTabs`: avatar nho thay vi platform icon
- `ChannelCard`: hien `pageName` ben canh `name` (hoac thay the)

## Data model changes

### Contact entity — them `AvatarUrl`

```
Contact.AvatarUrl: string? (NVARCHAR 512)
```

### Message — them `SenderDisplayName`

```
Message.SenderDisplayName: string? (NVARCHAR 256)
```

### Inbox entity — them `ExternalPageName` (hoac update Name)

**Option A**: Them field `ExternalPageName`
```
Inbox.ExternalPageName: string? (NVARCHAR 256)
```

**Option B**: Update `Inbox.Name` truc tiep tu Pancake data
```
(Khong can migration — chi update gia tri cot Name)
```

Chon **Option B** vi don gian: `Name` hien tai la admin-set ("Sale A"), se duoc update boi backend thanh ten Zalo that. Sale name van hien qua `memberDisplayName` trong ChannelCard.


## API changes

### GET /api/inbox/channels
Response items them pageName (alias cua inbox.Name sau khi da update tu Pancake)

### GET /api/inbox/conversations
Response items them contactAvatarUrl, inboxId, inboxName, inboxAvatarUrl

### GET /api/inbox/conversations/{id}
Response messages them senderDisplayName, conversation them inboxId, inboxName, inboxAvatarUrl

## Flow diagram

`
Pancake API (poll)
  | GET /v2/pages/{pageId}/conversations
  | response: from.name, from.avatar_url, last_sent_by.id,
  |           last_sent_by.name, customers, page_id
  V
PancakePollingService
  | Parse + bo sung Metadata:
  |   display_name = from.name
  |   avatar_url = from.avatar_url
  |   sender_name = last_sent_by.name
  |   page_id = pageId (tu URL)
  |   is_owner = (last_sent_by.id == pageId)
  |   page_admin_name = last_sent_by.name (if is_owner)
  V
ChannelMessageIngestor
  | UpsertContact: displayName + avatarUrl tu metadata
  | Ingest: direction = is_owner ? "out" : "in"
  | ResolveInboxId: match page_id exact
  | Update inbox Name = page_admin_name (neu khac)
  V
InboxEndpoints (API)
  | ConversationListItemDto.ContactAvatarUrl, InboxId, InboxName, InboxAvatarUrl
  | MessageDto.SenderDisplayName
  | InboxChannel: name da duoc update tu Pancake
  V
Frontend
  | ChatMessageThread: avatar + name ben trai, owner ben phai
  | ConversationList: avatar_url
  | ChannelCard: hien ten Zalo that
`

## Non-goals (v2)

- Khong sua realtime SignalR message push (chi sua polling ingestion)
- Khong them search/sort theo ten (chi hien thi dung)
- Khong sua copilot / AI features
- Khong sua webhook path (chi polling path)
- Khong them API moi de lay page info tu Pancake (chi trich xuat tu conversation list co san)
- Khong lam "gop trang" (merge channels) - chi chon 1 kenh vao workspace kenh do
- Khong lay SDT khach hang tu Pancake vi API khong gui
- Khong luu SenderAvatarUrl per-message - dung Contact.AvatarUrl la du
