# Chat Polling: Rich Content + Per-Sender Avatar

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Fix polling service to parse Pancake attachments (photo/sticker/document/call) and use per-message sender avatar instead of group avatar.

**Architecture:** Expand PancakeMessage models to include attachments and richer sender info. Parse attachments in PollingService to set content_type and attachment URL. Add AttachmentUrl to Message entity and DTO. Update frontend MessageBubble to render rich content.

**Tech Stack:** C# / .NET 8, Entity Framework Core, TypeScript / React, Tailwind CSS

---

## File Structure

| File | Responsibility |
|---|---|
| src/api/Clawbot.Api/Services/PancakePollingService.cs | Parse attachments, per-message sender, fix ExternalUserId |
| src/shared/Clawbot.Domain/Conversations/Message.cs | Add AttachmentUrl property |
| src/shared/Clawbot.Domain/Conversations/Conversation.cs | Pass attachmentUrl through AppendMessage |
| src/shared/Clawbot.Infrastructure/Persistence/Configurations/DomainModelConfigurations.cs | Configure AttachmentUrl column |
| src/shared/Clawbot.SharedKernel/Channels/ChannelMessage.cs | Add AttachmentUrl to record |
| src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs | Pass attachment_url from metadata |
| src/api/Clawbot.Api.Contracts/Inbox/InboxDtos.cs | Add AttachmentUrl to MessageDto |
| src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs | Pass AttachmentUrl in query |
| src/api/Clawbot.Api/Program.cs | CORS from config |
| src/frontend/clawbot-web/src/shared/api/inbox.ts | Add ttachmentUrl to InboxMessage |
| src/frontend/clawbot-web/src/features/conversations/ConversationsPage.tsx | Render photo/sticker/document/call in MessageBubble |
| src/frontend/clawbot-web/src/features/conversations/useInboxRealtime.ts | Pass attachmentUrl in realtime |

---

### Task 1: Expand PancakeMessage models

**Files:**
Modify: src/api/Clawbot.Api/Services/PancakePollingService.cs (records at bottom)

- [ ] **Step 1:** Expand PancakeMessageSender to include Name and AvatarUrl:

`csharp
public sealed record PancakeMessageSender(
    string? Id,
    string? Name,
    string? AvatarUrl,
    bool? IsGroup,
    string? AdminId,
    bool? IsAutomated);
`

- [ ] **Step 2:** Expand PancakeMessage to include Message and Attachments:

`csharp
public sealed record PancakeMessage(
    string? Id,
    string? Message,
    PancakeMessageSender? From,
    IReadOnlyList<PancakeAttachment>? Attachments);
`

- [ ] **Step 3:** Add PancakeAttachment and PancakeImageData records:

`csharp
public sealed record PancakeAttachment(
    string? Type,
    string? Url,
    string? OriginUrl,
    string? Name,
    string? MimeType,
    PancakeImageData? ImageData);

public sealed record PancakeImageData(int? Width, int? Height);
`

- [ ] **Step 4:** Verify build: dotnet build src/api/Clawbot.Api/Clawbot.Api.csproj --no-restore

- [ ] **Step 5:** Commit

---

### Task 2: Fix ExternalUserId and parse attachments in PollingService

**Files:**
Modify: src/api/Clawbot.Api/Services/PancakePollingService.cs (PollPageAsync method, ~line 170)

- [ ] **Step 1:** Replace the metadata + channelMsg block with:

`csharp
                var metadata = new Dictionary<string, string>
                {
                    ["external_message_id"] = latestMsg.Id,
                    ["content_type"] = "text",
                };

                // Per-message sender info
                if (latestMsg.From != null)
                {
                    if (!string.IsNullOrEmpty(latestMsg.From.Name)) metadata["sender_name"] = latestMsg.From.Name;
                    if (!string.IsNullOrEmpty(latestMsg.From.AvatarUrl)) metadata["sender_avatar_url"] = latestMsg.From.AvatarUrl;
                    metadata["sender_id"] = latestMsg.From.Id ?? "";
                }

                if (conv.From != null)
                {
                    if (!string.IsNullOrEmpty(conv.From.Name) && !metadata.ContainsKey("sender_name")) metadata["sender_name"] = conv.From.Name;
                    if (!string.IsNullOrEmpty(conv.From.AvatarUrl) && !metadata.ContainsKey("sender_avatar_url")) metadata["sender_avatar_url"] = conv.From.AvatarUrl;
                    if (conv.From.IsGroup == true) metadata["is_group"] = "true";
                    metadata["from_id"] = conv.From.Id ?? "";
                }
                if (conv.LastSentBy != null)
                {
                    metadata["sender_id"] = conv.LastSentBy.Id ?? "";
                }
                if (!string.IsNullOrEmpty(conv.PageId)) metadata["page_id"] = conv.PageId;

                // Parse attachments for rich content
                string text = snippet;
                if (latestMsg.Attachments != null && latestMsg.Attachments.Count > 0)
                {
                    var att = latestMsg.Attachments[0];
                    switch (att.Type)
                    {
                        case "photo":
                            metadata["content_type"] = "photo";
                            text = att.Url ?? "";
                            break;
                        case "sticker":
                            metadata["content_type"] = "sticker";
                            text = att.Url ?? "";
                            break;
                        case "document":
                            metadata["content_type"] = "document";
                            text = att.Name ?? "Tai lieu";
                            if (!string.IsNullOrEmpty(att.Url)) metadata["attachment_url"] = att.Url;
                            break;
                        case "pzl_chat_recommended":
                            metadata["content_type"] = "call_missed";
                            text = "Cuoc goi nhlo";
                            break;
                    }
                }

                var channelMsg = new Clawbot.SharedKernel.Channels.ChannelMessage(
                    Channel: "zalo", ExternalThreadId: convId,
                    ExternalUserId: conv.From?.Id ?? latestMsg.From?.Id ?? "unknown", Text: text,
                    SentAt: conv.UpdatedAt.HasValue ? new DateTimeOffset(conv.UpdatedAt.Value, TimeSpan.Zero) : DateTimeOffset.UtcNow,
                    Metadata: metadata);
                await ingestor.IngestAsync(tenantId, channelMsg, ct);
`

