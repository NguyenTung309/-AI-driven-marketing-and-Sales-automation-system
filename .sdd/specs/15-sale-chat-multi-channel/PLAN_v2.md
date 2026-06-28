# Pancake Chat Display & Routing Fix — Implementation Plan v2

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans.

**Goal:** Fix 6 chat display issues: conversation names (pzl_xxx → real names), avatars, sender names, message alignment (owner right, contact left), and inbox routing fairness.

**Architecture:** 4 layers — PancakePollingService fetch more fields → ChannelMessageIngestor store + route → API expose → Frontend render.

**Tech Stack:** .NET 8, EF Core, React + TanStack Query, Tailwind CSS, SQL Server, Pancake API (pages.fm).

---

## File Structure

### Modified files

| File | Change |
|---|---|
| src/api/Clawbot.Api/Services/PancakePollingService.cs | Fetch from.name, avatar_url, last_sent_by.id/name, customers from Pancake API |
| src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs | Handle avatar, direction, sender_name, page_id routing, inbox name update |
| src/shared/Clawbot.Domain/Channels/Inbox.cs | Add UpdateName method |
| src/shared/Clawbot.Domain/Contacts/Contact.cs | Add AvatarUrl + UpdateAvatar |
| src/shared/Clawbot.Domain/Conversations/Message.cs | Add SenderDisplayName |
| src/shared/Clawbot.Domain/Conversations/Conversation.cs | Update AppendMessage signature (senderDisplayName param) |
| src/api/Clawbot.Api.Contracts/Inbox/InboxDtos.cs | Add ContactAvatarUrl, SenderDisplayName, InboxId, InboxName, InboxAvatarUrl |
| src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs | Return avatar_url, sender_display_name, inbox info from DB join |
| deploy/migrations/0033_add_contact_avatar_message_sender.sql | Migration script for Contact.AvatarUrl + Message.SenderDisplayName |
| src/frontend/clawbot-web/src/features/agent-hub/ChatMessageThread.tsx | Avatar + name left for contact, owner right |
| src/frontend/clawbot-web/src/features/agent-hub/TabConversation.tsx | Avatar instead of platform icon |
| src/frontend/clawbot-web/src/features/conversations/ConversationList.tsx | Avatar_url instead of fallback |
| src/frontend/clawbot-web/src/features/conversations/ChatPane.tsx | Same fix as ChatMessageThread |
| src/frontend/clawbot-web/src/features/agent-hub/AgentHubLayout.tsx | Avatar in sidebar, pass contactAvatarUrl to ChatMessageThread |
| src/frontend/clawbot-web/src/shared/api/inbox.ts | Types: contactAvatarUrl, senderDisplayName |
| src/frontend/clawbot-web/src/features/inbox/ChannelCard.tsx | Show Zalo name + sale name
### New files

| File | Responsibility |
|---|---|
| `deploy/migrations/0033_add_contact_avatar_message_sender.sql` | ALTER TABLE contacts ADD avatar_url; ALTER TABLE messages ADD sender_display_name |

---

## Phase 1: Backend — PancakePollingService fetch full fields

### Task 1.1: Add PancakeConversation response fields

**Files:**
- Modify: `src/api/Clawbot.Api/Services/PancakePollingService.cs`

- [ ] **- [ ] Step 1: Add `From`, `LastSentBy`, `Customers` record types**

Add to the bottom of `PancakePollingService.cs`:

```csharp
public sealed record PancakeConversation(
    string? Id, string? Type, string? Snippet, int? MessageCount,
    DateTime? UpdatedAt, DateTime? InsertedAt, string? PageId,
    PancakeFrom? From, PancakeLastSentBy? LastSentBy,
    IReadOnlyList<PancakeCustomer>? Customers);

public sealed record PancakeFrom(
    string? Id, string? Name, string? AvatarUrl, bool? IsGroup);

public sealed record PancakeLastSentBy(
    string? Id, string? Name, string? DisplayName,
    string? AvatarUrl, string? AdminName);

public sealed record PancakeCustomer(
    string? Id, string? Name, string? AvatarUrl, string? FbId);
```

Note: Thay thế record `PancakeConversation` cũ (đang thiếu các field này).

- [ ] **Step 2: Run test to verify compilation**

```bash
cd src/api/Clawbot.Api && dotnet build --no-restore 2>&1 | tail -20
```
Expected: build succeeds (warnings OK, errors NOT ok)

- [ ] **Step 3: Commit**

```bash
git add src/api/Clawbot.Api/Services/PancakePollingService.cs
git commit -m "fix: add PancakeFrom/LastSentBy/Customers records to parse API response"
```

### Task 1.2: Parse from.name, avatar, last_sent_by in poll loop

**Files:**
- Modify: `src/api/Clawbot.Api/Services/PancakePollingService.cs` (trong vòng lặp `PollConversationsAsync`)

