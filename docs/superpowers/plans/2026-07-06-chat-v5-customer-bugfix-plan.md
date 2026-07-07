# Kế hoạch fix bug khách báo — chat_v5 (2026-07-06)

**Trạng thái:** ✅ Phase 1 (`6606e55`) · ✅ Phase 2 (`a8ea0e3`) · ✅ Phase 3 (`50dae78`) · ✅ Phase 4 — auto-reply theo cờ "AI đang chat" per-conversation.
**Quyết định đã chốt:** Q1 bỏ token per-sale · Q2 mint flow retarget vào `inboxes` · Q3 chưa re-host media · Q4 auto-reply theo cờ per-conversation (default BẬT; tự tắt khi sale gửi tay hoặc escalate).
**Sau deploy:** `deploy/fix_contact_overwrite.sql` tự chạy trong `run-all.bat` (one-shot, guard bằng bảng `data_patches`); token plaintext cũ tự re-encrypt lúc API khởi động. Môi trường không dùng run-all.bat thì chạy file đó qua sqlcmd 1 lần.

**Nhánh làm việc:** `thang/chat_v5` (đã chứa toàn bộ commit của `thang/chat_v4` — hai nhánh cùng trỏ `60c305e`, không cần làm riêng trên v4).

## Yêu cầu khách (nguyên văn, đã nhóm lại)

1. **Cấu hình kênh:** "cần 2 thứ để cấu hình kênh là page_id (để biết kênh nào) vs page accesstoken (quyền truy cập kênh)… mỗi kênh có 1 page_id và 1 accesstoken."
2. **Bug ghi đè:** "nó cứ ghi đè avatar với tên của e cho các đoạn chat, bên trong thì ko sao, còn bên ngoài (list bên trái) thì nó ghi đè luôn. Nhóm này AI agent nó ghi đè luôn avatar với name chủ Zalo vô… với PancakePollingService ấy, nó cứ bị ghi đè dữ liệu."
   - Group chat: "db có 1 trường lưu avatar thôi nên ai cũng chung avatar hết."
3. **RabbitMQ:** "em mới update thêm cái lấy được ảnh, với file nội dung các thứ, mà e quên chưa để BE chạy qua RabbitMQ."
4. **Tích hợp agent:** "xong còn tích hợp agent nữa" — feature, không phải bug; tách phase riêng.

---

## Hiện trạng & root cause

### Bug 2 — Ghi đè name/avatar hội thoại (ưu tiên cao nhất)

Danh sách bên trái đọc `Contact.DisplayName` + `Contact.AvatarUrl` của contact gắn với conversation (`InboxEndpoints.ListAsync`, `InboxEndpoints.cs:90-107`). Contact này bị polling ghi đè bởi **người gửi tin cuối cùng** thay vì giữ thông tin **đối tác hội thoại** (khách / nhóm):

- `PancakePollingService.cs:184-195`: set `metadata["display_name"] = msg.From.Name` — tức là tên **người gửi từng tin** (kể cả admin/AI), không phải tên khách/nhóm.
- `ChannelMessageIngestor.UpsertConversationAsync` (`ChannelMessageIngestor.cs:130-139`) dùng chính `display_name`/`sender_avatar_url` đó để upsert contact của hội thoại. Có strip cho outbound nhưng **chỉ khi flag `is_owner`** — flag này chỉ được set khi `msg.From.AdminId != null || msg.From.IsAutomated == true` (`PancakePollingService.cs:221-222`). Tin AI/OA gửi qua API thường có `From.Id == pageId` nhưng `AdminId == null` → **không strip** → contact hội thoại nhận tên + avatar chủ OA. Đây đúng hiện tượng "AI agent ghi đè name chủ Zalo vô".
  - Ingestor đã có logic owner chuẩn hơn (`sender_id == page_id`, `ChannelMessageIngestor.cs:67-73`) nhưng chạy **sau** khi upsert contact — quá muộn.
- Avatar: `UpsertContactAsync` gọi `UpdateAvatar` **mọi message** nếu avatar không phải default (`ChannelMessageIngestor.cs:245-248`) → group chat: mỗi thành viên nhắn là avatar hội thoại đổi theo người đó ("ai cũng chung avatar").
- Name: chỉ đổi khi đang là placeholder (`pzl_*`), nhưng contact **tạo mới** từ echo outbound sẽ sinh ra với tên admin ngay từ đầu (`ChannelMessageIngestor.cs:252-255`) → kẹt tên sai vĩnh viễn, placeholder-check không bao giờ sửa lại được.

Trong khung chat đã hiển thị đúng vì message đã có `SenderDisplayName`/`SenderAvatarUrl` per-message (commit `16f2285`, `3188ac4`) — **không cần thêm cột mới**; chỉ cần sửa nguồn dữ liệu cho contact hội thoại.

### Yêu cầu 1 — Token per-kênh

Hiện có **4 nguồn token song song**, ưu tiên sai so với yêu cầu "mỗi kênh 1 token":