- [ ] **Step 2:** Verify build
- [ ] **Step 3:** Commit

---

### Task 3: Add AttachmentUrl to Message entity + EF config

**Files:**
Modify: src/shared/Clawbot.Domain/Conversations/Message.cs
Modify: src/shared/Clawbot.Infrastructure/Persistence/Configurations/DomainModelConfigurations.cs

- [ ] **Step 1:** Add public string? AttachmentUrl { get; private set; } to Message.cs after SenderAvatarUrl
- [ ] **Step 2:** Add string? attachmentUrl = null parameter to Create method and set in initializer
- [ ] **Step 3:** Add uilder.Property(x => x.AttachmentUrl).HasMaxLength(2048); to MessageConfiguration
- [ ] **Step 4:** Verify build
- [ ] **Step 5:** Commit

---

### Task 4: Thread attachment URL through Conversation, Ingestor, DTO

**Files:**
Modify: src/shared/Clawbot.Domain/Conversations/Conversation.cs
Modify: src/shared/Clawbot.SharedKernel/Channels/ChannelMessage.cs
Modify: src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs
Modify: src/api/Clawbot.Api.Contracts/Inbox/InboxDtos.cs
Modify: src/api/Clawbot.Api/Endpoints/InboxEndpoints.cs

- [ ] **Step 1:** Add string? AttachmentUrl = null to ChannelMessage record
- [ ] **Step 2:** Add string? attachmentUrl = null to Conversation.AppendMessage and pass to Message.Create
- [ ] **Step 3:** In IngestAsync, extract ttachmentUrl from metadata and pass to AppendMessage
- [ ] **Step 4:** Add string? AttachmentUrl = null to MessageDto
- [ ] **Step 5:** Pass m.AttachmentUrl in InboxEndpoints GetAsync projection
- [ ] **Step 6:** Verify build
- [ ] **Step 7:** Commit

---

### Task 5: Frontend types + MessageBubble rich content

**Files:**
Modify: src/frontend/clawbot-web/src/shared/api/inbox.ts
Modify: src/frontend/clawbot-web/src/features/conversations/useInboxRealtime.ts
Modify: src/frontend/clawbot-web/src/features/conversations/ConversationsPage.tsx

- [ ] **Step 1:** Add eadonly attachmentUrl: string | null; to InboxMessage interface
- [ ] **Step 2:** Add eadonly attachmentUrl?: string | null; to InboxMessageEvent
- [ ] **Step 3:** Add ttachmentUrl: evt.attachmentUrl ?? null in 	oMessage() in useInboxRealtime.ts
- [ ] **Step 4:** Replace content <p> in MessageBubble with rich content rendering (photo/sticker/document/call_missed)
- [ ] **Step 5:** Verify TypeScript: cd src/frontend/clawbot-web && npx tsc --noEmit
- [ ] **Step 6:** Commit

---

### Task 6: Fix CORS config + ExternalThreadId alignment

**Files:**
Modify: src/api/Clawbot.Api/Program.cs
Modify: src/api/Clawbot.Api/Services/PancakePollingService.cs
Modify: src/api/Clawbot.Api/appsettings.json

- [ ] **Step 1:** Read CORS origins from Cors:Origins config section in Program.cs
- [ ] **Step 2:** Add "Cors": { "Origins": ["http://localhost:15876"] } to appsettings.json
- [ ] **Step 3:** Align ExternalThreadId: string.IsNullOrEmpty(conv.PageId) ? convId : $"{conv.PageId}:{convId}"
- [ ] **Step 4:** Verify build
- [ ] **Step 5:** Commit