- [ ] **- [ ] Step 1: Add metadata extraction khi tao ChannelMessage**

Trong `PollConversationsAsync`, sau dòng `var channelMsg = new ChannelMessage(...)`, thêm:

```csharp
var metadata = new Dictionary<string, string>
{
    ["external_message_id"] = latestMsg.Id,
    ["content_type"] = "text",
};

// Bo sung metadata tu Pancake conversation response
if (conv.From != null)
{
    if (!string.IsNullOrEmpty(conv.From.Name))
        metadata["display_name"] = conv.From.Name;
    if (!string.IsNullOrEmpty(conv.From.AvatarUrl))
        metadata["avatar_url"] = conv.From.AvatarUrl;
    if (conv.From.IsGroup == true)
        metadata["is_group"] = "true";
    metadata["from_id"] = conv.From.Id ?? "";
}

// Bo sung sender info tu tin nhan cuoi
if (conv.LastSentBy != null)
{
    var senderName = conv.LastSentBy.DisplayName ?? conv.LastSentBy.Name ?? conv.LastSentBy.AdminName;
    if (!string.IsNullOrEmpty(senderName))
        metadata["sender_name"] = senderName;
    metadata["sender_id"] = conv.LastSentBy.Id ?? "";

    // Neu sender la page owner (id == page_id), luu page_admin_name de update inbox name sau
    if (!string.IsNullOrEmpty(conv.PageId)
        && string.Equals(conv.LastSentBy.Id, conv.PageId, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(senderName))
    {
        metadata["page_admin_name"] = senderName;
    }
}

// Bo sung page_id (da co tu URL, nhung lay tu response cho chinh xac)
if (!string.IsNullOrEmpty(conv.PageId))
    metadata["page_id"] = conv.PageId;

// Bo sung customers (cho group chat — lay customer dau tien lam contact)
if (conv.Customers != null && conv.Customers.Count > 0)
{
    var firstCustomer = conv.Customers[0];
    if (!string.IsNullOrEmpty(firstCustomer.Name) && !metadata.ContainsKey("display_name"))
        metadata["display_name"] = firstCustomer.Name;
    if (!string.IsNullOrEmpty(firstCustomer.AvatarUrl) && !metadata.ContainsKey("avatar_url"))
        metadata["avatar_url"] = firstCustomer.AvatarUrl;
}
```

Sau đó dùng `metadata` thay cho dictionary cũ:

```csharp
var channelMsg = new ChannelMessage(
    Channel: "zalo",
    ExternalThreadId: convId,
    ExternalUserId: latestMsg.From?.Id ?? "unknown",
    Text: snippet,
    SentAt: conv.UpdatedAt.HasValue
        ? new DateTimeOffset(conv.UpdatedAt.Value, TimeSpan.Zero)
        : DateTimeOffset.UtcNow,
    Metadata: metadata);
```

- [ ] **Step 2: Build test**

```bash
cd src/api/Clawbot.Api && dotnet build --no-restore 2>&1 | tail -20
```
Expected: build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/api/Clawbot.Api/Services/PancakePollingService.cs
git commit -m "fix: parse from.name, avatar_url, last_sent_by from Pancake conversation API"
```

---

## Phase 2: Backend — ChannelMessageIngestor handle new fields

### Task 2.1: Add SenderDisplayName to Message entity

**Files:**
- Modify: `src/shared/Clawbot.Domain/Conversations/Message.cs`

- [ ] **- [ ] Step 1: Add property**

```csharp
// Message.cs — trong class Message
public string? SenderDisplayName { get; private set; }
```

- [ ] **Step 2: Update constructor/factory `Create` method signature**

```csharp
public static Message Create(
    Guid conversationId, Guid tenantId, string direction, string senderType,
    string content, string contentType, DateTimeOffset sentAt,
    Guid? senderUserId = null, string? externalMessageId = null,
    string? originalContent = null, string? redactedContent = null,
    string messageType = "text", string? parentPostId = null,
    string? senderDisplayName = null)  // ← THEM
{
    // ... existing code ...
    msg.SenderDisplayName = senderDisplayName;
    return msg;
}
```

- [ ] **Step 3: Build test**

```bash
dotnet build src/shared/Clawbot.Domain/Clawbot.Domain.csproj 2>&1 | tail -10
```
Expected: build succeeds

- [ ] **Step 4: Commit**

```bash
git add src/shared/Clawbot.Domain/Conversations/Message.cs
git commit -m "fix: add SenderDisplayName to Message entity"
```

### Task 2.2: Add AvatarUrl to Contact entity

**Files:**
- Modify: `src/shared/Clawbot.Domain/Contacts/Contact.cs`

- [ ] **- [ ] Step 1: Add property + update method**

```csharp
// Contact.cs
public string? AvatarUrl { get; private set; }