| Nguồn | Ghi bởi | Đọc bởi | Vấn đề |
|---|---|---|---|
| `inboxes.encrypted_access_token` | ChannelManagementPage → `AdminInboxEndpoints.CreateInboxAsync` | Polling inbound + `PancakePageTokenResolver` (outbound fallback) | **Lưu plaintext** (`AdminInboxEndpoints.cs:215` không encrypt), tên cột nói dối; không có endpoint update token sau khi tạo |
| `users.pancake_access_token_encrypted` | AdminUserModal ("Access token Pancake của nhân viên sale") | `SendOutboundAsync` ưu tiên số 1 (`InboxEndpoints.cs:292-297`) | Trái yêu cầu per-kênh; mỗi sale 1 token gây lệch quyền gửi |
| `pancake_pages.page_access_token_encrypted` | Mint flow SPEC-16 M-5 (`PancakePageTokenService`) | **Không ai đọc** sau commit `60c305e` (resolver đã chuyển sang đọc `inboxes`) | Dead path — mint xong vứt |
| `DemoRuntimeConfig` env token | env var | Polling fallback demo | Giữ cho demo, OK |

Gap FE: edit modal kênh có ô "Page Access Token" nhưng `saveMutation` chỉ gửi member — **token nhập vào bị drop im lặng** (`ChannelManagementPage.tsx:67-78,385-395`).

### Yêu cầu 3 — RabbitMQ

Polling gọi `IChannelMessageIngestor.IngestAsync` **trực tiếp trong vòng poll** (`PancakePollingService.cs:268`). Hạ tầng MassTransit + RabbitMQ + EF Outbox đã dựng sẵn với 3 consumer (`DependencyInjection.cs:89-110`) — chỉ thiếu đường cho inbound chat message.

---

## Kế hoạch fix

### Phase 1 — Bug ghi đè name/avatar (P1, làm trước)

**1a. Tách metadata conversation-level khỏi sender-level** — `PancakePollingService.cs`

- Thêm keys mới: `conversation_name`, `conversation_avatar_url`, lấy từ **`conv.From`** (group: tên + avatar nhóm; 1-1: khách) và `conv.Customers[0]` cho 1-1 khi có.
- `sender_name`/`sender_avatar_url` giữ nguyên, **chỉ** phục vụ per-message render; bỏ việc set `display_name` từ `msg.From`.
- Xác định owner sớm ngay tại polling: `is_owner = (msg.From.Id == pageId) || AdminId != null || IsAutomated` — đồng bộ với logic ingestor.

**1b. Sửa rule upsert contact hội thoại** — `ChannelMessageIngestor.cs`

- `UpsertConversationAsync`: contact hội thoại chỉ nhận name/avatar từ `conversation_name`/`conversation_avatar_url` (nguồn = đối tác hội thoại, authoritative từ Pancake), **không bao giờ** từ `sender_*`. Cho phép sync lại mỗi lần poll → contact hỏng sẽ **tự heal** khi có tin mới.
- Sender-contact update (`IngestAsync` dòng 47-48): skip hoàn toàn khi `is_owner` (không tạo/không sửa contact từ tin của admin/AI).
- Tính `isOwner` một lần trước cả hai bước upsert (dời khối dòng 67-73 lên đầu).

**1c. Data repair contact đã hỏng** — 1 file SQL mới trong `deploy/migrations/`

- Reset `display_name` về external id (placeholder) + `avatar_url = NULL` cho các contact bị nhiễm: contact có external id dạng `pzl_g_*` (group) hoặc contact mà tên trùng tên admin/page. Sau reset, cơ chế self-heal ở 1b tự điền lại tên/avatar đúng ở lần poll kế tiếp.
- Theo quy ước migration của repo: không `GO`, mỗi file 1 batch; đồng thời thêm vào repair block của `run-all.bat` nếu đụng schema (đợt này chỉ UPDATE data, không đổi schema).

**Test:** mở rộng `ChannelMessageIngestorTests` — case: (i) echo outbound của AI (`From.Id == pageId`, AdminId null) không đổi contact; (ii) tin thành viên group không đổi avatar hội thoại; (iii) `conversation_name` sync đúng tên nhóm; (iv) contact hỏng tự heal. `PancakePollingServiceTests`: metadata tách đúng keys.

**Ước lượng:** ~0.5–1 ngày. Rủi ro thấp — không đổi schema.
**Trade-off chấp nhận:** name/avatar contact luôn sync theo Pancake; nếu sau này có tính năng rename contact thủ công trong CRM thì cần thêm flag "manual override" (chưa cần bây giờ).

### Phase 2 — Chuẩn hoá token per-kênh (P1b)

Chốt mô hình: **`inboxes` là nguồn token duy nhất cho vận hành kênh** (đúng yêu cầu "mỗi kênh 1 page_id + 1 token").