public void UpdateAvatar(string? avatarUrl, DateTimeOffset at)
{
    AvatarUrl = avatarUrl;
    UpdatedAt = at;
}
```

- [ ] **Step 2: Build**

```bash
dotnet build src/shared/Clawbot.Domain/Clawbot.Domain.csproj 2>&1 | tail -10
```

- [ ] **Step 3: Commit**

```bash
git add src/shared/Clawbot.Domain/Contacts/Contact.cs
git commit -m "fix: add AvatarUrl to Contact entity"
```

### Task 2.3: Add UpdateName to Inbox entity

**Files:**
- Modify: src/shared/Clawbot.Domain/Channels/Inbox.cs

- [ ] - [ ] Step 1: Add UpdateName method

`csharp
public void UpdateName(string name, DateTimeOffset at)
{
    Name = name ?? throw new ArgumentNullException(nameof(name));
    UpdatedAt = at;
}
`

- [ ] Step 2: Build + commit

`ash
dotnet build src/shared/Clawbot.Domain/Clawbot.Domain.csproj 2>&1 | tail -10
git add src/shared/Clawbot.Domain/Channels/Inbox.cs
git commit -m "fix(v2): add UpdateName to Inbox"
`



**Files:**
- Create: `deploy/migrations/0033_add_contact_avatar_message_sender.sql`

- [ ] **- [ ] Step 1: Write migration SQL**

```sql
BEGIN TRANSACTION;

-- Add avatar_url to contacts
ALTER TABLE contacts ADD avatar_url NVARCHAR(512) NULL;

-- Add sender_display_name to messages
ALTER TABLE messages ADD sender_display_name NVARCHAR(256) NULL;

COMMIT;
```

- [ ] **Step 2: Commit**

```bash
git add deploy/migrations/0033_add_contact_avatar_message_sender.sql
git commit -m "fix: migration for Contact.AvatarUrl and Message.SenderDisplayName"
```

### Task 2.4: Update ChannelMessageIngestor — avatar, direction, sender_name

**Files:**
- Modify: `src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs`

- [ ] **- [ ] Step 1: Update `UpsertContactAsync` — luu avatar_url**

```csharp
private async Task<Contact?> UpsertContactAsync(Guid tenantId, ChannelMessage message, CancellationToken ct)
{
    if (string.IsNullOrWhiteSpace(message.ExternalUserId)) return null;

    var existing = await _db.ContactExternalIds
        .IgnoreQueryFilters()
        .Where(x => x.Platform == message.Channel && x.ExternalId == message.ExternalUserId)
        .Join(_db.Contacts.IgnoreQueryFilters(), x => x.ContactId, c => c.Id, (x, c) => c)
        .Where(c => c.TenantId == tenantId)
        .FirstOrDefaultAsync(ct).ConfigureAwait(false);

    if (existing is not null)
    {
        // Update display name neu dang sai
        var newName = message.Metadata.TryGetValue("display_name", out var dn) && !string.IsNullOrWhiteSpace(dn) ? dn : null;
        if (newName != null && (existing.DisplayName == message.ExternalUserId || existing.DisplayName.StartsWith("pzl_", StringComparison.Ordinal)))
        {
            existing.UpdateDisplayName(newName);
        }

        // Update avatar neu co
        if (message.Metadata.TryGetValue("avatar_url", out var av) && !string.IsNullOrWhiteSpace(av))
        {
            existing.UpdateAvatar(av, _clock.UtcNow);
        }

        return existing;
    }

    var displayName = message.Metadata.TryGetValue("display_name", out var existingDn) && !string.IsNullOrWhiteSpace(existingDn)
        ? existingDn
        : message.ExternalUserId;

    var avatarUrl = message.Metadata.TryGetValue("avatar_url", out var av2) ? av2 : null;

    var contact = Contact.Create(tenantId, displayName, _clock.UtcNow);
    if (!string.IsNullOrEmpty(avatarUrl))
        contact.UpdateAvatar(avatarUrl, _clock.UtcNow);
    contact.LinkExternalId(message.Channel, message.ExternalUserId, _clock.UtcNow);
    _db.Contacts.Add(contact);

    try
    {
        await _embeddingSync.UpsertContactAsync(contact, tenantId, ct).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        LogEmbeddingUpsertFailed(_logger, ex, contact.Id);
    }

    return contact;
}
```

- [ ] **Step 2: Update `IngestAsync` — direction detection + sender_name**

Trong `IngestAsync`, thay dòng `conversation.AppendMessage(...)` cũ bằng:

```csharp
// Xac dinh direction: so sanh sender_id vs page_id
var senderId = message.Metadata.TryGetValue("sender_id", out var sid) ? sid : "";
var pageId = message.Metadata.TryGetValue("page_id", out var pid) ? pid : "";
var isOwner = !string.IsNullOrEmpty(senderId) && !string.IsNullOrEmpty(pageId)
    && string.Equals(senderId, pageId, StringComparison.Ordinal);

var direction = isOwner ? "out" : "in";
var senderType = isOwner ? "user" : "contact";

// Lay sender display name
var senderDisplayName = message.Metadata.TryGetValue("sender_name", out var sn) ? sn : null;

// PII redaction
var redacted = await _pii.RedactAsync(message.Text, ct).ConfigureAwait(false);

var msg = conversation.AppendMessage(
    direction: direction,
    senderType: senderType,
    content: redacted.RedactedText,
    contentType: message.Metadata.TryGetValue("content_type", out var ct2) ? ct2 : "text",
    sentAt: message.SentAt,
    senderUserId: null,
    externalMessageId: externalMsgId,
    originalContent: message.Text,
    redactedContent: redacted.RedactedText,
    messageType: message.MessageType,
    parentPostId: message.ParentPostId,
    senderDisplayName: senderDisplayName);  // ← THEM
```

- [ ] **Step 3: Update `AppendMessage` call trong Conversation.cs**

```csharp
// Conversation.cs — AppendMessage signature
public Message AppendMessage(string direction, string senderType, string content,
    string contentType, DateTimeOffset sentAt, Guid? senderUserId = null,
    string? externalMessageId = null, string? originalContent = null,
    string? redactedContent = null, string messageType = "text",
    string? parentPostId = null, string? senderDisplayName = null)
{
    var msg = Message.Create(Id, TenantId, direction, senderType, content,
        contentType, sentAt, senderUserId, externalMessageId,
        originalContent, redactedContent, messageType, parentPostId,
        senderDisplayName); // ← TRUYEN
    _messages.Add(msg);
    LastMessageAt = sentAt;
    return msg;
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/Clawbot.sln 2>&1 | tail -20
```

- [ ] **Step 5: Commit**

```bash
git add src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs
git add src/shared/Clawbot.Domain/Conversations/Conversation.cs
git add src/shared/Clawbot.Domain/Conversations/Message.cs
git add src/shared/Clawbot.Domain/Contacts/Contact.cs
git commit -m "fix: update ingestor to handle direction, avatar, sender_name from Pancake metadata"
```

### Task 2.5: Fix ResolveInboxIdAsync — fair routing by page_id

**Files:**
- Modify: `src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs`

- [ ] **- [ ] Step 1: Update `ResolveInboxIdAsync` cleanup**

Giữ nguyên matching theo `page_id` (đã đúng), nhưng xóa bỏ phần "ưu tiên inboxesWithMembers" vì gây ra mất cân bằng:

```csharp
// Xoa doan:
// if (inboxesWithMembers.Count > 0)
//     return matchedInboxes.First(i => inboxesWithMembers.Any(id => id == i.Id)).Id;

// Thay bang: return matchedInboxes[0].Id (da match page_id chinh xac)
```

Note: Nếu page_id match chính xác thì chỉ có 1 inbox match. Nếu có nhiều inbox match (cùng page_id, hiếm), lấy cái đầu tiên.

- [ ] **Step 2: Commit**

```bash
git add src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs
git commit -m "fix: remove inbox member priority bias in ResolveInboxIdAsync, use page_id exact match"
```

---

## Phase 3: Backend — API tra ve avatar + sender name

### Task 3.1: Update DTOs

**Files:**
- Modify: `src/api/Clawbot.Api.Contracts/Inbox/InboxDtos.cs`

- [ ] **- [ ] Step 1: Them `ContactAvatarUrl` vao `ConversationListItemDto`**

```csharp
public sealed record ConversationListItemDto(
    Guid Id, string Platform, string ExternalThreadId, string Status,
    Guid? ContactId, string? ContactDisplayName, string? ContactAvatarUrl,
    Guid? InboxId, string? InboxName, string? InboxAvatarUrl,
    Guid? AssignedTo, DateTimeOffset? LastMessageAt,
    string? LastMessagePreview, byte[]? RowVersion, int UnreadCount);
```

- [ ] **Step 2: Them `SenderDisplayName` vao `MessageDto`**

```csharp
public sealed record MessageDto(
    Guid Id, string Direction, string SenderType,
    Guid? SenderUserId, string Content, string ContentType,
    DateTimeOffset SentAt, string? SenderDisplayName);
```

- [ ] **Step 3: Build**

```bash
dotnet build src/api/Clawbot.Api.Contracts/Clawbot.Api.Contracts.csproj 2>&1 | tail -10
```

- [ ] **Step 4: Commit**

```bash
git add src/api/Clawbot.Api.Contracts/Inbox/InboxDtos.cs
git commit -m "fix: add ContactAvatarUrl, SenderDisplayName to DTOs"
```

### Task 3.3: Update IngestAsync ? update Inbox.Name from page_admin_name

**Files:**
- Modify: src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs

- [ ] Step 1: After UpsertConversationAsync, update inbox name

`csharp
var pageAdminName = message.Metadata.TryGetValue("page_admin_name", out var pan) ? pan : null;
if (!string.IsNullOrEmpty(pageAdminName) && conversation.InboxId.HasValue)
{
    var inbox = await _db.Inboxes.IgnoreQueryFilters()
        .FirstOrDefaultAsync(i => i.Id == conversation.InboxId.Value, ct).ConfigureAwait(false);
    if (inbox != null && inbox.Name != pageAdminName)
        inbox.UpdateName(pageAdminName, _clock.UtcNow);
}
`

Step 2: Build + commit

`
dotnet build src/shared/Clawbot.Infrastructure/Clawbot.Infrastructure.csproj 2>&1 | tail -15
git add src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs
git commit -m "fix(v2): update Inbox.Name from page_admin_name from Pancake"
`

### Task 3.2: Update InboxEndpoints — join avatar, sender_name

**Files:**
- Modify: `src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs`

- [ ] **- [ ] Step 1: `ListAsync` — lay avatar_url tu Contact**

```csharp
// Sau khi lay contactNames, them avatar urls
var contactAvatars = await db.Contacts.AsNoTracking()
    .Where(c => contactIds.Contains(c.Id))
    .ToDictionaryAsync(c => c.Id, c => c.AvatarUrl, ct).ConfigureAwait(false);

// Trong Select khi tao ConversationListItemDto:
var items = rows.Select(r => new ConversationListItemDto(
    r.Id, r.Platform, r.ExternalThreadId, r.Status, r.ContactId,
    r.ContactId.HasValue && contactNames.TryGetValue(r.ContactId.Value, out var n) ? n : null,
    r.ContactId.HasValue && contactAvatars.TryGetValue(r.ContactId.Value, out var a) ? a : null,
    r.AssignedTo, r.LastMessageAt,
    r.LastMessage is null ? null : Preview(r.LastMessage),
    r.RowVersion, 0)).ToList();
```

- [ ] **Step 2: `GetAsync` — lay sender_display_name cho messages**

```csharp
// Trong GetAsync, sau khi lay messages:
var messages = conv.Messages.OrderBy(m => m.SentAt)
    .Select(m => new MessageDto(
        m.Id, m.Direction, m.SenderType, m.SenderUserId,
        m.Content, m.ContentType, m.SentAt,
        m.SenderDisplayName))  // ← THEM
    .ToList();
```

- [ ] **Step 3: Fix compilation (tham so moi cua ConversationListItemDto)**

Tìm tất cả chỗ new ConversationListItemDto(...) trong project, thêm `null` cho ContactAvatarUrl.

Check: `rg "new ConversationListItemDto" --type cs`

- [ ] **Step 4: Build**

```bash
dotnet build src/api/Clawbot.Api/Clawbot.Api.csproj 2>&1 | tail -15
```

- [ ] **Step 5: Commit**

```bash
git add src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs
git commit -m "fix: expose ContactAvatarUrl and SenderDisplayName from API"
```

---

## Phase 4: Frontend — FE types

### Task 4.1: Update inbox.ts types

**Files:**
- Modify: `src/frontend/clawbot-web/src/shared/api/inbox.ts`

- [ ] **- [ ] Step 1: Them `contactAvatarUrl` vao `ConversationListItem`**

```typescript
export interface ConversationListItem {
  readonly id: string;
  readonly platform: string;
  readonly externalThreadId: string;
  readonly status: ConversationStatus;
  readonly contactId: string | null;
  readonly contactDisplayName: string | null;
  readonly contactAvatarUrl: string | null;  // ← THEM
  readonly assignedTo: string | null;
  readonly lastMessageAt: string | null;
  readonly lastMessagePreview: string | null;
  readonly rowVersion: string | null;
  readonly unreadCount: number;
}
```

- [ ] **Step 2: Them `senderDisplayName` vao `InboxMessage`**

```typescript
export interface InboxMessage {
  readonly id: string;
  readonly direction: string;
  readonly senderType: string;
  readonly senderUserId: string | null;
  readonly content: string;
  readonly contentType: string;
  readonly sentAt: string;
  readonly senderDisplayName: string | null;  // ← THEM
}
```

- [ ] **Step 3: Commit**

```bash
git add src/frontend/clawbot-web/src/shared/api/inbox.ts
git commit -m "fix: add contactAvatarUrl, senderDisplayName to FE types"
```

---

## Phase 5: Frontend — ChatMessageThread show avatar + name

### Task 5.1: Rewrite ChatMessageThread

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/agent-hub/ChatMessageThread.tsx`

- [ ] **- [ ] Step 1: Full rewrite component**

```tsx
import type { InboxMessage } from "@/shared/api/inbox";

interface Props {
  readonly messages: readonly InboxMessage[];
  readonly loading: boolean;
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
}

function MessageAvatar({ url, name }: { url?: string | null; name?: string | null }) {
  if (url) {
    return (
      <img
        src={url}
        alt=""
        className="size-8 rounded-full object-cover shrink-0"
        onError={(e) => {
          (e.target as HTMLImageElement).style.display = "none";
          (e.target as HTMLImageElement).nextElementSibling?.classList.remove("hidden");
        }}
      />
    );
  }
  return (
    <div className="size-8 rounded-full bg-surface-variant flex items-center justify-center text-label-sm font-bold text-on-surface-variant shrink-0">
      {(name?.charAt(0) ?? "?").toUpperCase()}
    </div>
  );
}

export default function ChatMessageThread({ messages, loading }: Props) {
  if (loading) {
    return (
      <div className="flex items-center justify-center h-full text-body-md text-on-surface-variant">
        Đang tải tin nhắn...
      </div>
    );
  }
  if (messages.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-body-md text-on-surface-variant">
        Chưa có tin nhắn
      </div>
    );
  }
  return (
    <div className="flex flex-col gap-3 p-4 overflow-y-auto h-full">
      {messages.map((msg) => {
        const isOwner = msg.direction === "out";
        return (
          <div key={msg.id} className={`flex gap-2 ${isOwner ? "justify-end" : "justify-start"}`}>
            {/* Avatar: chi contact moi co */}
            {!isOwner && (
              <MessageAvatar url={/* TODO: lay tu contact */ null} name={msg.senderDisplayName} />
            )}

            {/* Message bubble */}
            <div className="max-w-[70%] flex flex-col">
              {/* Ten nguoi gui: chi contact hien thi */}
              {!isOwner && msg.senderDisplayName && (
                <span className="text-label-xs text-on-surface-variant mb-0.5 ml-1">
                  {msg.senderDisplayName}
                </span>
              )}

              <div
                className={`rounded-2xl px-4 py-2 text-body-md ${
                  isOwner
                    ? "bg-primary text-on-primary rounded-br-md"
                    : "bg-surface-container-high text-secondary rounded-bl-md"
                }`}
              >
                <p className="whitespace-pre-wrap break-words">{msg.content}</p>
                <p className={`mt-1 text-label-xs ${isOwner ? "text-on-primary/70" : "text-on-surface-variant"}`}>
                  {formatTime(msg.sentAt)}
                </p>
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}
```

Note: `MessageAvatar` dang dung `null` cho url vi avatar la cua contact, khong phai cua message. Can fix o Task 5.2.

- [ ] **Step 2: Build FE**

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -20
```

- [ ] **Step 3: Commit**

```bash
git add src/frontend/clawbot-web/src/features/agent-hub/ChatMessageThread.tsx
git commit -m "fix: ChatMessageThread show sender name + avatar for contact, right-align owner"
```

### Task 5.2: Avatar from contact — pass contactAvatarUrl to ChatMessageThread

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/agent-hub/AgentHubLayout.tsx`
- Modify: `src/frontend/clawbot-web/src/features/agent-hub/ChatMessageThread.tsx`

- [ ] **- [ ] Step 1: Pass `contactAvatarUrl` prop to ChatMessageThread**

```tsx
// ChatMessageThread.tsx — them prop
interface Props {
  readonly messages: readonly InboxMessage[];
  readonly loading: boolean;
  readonly contactAvatarUrl?: string | null;
  readonly contactDisplayName?: string | null;
}
```

Su dung contactAvatarUrl trong render:

```tsx
{!isOwner && (
  <MessageAvatar url={contactAvatarUrl} name={msg.senderDisplayName ?? contactDisplayName} />
)}
```

- [ ] **Step 2: Update AgentHubLayout — truyen contactAvatarUrl**

```tsx
<ChatMessageThread
  messages={activeConv?.messages ?? []}
  loading={detailQuery.isLoading}
  contactAvatarUrl={activeConv?.contactAvatarUrl}
  contactDisplayName={activeConv?.contactDisplayName}
/>
```

- [ ] **Step 3: Build FE**

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -20
```

- [ ] **Step 4: Commit**

```bash
git add src/frontend/clawbot-web/src/features/agent-hub/ChatMessageThread.tsx
git add src/frontend/clawbot-web/src/features/agent-hub/AgentHubLayout.tsx
git commit -m "fix: pass contactAvatarUrl to ChatMessageThread for avatar rendering"
```

### Task 5.3: Fix ConversationList — avatar_url thay fallback

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/conversations/ConversationList.tsx`

- [ ] **- [ ] Step 1: Them icon nguoi / group avatar**

```tsx
// Thay cho:
{/* <div className="w-9 h-9 rounded-full bg-slate-200 flex items-center justify-center text-xs font-medium text-slate-600 shrink-0">
  {(conv.contactDisplayName?.charAt(0).toUpperCase() ?? '?')}
</div> */}

// Bang:
{conv.contactAvatarUrl ? (
  <img
    src={conv.contactAvatarUrl}
    alt=""
    className="w-9 h-9 rounded-full object-cover shrink-0"
    onError={(e) => {
      (e.target as HTMLImageElement).style.display = "none";
      ((e.target as HTMLImageElement).nextElementSibling as HTMLElement)?.classList.remove("hidden");
    }}
  />
) : (
  <div className="w-9 h-9 rounded-full bg-slate-200 flex items-center justify-center text-xs font-medium text-slate-600 shrink-0">
    {(conv.contactDisplayName?.charAt(0).toUpperCase() ?? '?')}
  </div>
)}
<FallbackAvatar name={conv.contactDisplayName} />
```

- [ ] **Step 2: Build FE**

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -20
```

- [ ] **Step 3: Commit**

```bash
git add src/frontend/clawbot-web/src/features/conversations/ConversationList.tsx
git commit -m "fix: show contact avatar_url in conversation list"
```

### Task 5.4: Fix ConversationList in AgentHubLayout

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/agent-hub/AgentHubLayout.tsx`

- [ ] **- [ ] Step 1: Them avatar va contact name ben canh status**

Trong conversation list sidebar cua AgentHubLayout, them avatar:

```tsx
<button key={item.id} type="button" onClick={() => selectConversation(item.id)}
  className="w-full text-left px-4 py-3 border-b border-outline/50 hover:bg-surface-container-high transition-colors">
  <div className="flex items-center gap-3">
    {/* Avatar */}
    {item.contactAvatarUrl ? (
      <img src={item.contactAvatarUrl} alt="" className="size-9 rounded-full object-cover shrink-0"
        onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
    ) : (
      <div className="size-9 rounded-full bg-surface-variant flex items-center justify-center text-label-sm font-bold text-on-surface-variant shrink-0">
        {(item.contactDisplayName?.charAt(0) ?? '?').toUpperCase()}
      </div>
    )}
    <div className="flex-1 min-w-0">
      <div className="flex items-center justify-between gap-2">
        <span className="font-semibold text-body-md text-secondary truncate">
          {item.contactDisplayName ?? item.externalThreadId}
        </span>
        <span className="inline-flex items-center rounded-full px-2 py-0.5 text-label-xs">{item.status}</span>
      </div>
      <p className="mt-0.5 text-body-sm text-on-surface-variant truncate">{item.lastMessagePreview ?? ""}</p>
      <p className="mt-0.5 text-label-xs text-on-surface-variant">{item.lastMessageAt ? new Date(item.lastMessageAt).toLocaleString("vi-VN") : ""}</p>
    </div>
  </div>
</button>
```

- [ ] **Step 2: Build FE**

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -20
```

- [ ] **Step 3: Commit**

```bash
git add src/frontend/clawbot-web/src/features/agent-hub/AgentHubLayout.tsx
git commit -m "fix: show avatar in AgentHub sidebar conversation list"
```

### Task 5.5: Fix TabConversation — avatar thay platform icon

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/agent-hub/TabConversation.tsx`

- [ ] **- [ ] Step 1: Them contactAvatarUrl prop**

```tsx
interface TabConversationProps {
  readonly conversation: ConversationListItem;
  readonly isActive: boolean;
  readonly onSelect: () => void;
  readonly onClose: () => void;
}
```

`ConversationListItem` da co `contactAvatarUrl`.

- [ ] **Step 2: Show avatar nho thay platform icon**

```tsx
<span className="flex size-4 items-center justify-center rounded-full shrink-0">
  {conversation.contactAvatarUrl ? (
    <img src={conversation.contactAvatarUrl} alt=""
      className="size-4 rounded-full object-cover"
      onError={(e) => { (e.target as HTMLImageElement).style.display = "none"; }} />
  ) : (
    <span className="text-label-xs font-bold text-on-surface-variant">
      {platformIcon(conversation.platform)}
    </span>
  )}
</span>
```

- [ ] **Step 3: Build FE**

```bash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -20
```

- [ ] **Step 4: Commit**

```bash
git add src/frontend/clawbot-web/src/features/agent-hub/TabConversation.tsx
git commit -m "fix: show contact avatar in tab instead of platform icon"
```

---

## Phase 6: Fix ChatPane (ConversationsPage — neu con dung)

### Task 6.1: Fix ChatPane

**Files:**
- Modify: `src/frontend/clawbot-web/src/features/conversations/ChatPane.tsx`

- [ ] **- [ ] Step 1: Same avatar + name + alignment fix**

```tsx
import type { MessageDto } from './types';

interface Props {
  messages: MessageDto[];
  contactName: string | null;
  contactAvatarUrl?: string | null;
}

function formatTime(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleTimeString('vi', { hour: '2-digit', minute: '2-digit' });
}

export default function ChatPane({ messages, contactName, contactAvatarUrl }: Props) {
  return (
    <div className="flex-1 flex flex-col gap-2 p-4 overflow-y-auto">
      {messages.length === 0 && (
        <div className="flex items-center justify-center h-full text-sm text-slate-400">Chưa có tin nhắn</div>
      )}
      {messages.map(msg => {
        const isOwner = msg.direction === 'out';
        return (
          <div key={msg.id} className={`flex gap-2 ${isOwner ? 'justify-end' : 'justify-start'}`}>
            {!isOwner && contactAvatarUrl && (
              <img src={contactAvatarUrl} alt="" className="size-7 rounded-full object-cover shrink-0"
                onError={(e) => { (e.target as HTMLImageElement).style.display = 'none'; }} />
            )}
            {!isOwner && !contactAvatarUrl && (
              <div className="size-7 rounded-full bg-slate-200 flex items-center justify-center text-xs font-medium text-slate-600 shrink-0">
                {(contactName?.charAt(0) ?? '?').toUpperCase()}
              </div>
            )}
            <div className="max-w-[75%] flex flex-col">
              {!isOwner && contactName && (
                <span className="text-[10px] text-slate-400 mb-0.5 ml-1">{contactName}</span>
              )}
              <div className={'rounded-2xl px-3.5 py-2 text-sm ' + (isOwner ? 'bg-blue-500 text-white rounded-br-sm' : 'bg-slate-100 text-slate-900 rounded-bl-sm')}>
                <p className="whitespace-pre-wrap break-words">{msg.content}</p>
              </div>
              <span className={'text-[10px] text-slate-400 mt-0.5 ' + (isOwner ? 'mr-1 text-right' : 'ml-1')}>
                {formatTime(msg.sentAt)}
              </span>
            </div>
          </div>
        );
      })}
    </div>
  );
}
```

- [ ] **Step 2: Commit**

```bash
git add src/frontend/clawbot-web/src/features/conversations/ChatPane.tsx
git commit -m "fix: ChatPane show avatar + left-align contact, right-align owner"
```

---

---
## Phase 7: Frontend ? ChannelCard show Zalo name

### Task 7.1: Update ChannelCard

**Files:**
- Modify: src/frontend/clawbot-web/src/features/inbox/ChannelCard.tsx

Sau khi backend update Inbox.Name = ten Zalo that, ChannelCard da hien channel.name. Dam bao hien:

- Dong 1: Ten Zalo (VD: "Le Minh Thang")
- Dong 2: Sale phu trach (VD: "Sale A")
- Dong 3: Platform + page_id

- [ ] Step 1: Add platform + page_id subtitle

`	sx
<div className="min-w-0 flex-1">
  <div className="flex items-center gap-2">
    <span className="text-body-md font-semibold truncate">{channel.name}</span>
    {!channel.hasToken && <span className="rounded bg-warning-container px-1.5 py-0.5 text-label-xs text-on-warning-container">No token</span>}
  </div>
  {channel.memberDisplayName && <span className="text-label-sm text-secondary block truncate">{channel.memberDisplayName}</span>}
  <span className="text-label-xs text-tertiary block truncate mt-0.5">{channel.platform} &middot; {channel.externalPageId}</span>
</div>
`

Step 2: Build + commit

`ash
cd src/frontend/clawbot-web && npx tsc --noEmit 2>&1 | tail -20
git add src/frontend/clawbot-web/src/features/inbox/ChannelCard.tsx
git commit -m "fix(v2): ChannelCard show Zalo name + sale name + platform"
`

---
## Phase 8: Migration & verify

### Task 8.1: Run migration

- [ ] - [ ] Step 1: Run SQL migration

`ash
Get-Content deploy/migrations/0033_add_contact_avatar_message_sender.sql | sqlcmd -S localhost -U sa -P "YourPassword123!" -d ClawbotDb
`

- [ ] Step 2: Restart API service

- [ ] Step 3: Verify DB

`ash
sqlcmd -S localhost -U sa -P "YourPassword123!" -d ClawbotDb -Q "SELECT TOP 5 DisplayName, AvatarUrl FROM contacts WHERE AvatarUrl IS NOT NULL"
sqlcmd -S localhost -U sa -P "YourPassword123!" -d ClawbotDb -Q "SELECT TOP 5 Name FROM inboxes"
`

- [ ] Step 4: Commit

`ash
git add deploy/migrations/0033_add_contact_avatar_message_sender.sql
git commit -m "fix(v2): run migration 0033 for avatar_url, sender_display_name"
`