- **2a. Encrypt nhất quán** — `AdminInboxEndpoints.CreateInboxAsync` encrypt bằng `IEncryptor` trước khi `SetAccessToken`; `PancakePollingService` decrypt khi đọc (dùng chung helper với `PancakePageTokenResolver`, giữ fallback plaintext 1 release cho row cũ rồi bỏ).
- **2b. Endpoint update token**: `PUT /api/admin/inboxes/{id}` nhận `{ pageAccessToken?, isActive? }` (permission `admin:inboxes` sẵn có). FE: nối `tokenInput` trong edit modal vào endpoint này (hết drop im lặng).
- **2c. Outbound bỏ ưu tiên user-token**: `SendOutboundAsync` thôi đọc `users.pancake_access_token_encrypted`, truyền `accessToken: null` để adapter tự resolve token kênh qua `PancakePageTokenResolver` (đường này đã chạy). 
- **2d. Dọn UI**: bỏ field "Access token Pancake của nhân viên sale" khỏi `AdminUserModal.tsx` (+ props liên quan trong `admin.ts`, `AdminUsersEndpoints` giữ cột DB để backward-compat, chỉ ngừng nhận input mới).
- **2e. Mint flow M-5**: retarget `PancakePageTokenService.MintAndStoreAsync`/`StorePageTokenDirectAsync` ghi vào `inboxes` (tạo inbox nếu chưa có, set token encrypted) thay vì `pancake_pages` — giữ trải nghiệm "dán user token → auto kết nối nhiều trang", hết dead path.
- **2f. One-off encrypt migration**: hosted service chạy 1 lần lúc startup — quét `inboxes.encrypted_access_token`, row nào decrypt fail (= plaintext) thì encrypt lại. (Không làm bằng SQL được vì AES key nằm ở app.)

**Test:** create/update inbox → token đọc lại được cả polling lẫn outbound; user không có token cá nhân vẫn gửi được qua token kênh; row plaintext cũ tự encrypt sau restart.

**Ước lượng:** ~1 ngày.
**Cần anh chốt trước khi làm:**
- Q1: Bỏ hẳn token per-sale (khuyến nghị: bỏ — đúng lời khách) hay giữ làm override đặc biệt?
- Q2: Mint flow retarget vào `inboxes` (khuyến nghị) hay bỏ luôn, chỉ nhập token tay từng kênh?

### Phase 3 — Inbound qua RabbitMQ (P2)

- **3a.** Contract mới `Clawbot.SharedKernel` (hoặc `Infrastructure.Messaging`): `record ChannelInboundMessageReceived(Guid TenantId, ChannelMessage Message)`.
- **3b.** `PancakePollingService`: thay `ingestor.IngestAsync(...)` bằng `IPublishEndpoint.Publish(new ChannelInboundMessageReceived(...))`. Giữ nguyên `ProcessedMessages` marking ở polling (chống re-publish mỗi vòng poll).
- **3c.** Consumer mới `ChannelInboundMessageConsumer` gọi `IChannelMessageIngestor` — idempotent sẵn nhờ dedup `external_message_id` (`ChannelMessageIngestor.IsDuplicateAsync`), nên at-least-once của RabbitMQ an toàn.
- **3d.** Ordering: cấu hình endpoint consumer `ConcurrentMessageLimit = 1` giai đoạn đầu — đơn giản, đủ cho tải hiện tại; khi cần scale thì partition theo conversation.
- **3e.** (Cùng pattern, tuỳ chọn) `WebhookEndpoints` cũng publish thay vì ingest trực tiếp — làm nếu còn thời gian, không bắt buộc đợt này.

Media/file: attachment hiện hotlink URL từ `pages.fm` (`content_url` có thể hết hạn). Đợt này **chưa** download/re-host — ghi nhận là bước sau nếu khách cần lưu trữ lâu dài (Q3).

**Test:** integration — publish message → consumer ingest → conversation/message xuất hiện; duplicate delivery không tạo message đôi; RabbitMQ down → polling vẫn không mất tin (outbox/retry).

**Ước lượng:** ~0.5–1 ngày.

### Phase 4 — Tích hợp agent (P3, ngoài scope bug-fix)

Feature riêng: nối AI agent auto-reply vào inbox (đã có nền `tung/dev/feat/inbox-agents`, gRPC AgentService, SaleAssist). Cần khách chốt scope: auto-reply toàn bộ hay chỉ khi "AI đang chat" bật per-conversation. Lên plan riêng sau khi Phase 1–3 xong.

---

## Thứ tự thực hiện đề xuất

1. Phase 1 (bug ghi đè) — khách đang thấy data sai hằng ngày, sửa trước.
2. Phase 2 (token per-kênh) — đang chặn cấu hình kênh mới đúng cách + vá lỗ plaintext token.
3. Phase 3 (RabbitMQ) — kiến trúc, không đổi hành vi nhìn thấy được.
4. Phase 4 (agent) — plan riêng.

## Câu hỏi cần chốt (với anh / khách)

| # | Câu hỏi | Khuyến nghị |
|---|---|---|
| Q1 | Bỏ hẳn token per-sale ở AdminUserModal? | Bỏ — outbound dùng token kênh |
| Q2 | Mint flow M-5: retarget vào `inboxes` hay bỏ? | Retarget — đỡ copy token tay từng page |
| Q3 | Media có cần download & re-host (URL Pancake có thể hết hạn)? | Để sau, đợt này chỉ đưa pipeline qua MQ |
| Q4 | Scope tích hợp agent? | Plan riêng Phase 4 |
