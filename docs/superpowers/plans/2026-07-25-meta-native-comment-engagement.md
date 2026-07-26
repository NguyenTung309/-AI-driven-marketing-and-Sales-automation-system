# Plan: Fallback Meta Graph cho đồng bộ + tự trả lời comment FB/IG khi chưa nối Pancake

- Ngày: 2026-07-25
- Trạng thái: P0 + P1 + P2a/P2b + P3 ĐÃ TRIỂN KHAI + ĐÃ VERIFY BUILD/TEST/LINT; CHƯA CHẠY MIGRATION TRÊN DB THẬT
- Cập nhật: 2026-07-26 — cả bốn phase hoàn tất trên branch hiện tại. Kết quả từng phase: §4.6 (P0), §5.5 (P1), §6.5 (P2), §6.6 (P3). Migration static guard đã được sửa để hiểu lịch sử migration song song trước 0087 và hiện pass.
- Nguồn: câu hỏi "chưa nối Pancake thì fallback Graph API được không? bài post chưa thấy đồng bộ comment/like, chưa tự động reply comment"
- Quyết định của chủ sản phẩm (2026-07-25):
  1. **Pancake chưa kết nối thì fallback Graph API** — làm, không cần cân nhắc thêm. App Review chỉ là ràng buộc vận hành (§7.1), không đổi thiết kế.
  2. **Rep đủ mọi comment**, không giới hạn 1 rep/khách/post như hiện tại — kèm hệ chống spam nhiều tầng (§6.4), là phần bắt buộc ship cùng.
- Ghi chú: plan này **đảo ngược** khuyến nghị cũ trong memory `facebook-publish-vs-engagement-split` ("xây Meta-native comment/reply = lặp lại Pancake, tránh"). Lý do đảo: khuyến nghị đó đúng khi tenant *có thể* nối Pancake. Với tenant chỉ có Meta OAuth thì toàn bộ đường comment đang chết và không có cách bật, nên fallback là bắt buộc chứ không phải trùng lặp.

---

## 1. Trả lời ngắn hai câu hỏi

**Fallback Graph API được không?** Được. Graph API có đủ 4 primitive cần thiết (đọc comment, đọc like/comment count, rep công khai, DM riêng từ comment). Rào cản không phải kỹ thuật mà là **quyền Meta App Review** — xem §7.1, đây là rủi ro lớn nhất của plan này.

**Vì sao chưa thấy đồng bộ like/comment và chưa tự reply?** Hai nguyên nhân khác nhau, đừng gộp:

| Việc | Code có sẵn? | Vì sao chưa thấy |
|---|---|---|
| Đếm like/comment trên post | Có, đủ end-to-end | Job chạy nhưng có **3 nhánh `continue` im lặng không log** — không biết nó bỏ post nào và vì sao. Ngoài ra Instagram bị loại hẳn bởi filter `Platform == "facebook"`. |
| Ingest comment vào inbox | Chỉ đường Pancake | Không nối Pancake → **không có dòng comment nào trong DB** → job reply quét ra 0 ứng viên, không làm gì cả |
| Rep comment + DM riêng | Chỉ adapter Pancake | Kể cả có comment trong DB thì adapter cũng ném `Pancake config not resolved` |

Điểm quan trọng dễ bỏ sót: **muốn tự reply comment thì phải giải cả 2 bài toán ingest và send.** Chỉ viết adapter gửi là vô nghĩa vì không có gì để trả lời.

---

## 2. Hiện trạng đã xác minh (đọc code, không suy đoán)

### 2.1 Đăng bài — Meta Graph, đang hoạt động

`GraphSocialPublisher.PublishFacebookWithMetaAsync` → `MetaGraphClient.PublishPageAsync`
([MetaGraphClient.cs:325-344](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaGraphClient.cs#L325-L344)).
Trả `MetaPublishedPost(PostId, Permalink)`, permalink = `https://www.facebook.com/{postId}`.

`PublishResult` **đã mang sẵn `ExternalPostId`** ([GraphSocialPublisher.cs:200](src/shared/Clawbot.Infrastructure/Content/Publishing/GraphSocialPublisher.cs#L200)) — nhưng `ContentSchedule` không có cột để lưu, nên bị bỏ đi. Đây là gốc của toàn bộ sự mong manh ở §2.2.

### 2.2 Đếm like/comment — code đủ, quan sát bằng không

`MetaEngagementSyncJob` cron `*/15` ([HangfireModule.cs:212-216](src/shared/Clawbot.Infrastructure/Jobs/HangfireModule.cs#L212-L216)), DTO + FE badge đều đã wire (`ContentDtos.cs:169`, `ContentWorkspacePage.tsx:1232`), migration `0059_content_schedule_engagement.sql` có sẵn.

Ba nhánh bỏ post **không log một dòng nào**:

| Dòng | Điều kiện | Hệ quả |
|---|---|---|
| [MetaEngagementSyncJob.cs:38-39](src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L38-L39) | `ExtractPostId(PostUrl) is null` | bỏ im lặng |
| [:47-48](src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L47-L48) | `ResolvePageAsync` trả null | bỏ im lặng |
| [:26](src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L26) | `Platform != "facebook"` | Instagram không bao giờ được sync |

`ExtractPostId` yêu cầu đoạn tail của URL **có dấu `_`** ([:74-82](src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L74-L82)). Permalink Instagram là `https://www.instagram.com/p/{shortcode}/` → không có `_` → kể cả bỏ filter platform thì vẫn bị loại. Post ảnh FB nếu Graph chỉ trả `id` (photo id) mà không trả `post_id` cũng rơi vào đây.

Cạm bẫy thứ hai: `ResolvePageAsync` lọc theo `CanPublish` = page phải có task **`CREATE_CONTENT`** ([MetaIntegrationService.cs:636-637](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaIntegrationService.cs#L636-L637)). Quyền cần cho moderation comment là **`MODERATE`**, là task khác. Page chỉ có MODERATE sẽ trả null.

### 2.3 Ingest comment — chỉ có một đường, đi qua Pancake

`PancakePollingService` map `conv.Type == "COMMENT"` + `post_id` ([PancakePollingService.cs:312-315](src/api/Clawbot.Api/Services/PancakePollingService.cs#L312-L315)) → publish `ChannelInboundMessageReceived` → `ChannelMessageIngestor.IngestAsync`.

`ChannelInboundMessageConsumer` **chủ động bỏ qua** comment cho chat auto-reply ([:51-52](src/shared/Clawbot.Infrastructure/Messaging/ChannelInboundMessageConsumer.cs#L51-L52)) và nhường cho `CommentAutoReplyJob`.

Không nối Pancake → bảng `messages` không có dòng nào `message_type = 'comment'` → `RunScanAsync` trả list rỗng, job kết thúc sau 1 query.

### 2.4 Gửi reply — adapter duy nhất là Pancake, và DI không cho phép vắng mặt

`ICommentChannelAdapter` ([ICommentChannelAdapter.cs](src/shared/Clawbot.SharedKernel/Channels/ICommentChannelAdapter.cs)) — abstraction đã đúng, chỉ có 1 implementation.

DI đăng ký **vô điều kiện** bằng cách cast `IChannelAdapter`:

```csharp
// DependencyInjection.cs:199-201
services.AddScoped<ICommentChannelAdapter>(sp =>
    sp.GetRequiredService<IChannelAdapter>() as ICommentChannelAdapter
    ?? throw new InvalidOperationException("ICommentChannelAdapter not available"));
```

Nên nhánh `if (commentAdapter is null) → skip` trong `CommentAutoReplyJob` ([:55-59](src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs#L55-L59)) thực tế **không bao giờ chạy** khi Infrastructure DI được load. Lỗi thật xảy ra sâu hơn ở `SendPayloadAsync` → `InvalidOperationException("Pancake config not resolved for specified tenant.")`, bị bắt bởi `catch` ở `RunScanAsync` ([:44-47](src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs#L44-L47)) và chỉ log warning 9206. Đây là lý do triệu chứng hoàn toàn im lặng.

### 2.5 Hạ tầng có thể tái dùng (không phải viết lại từ đầu)

Đây là điều làm plan này rẻ hơn tưởng tượng:

| Có sẵn | Ở đâu | Dùng cho |
|---|---|---|
| Webhook Meta có HMAC `X-Hub-Signature-256` + verify token + resolve multi-tenant theo app id | [MetaBusinessIntegrationWebhookEndpoints.cs](src/api/Clawbot.Api/Endpoints/MetaBusinessIntegrationWebhookEndpoints.cs) | copy khung cho webhook `object: "page"` |
| `IMetaGraphClient.GetAsync` / `PostAsync` generic | [MetaGraphClient.cs:136-147](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaGraphClient.cs#L136-L147) | mọi call comment, không cần thêm HTTP client |
| `IChannelMessageIngestor.IngestAsync` + dedup `external_message_id` | [ChannelMessageIngestor.cs:39](src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs#L39) | ingest comment Meta, at-least-once an toàn |
| `ChannelMessage` đã có `MessageType` + `ParentPostId` | [ChannelMessage.cs:13-14](src/shared/Clawbot.SharedKernel/Channels/ChannelMessage.cs#L13-L14) | không cần đổi contract |
| Toàn bộ `CommentAutoReplyJob` (intent gate, dedup, tôn trọng handover, notification) | [CommentAutoReplyJob.cs](src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs) | giữ nguyên, chỉ đổi adapter bên dưới |
| `MetaAsset.Tasks` đã lưu JSON tasks của page | `MetaIntegrationService.cs:548` | kiểm tra `MODERATE` không cần gọi Graph |

---

## 3. Hai quyết định kiến trúc cần chốt trước khi code

### QĐ1 — Pancake vẫn là đường chính, Meta là fallback (đề xuất chọn)

Không đổi Meta thành đường chính. Lý do: Pancake còn mang auto-IB, inbox tooling, và đã qua App Review của họ. Meta-native chỉ bật khi tenant **không** có Pancake inbox.

Cụ thể: thay `AddScoped<ICommentChannelAdapter>` bằng một resolver chọn theo tenant tại thời điểm gửi:

```
PancakeCommentAdapter   nếu inbox của page có encrypted_access_token
MetaCommentAdapter      nếu không, mà MetaConnection usable + page có task MODERATE
null (skip có log)      nếu không có cả hai   ← nhánh này phải thật sự trả null, khác hiện tại
```

Phương án bị loại: Meta-native làm đường chính. Phải tự dựng backfill, tự lo IG webhook riêng, mất auto-IB, và bắt mọi tenant đang chạy Pancake phải qua App Review — đắt hơn nhiều mà không thêm giá trị.

### QĐ2 — CHỐT: rep đủ mọi comment, kèm hệ chống spam nhiều tầng

Hành vi hiện tại (đọc kỹ mới thấy):

```csharp
// CommentAutoReplyJob.cs:66-73
var alreadyReplied = await db.Messages... .AnyAsync(m =>
    m.ConversationId == inbound.ConversationId   // <- conversation, KHÔNG phải post
    && m.Direction == "out" && m.SenderType == "bot"
    && m.MessageType == "comment"
    && m.ParentPostId == inbound.ParentPostId, ct);
```

`ConversationId` với comment là **per khách per post** (`{pageId}:{convId}` từ Pancake, convId là conversation type COMMENT mang `post_id` — [PancakePollingService.cs:316-322](src/api/Clawbot.Api/Services/PancakePollingService.cs#L316-L322)). Nên khóa hiện tại = **1 rep / khách / post, vĩnh viễn**: khách A comment 3 lần chỉ được rep lần đầu, khách B vẫn được rep bình thường.

Quyết định: đổi sang **1 rep / comment**, chống spam bằng tầng cap riêng thay vì bằng khóa dedup thô. Thiết kế chi tiết ở §6.4. Đây trở thành phần **bắt buộc của P2**, không còn là tùy chọn P3 — không có nó thì mở per-comment sẽ thành máy spam dưới post.

---

## 4. P0 — Làm cho việc đồng bộ engagement quan sát được (nửa ngày, độc lập, giá trị ngay)

Làm trước vì: không phụ thuộc App Review, không phụ thuộc webhook, và nó trả lời được câu "vì sao chưa thấy like/comment" bằng dữ liệu thật thay vì phỏng đoán.

### 4.1 Thêm log cho các nhánh skip và batch

**Đã triển khai.** `MetaEngagementSyncJob` có EventId 5262 cho `no_post_id`, `no_page_credential`, `no_instagram_credential`, `no_standalone_instagram_credential`, `instagram_target_missing` và EventId 5263 cho tổng batch (`total`, `synced`, `skipped`, `failed`). EventId 5264 log exception bất ngờ nhưng không đưa token/raw payload vào message. Job cũng có `DisableConcurrentExecution` để không gọi Graph trùng khi hai lượt cron chồng nhau.

### 4.2 Bỏ parse URL, lưu post id thật

**Đã triển khai.**

- Dùng migration mới `0087_content_schedule_external_post_id.sql` — không dùng số `0060` vì `0060_background_jobs.sql` đã tồn tại và migration runner có baseline theo số.
- Cột `external_post_id NVARCHAR(256) NULL`; có repair command idempotent riêng trong `run-all.bat` sau repair engagement 0059.
- `ContentSchedule.ExternalPostId` được map EF và ghi độc lập với `PostUrl` qua `MarkPosted(postUrl, externalPostId, at)` / `MarkReconciledPosted`.
- `ContentPublishJob` truyền `PublishResult.ExternalId` thật. Fallback URL/idempotency key của `ContentPublishAttempt` không được copy thành schedule provider ID.
- Reconciliation giữ URL là URL; provider ID là trường riêng. ID không hợp lệ bị từ chối/loại khỏi trường schedule; không dùng idempotency key hoặc schedule GUID làm provider ID.
- Nếu publisher trả `Success=true` nhưng không có cả provider ID lẫn URL, publish được chuyển sang `outcome_unknown` để không đánh dấu giả là đã đăng.
- `MetaEngagementSyncJob` ưu tiên `ExternalPostId`; chỉ dùng `ExtractPostId(PostUrl)` cho Facebook row cũ. Graph object ID được giới hạn vào path-safe character set trước khi gọi API.
- Mỗi row được cập nhật watermark lần thử trước khi xử lý, để nhóm row lỗi vĩnh viễn không chiếm 100 slot đầu và làm các post hợp lệ phía sau bị đói.

### 4.3 Mở Instagram

IG **không dùng edge summary** — field khác hẳn:

| Nền tảng | Path | Fields |
|---|---|---|
| Facebook | `{post_id}` | `likes.summary(true),comments.summary(true)` → đọc `.summary.total_count` |
| Instagram | `{ig_media_id}` | `like_count,comments_count` → đọc trực tiếp số |

Tách `ReadFacebookCounts` và `ReadInstagramCounts`. Filter đã mở thành Facebook + Instagram. Facebook dùng `likes.summary(true),comments.summary(true)`; Instagram dùng `like_count,comments_count`. Thiếu field, JSON không phải object hoặc count âm đều là failure quan sát được, không cập nhật `EngagementSyncedAt` như một sync thành công.

Instagram có hai target mode và P0 xử lý cả hai:
- Linked Meta Page (`MetaAssetId` có giá trị): lấy page token qua resolver engagement riêng, không đổi publish resolver.
- Instagram độc lập (`MetaAssetId` null + `ProviderTargetId` có giá trị): dùng `IInstagramCredentialResolver`, kiểm tra target ID khớp trước khi gọi Graph.

### 4.4 Resolver tách theo capability

**Đã triển khai.** `IMetaIntegrationService` có:

```csharp
Task<MetaPageCredential?> ResolvePageForEngagementAsync(Guid tenantId, Guid? assetId, CancellationToken ct = default);
Task<MetaInstagramResolution> ResolveInstagramForEngagementAsync(Guid tenantId, Guid? assetId, CancellationToken ct = default);
```

Facebook comment/engagement page resolver có thể gác task `MODERATE`; Instagram metric resolver giữ `ResolvePageAsync` publish-target semantics vì đọc metric không cần moderation task. `ResolveInstagramAsync` hiện tại không bị đổi, nên đường publish không regress. Engagement-only Instagram scope chỉ kiểm `instagram_basic`; không thêm scope vào `RequiredPageScopes`.

### 4.5 Xác minh trước khi kết luận

Chạy trên DB thật, theo thứ tự:

```sql
-- 1. Cột có tồn tại chưa? (nếu lỗi Invalid column name -> DB chưa chạy repair 0059)
SELECT TOP 20 id, platform, status, post_url, like_count, comment_count, engagement_synced_at
FROM content_schedule
WHERE status = 'posted'
ORDER BY posted_at DESC;

-- 2. Có post nào đủ điều kiện cho job không?
SELECT platform, COUNT(*) AS total,
       SUM(CASE WHEN post_url IS NULL THEN 1 ELSE 0 END) AS no_url,
       SUM(CASE WHEN post_url LIKE '%_%' THEN 1 ELSE 0 END) AS url_has_underscore
FROM content_schedule
WHERE status = 'posted'
GROUP BY platform;

-- 3. Page có task MODERATE không? (JSON tasks đã lưu sẵn)
SELECT external_id, name, tasks, is_active, is_default
FROM meta_assets WHERE asset_type = 'page';

-- 4. Có comment nào trong DB chưa? (dự đoán: 0)
SELECT COUNT(*) FROM messages WHERE message_type = 'comment';
```

Sau đó xem Hangfire dashboard job `meta-engagement-sync`: đã chạy chưa, log 5260/5261/5262 ra gì.

### 4.6 Kết quả verify P0 ngày 2026-07-26

- `dotnet build src/shared/Clawbot.Infrastructure/Clawbot.Infrastructure.csproj --no-restore -c Release`: **0 lỗi, 0 warning**.
- `dotnet build src/api/Clawbot.Api/Clawbot.Api.csproj --no-restore -c Release`: **0 lỗi, 0 warning**.
- `dotnet build Clawbot.sln --no-restore -c Release`: **0 lỗi, 0 warning**.
- `dotnet test Clawbot.sln --no-restore -c Release`: **138/138 pass** tại lần verify P1.
- `git diff --check`: pass.
- `deploy/ci/verify-migrations.ps1`: **pass**, 106 migration files checked. Guard now explicitly treats the pre-0087 parallel migration history as legacy and applies strict same-batch/unique-prefix checks to new migrations.
- Chưa chạy SQL thật trên container: cần chạy `0087` + `0088` và repair trên DB dev trước deploy.

---

## 5. P1 — Ingest comment qua Meta (2-3 ngày)

Hai đường bổ sung nhau, làm cả hai. Webhook cho realtime, job quét cho vá lỗ webhook miss và cho tenant chưa dựng được HTTPS public.

### 5.1 Webhook `object: "page"`, field `feed`

Endpoint mới `/webhooks/meta/page`, **file riêng** `MetaPageWebhookEndpoints.cs`, không nhét vào file business-integration (parse khác nhau: `object` là `"page"` chứ không `"application"`, entry id là page id chứ không app id).

Tách phần dùng chung từ file cũ ra helper (`MetaWebhookSignature.IsValid`, `GetWebhookCandidatesAsync` + verify-token flow) — đừng copy-paste HMAC.

Payload cần lọc: `field == "feed"`, `value.item == "comment"`, `value.verb == "add"`. Các field dùng: `value.comment_id`, `value.post_id`, `value.message`, `value.from.id`, `value.from.name`, `value.created_time`, `value.parent_id`.

Map sang `ChannelMessage`:

| Field | Giá trị |
|---|---|
| `Channel` | `"facebook"` — xem cạm bẫy §5.4 |
| `ExternalThreadId` | `"{page_id}:{from.id}"` (khớp format `{pageId}:{convId}` mà `IsPageIdMatch` và `ExtractCustomerExternalId` đang giả định) |
| `ExternalUserId` | `value.from.id` |
| `MessageType` | `"comment"` |
| `ParentPostId` | `value.post_id` |
| `Metadata["external_message_id"]` | `value.comment_id` — bắt buộc, đây là khóa dedup **và** là id để rep sau này |
| `Metadata["page_id"]` | page id (resolve inbox) |
| `Metadata["sender_id"]`, `["sender_name"]` | từ `value.from` |

Rồi `Publish(new ChannelInboundMessageReceived(tenantId, msg))` — reuse toàn bộ consumer + ingest + dedup có sẵn. Endpoint trả 200 ngay, không xử lý đồng bộ (Meta retry nếu non-200).

Echo của chính page: nếu `value.from.id == page_id` thì set `Metadata["is_owner"] = "true"` — ingestor tự xử lý ([ChannelMessageIngestor.cs:46-51](src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs#L46-L51)). Không lọc bước này thì bot sẽ tự trả lời chính nó.

### 5.2 Subscribe page vào webhook

`POST /{page-id}/subscribed_apps` với `subscribed_fields=feed` bằng page token. Cần scope `pages_manage_metadata`.

Chỗ gọi: sau khi `SyncPagesAsync` gán asset, thêm bước best-effort subscribe cho page có `MODERATE`. Thất bại thì log + set trạng thái `comment_sync: unavailable` chứ không làm fail cả flow connect.

Cần bề mặt admin đọc được: `GET /{page-id}/subscribed_apps` để UI /channels hiển thị "webhook comment: đã đăng ký / chưa".

### 5.3 Job quét bù `MetaCommentSyncJob` cron `*/5`

Vì sao cần dù đã có webhook: webhook mất gói là chuyện thường, và tenant dev/on-prem có thể không có HTTPS public.

```
với mỗi content_schedule: status = posted, platform in (facebook, instagram), posted_at >= now - 7 ngày
  FB: GET /{post_id}/comments?fields=id,message,from,created_time,parent,can_comment
      &filter=stream&order=chronological&limit=50
  IG: GET /{ig_media_id}/comments?fields=id,text,from,timestamp,replies
  với mỗi comment: publish ChannelInboundMessageReceived (dedup lo phần trùng)
```

Cửa sổ 7 ngày vì private reply hết hạn sau 7 ngày (§7.2) — comment cũ hơn không còn hành động được, quét là đốt quota.

Job này chạy trong Hangfire scope **không có HTTP context** → phải nhận `tenantId` tường minh và `IgnoreQueryFilters()` mọi query `ITenantOwned`, nếu không sẽ trả rỗng âm thầm (memory `hangfire-job-scope-has-no-tenant`).

### 5.4 Cạm bẫy: chuỗi `Channel` và inbox resolution

`ChannelMessageIngestor.ResolveInboxIdAsync` khớp conversation với row `Inboxes` theo `Platform == message.Channel` ([:207-245](src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs#L207-L245)). Đường Pancake dùng `evt.Platform ?? "pancake"`.

Tenant chưa nối Pancake **không có row `inboxes` nào** → mọi fallback đều rỗng → `inbox_id = NULL` → hội thoại lọt khỏi filter theo kênh và khỏi auto-owner lead. Comment vào DB nhưng không ai thấy.

Vì vậy P1 phải kèm: khi Meta connect xong và page có MODERATE, **upsert row `Inboxes`** (`Platform = "facebook"`, `ExternalPageId = page.ExternalId`, `EncryptedAccessToken = NULL` vì token nằm ở `meta_assets`). Đây là bước hay bị quên và làm cả P1 trông như "không chạy".

Chốt luôn chuỗi platform dùng cho conversation Meta-native là `"facebook"` / `"instagram"`, và ghi vào doc. Nếu sau này tenant nối thêm Pancake sẽ sinh conversation song song trên cùng khách — chấp nhận, không merge (merge cần plan riêng).

### 5.5 Kết quả triển khai P1 ngày 2026-07-26

**Đã triển khai:**

- `MetaPageWebhookEndpoints` tại `/webhooks/meta/page`: GET verify token, POST HMAC `X-Hub-Signature-256`, parse `object=page` + `feed/comment/add`, giới hạn body streaming 1 MiB và tối đa 500 event sau dedup.
- Tenant mapping theo Page asset + Meta connection active; global fallback không được phép vượt qua tenant có Meta app override.
- `ChannelMessageIngestor` dedup theo `(TenantId, ExternalMessageId)` và chỉ fallback Inbox khi tenant có đúng một Inbox active; không còn chọn Inbox đầu tiên tùy ý.
- `MetaInboxProvisioner` tạo/upsert Inbox `facebook`/`instagram`; migration `0088_meta_inbox_unique_identity.sql` + repair `run-all.bat` thêm unique filtered index chống race.
- Meta page webhook subscription `POST /{page-id}/subscribed_apps` với `subscribed_fields=feed`, best-effort khi page sync/OAuth; lỗi quyền/network chỉ log EventId 5290, không làm hỏng connect.
- `MetaCommentSyncJob` cron `*/5`: quét cửa sổ 7 ngày, tối đa 10 trang Graph và 500 comment/schedule, FB `filter=stream`, IG flatten + paginate `replies.data`, hỗ trợ legacy FB URL và standalone IG credential; publish qua EF bus outbox sau từng schedule.
- Thêm `ContentSchedule.MetaCommentsSyncedAt` + migration/repair `0089` để row lỗi không chiếm mãi 100 slot đầu; hỗ trợ cả `paging.cursors.after` và `paging.next`.
- `CommentAutoReplyJob` tạm defer `facebook`/`instagram` tới P2 và loại Meta khỏi batch `Take(100)`, tuyệt đối không bắn nhầm comment Meta qua Pancake hoặc làm đói Pancake.
- Test mới `Clawbot.Infrastructure.Tests`: parser FB/IG + nested IG replies, Graph ID safety, metric malformed/negative; toàn bộ solution hiện **138 test pass** (130 agent + 8 infrastructure).

**Deferred sang P3:** GET trạng thái `subscribed_apps` và UI /channels hiển thị webhook/permission capability. P1 hiện mới có log vận hành và fallback an toàn.

---

## 6. P2 — Gửi reply qua Meta (1-2 ngày)

### 6.1 `MetaCommentChannelAdapter : ICommentChannelAdapter`

Giữ nguyên chữ ký interface, không đổi `CommentAutoReplyJob`.

```csharp
// SendCommentReplyAsync — rep công khai
// FB: POST /{comment_id}/comments        fields: message
// IG: POST /{ig_comment_id}/replies      fields: message
// -> đọc "id" trong response, trả về làm external_message_id (dedup echo webhook)

// SendPrivateReplyAsync — DM riêng từ comment
// FB: POST /{page_id}/messages
//     body: { recipient: { comment_id: "<comment_id>" }, message: { text: "..." } }
// IG: POST /{page_id}/messages  (cùng shape, recipient.comment_id)
// Lưu ý: endpoint legacy POST /{comment_id}/private_replies vẫn còn nhưng dạng
// recipient.comment_id là dạng Meta khuyến nghị hiện tại -> verify bằng Graph Explorer trước khi code
```

Dùng `IMetaGraphClient.PostAsync` có sẵn. `SendPrivateReplyAsync` hiện nhận `postId` + `fromId` — đường Meta không cần hai tham số này (comment_id là đủ) nhưng **giữ nguyên chữ ký** để không phá adapter Pancake; chỉ bỏ qua chúng bên trong.

Chọn token: `ResolvePageForEngagementAsync` (§4.4). IG dùng page token của page đã link, không phải token IG riêng.

Rate limit: adapter Pancake có `OutboundLimiter` 5/s theo `tenant:page` ([PancakeChannelAdapter.cs:160-167](src/shared/Clawbot.Infrastructure/Channels/Pancake/PancakeChannelAdapter.cs#L160-L167)). Adapter Meta phải có limiter tương đương — Graph siết theo page, vượt là bị khóa tạm cả page.

Không retry POST. Trả `outcome_unknown` như đường publish thay vì gửi lại (gửi lại = comment đôi, không có idempotency key).

### 6.2 Resolver thay cho cast vô điều kiện

```csharp
// thay DependencyInjection.cs:199-201
services.AddScoped<PancakeCommentChannelAdapter>(...);   // như hiện tại
services.AddScoped<MetaCommentChannelAdapter>();
services.AddScoped<ICommentChannelAdapterResolver, TenantCommentChannelAdapterResolver>();
```

`CommentAutoReplyJob` nhận resolver thay vì adapter, gọi `ResolveAsync(tenantId, platform, ct)`. Trả null → nhánh skip 9203 hiện có **cuối cùng cũng hoạt động thật**, kèm reason cụ thể (`no_pancake_inbox_no_meta_moderate`).

Thứ tự chọn: Pancake trước (có inbox + token) → Meta (connection usable + page MODERATE) → null.

### 6.3 Scope Meta — tuyệt đối không thêm vào `RequiredPageScopes`

Đây là cạm bẫy nghiêm trọng nhất về mặt vận hành.

`RequiredPageScopes = ["pages_manage_posts", "pages_read_engagement", "pages_show_list"]` ([MetaIntegrationService.cs:93](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaIntegrationService.cs#L93)) được kiểm ở **hai** chỗ:
- lúc connect → `throw meta_required_permissions_missing` ([:114-116](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaIntegrationService.cs#L114-L116))
- lúc refresh/health → `connection.RequireReconnect(...)` ([:290-296](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaIntegrationService.cs#L290-L296))

Thêm `pages_manage_engagement` vào mảng này sẽ khiến **mọi tenant đang chạy bị `MetaConnectionHealthJob` đánh dấu `reconnect_required` và mất luôn khả năng đăng bài** ngay ở lần chạy kế tiếp, trước cả khi ai kịp cấp quyền mới.

Cách đúng: mảng **riêng, tùy chọn**

```csharp
private static readonly string[] CommentPageScopes =
    ["pages_manage_engagement", "pages_messaging", "pages_manage_metadata"];
private static readonly string[] CommentInstagramScopes =
    ["instagram_manage_comments", "instagram_manage_messages"];
```

Không throw, không `RequireReconnect`. Chỉ tính ra một cờ năng lực (`CanAutoReplyComments`) đưa lên snapshot → UI /channels hiển thị "Tự trả lời comment: chưa đủ quyền, cần cấp lại" với link re-authorize. Resolver ở §6.2 đọc cờ này.

Scope được khai trong **Meta Login Configuration** (`config_id`), không phải trong code — `BuildAuthorizationUrlAsync` không truyền `scope` ([MetaGraphClient.cs:171-181](src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaGraphClient.cs#L171-L181)). Nên bước "thêm quyền" là **việc trong Meta App Dashboard**, không phải việc trong repo.

---

### 6.4 Rep đủ mọi comment + chống spam (bắt buộc, đi kèm 6.1-6.2)

Mở per-comment mà không có phần này thì bot sẽ rải cùng một câu dưới 20 comment trong 2 phút — Facebook đánh dấu spam page, và khách nhìn vào thấy rõ là máy.

#### 6.4.1 Đổi khóa idempotency sang từng comment

Cần cột mới vì hiện không có gì trỏ từ dòng reply về comment gốc (`ExternalMessageId` của dòng out là id của **chính reply**, không phải id comment được rep).

- Migration `0061_messages_parent_comment_id.sql`: `ALTER TABLE messages ADD parent_comment_id NVARCHAR(128) NULL;` (không `GO`) + index `(tenant_id, parent_comment_id)` ở **file riêng** — index trên cột vừa thêm bằng ALTER phải nằm ở migration khác (memory `clawbot-migration-no-go`) + khối repair trong `run-all.bat`.
- `Message`: thêm `public string? ParentCommentId { get; private set; }`; thêm param vào `Message.Create` ([Message.cs:55-72](src/shared/Clawbot.Domain/Conversations/Message.cs#L55-L72)) và `Conversation.AppendMessage` ([Conversation.cs:93](src/shared/Clawbot.Domain/Conversations/Conversation.cs#L93)) — cả hai đang có 15+ optional param, thêm vào cuối để không phá call site nào.
- Dòng bot ghi `parentCommentId: inbound.ExternalMessageId` cho cả reply công khai và DM.
- Dedup mới:

```csharp
var alreadyHandled = await db.Messages.IgnoreQueryFilters()
    .AnyAsync(m => m.TenantId == tenantId
        && m.Direction == "out"
        && m.ParentCommentId == inbound.ExternalMessageId, ct);
```

Bỏ `SenderType == "bot"` trong điều kiện này là **có chủ ý**: nếu sale đã trả lời tay comment đó thì bot cũng không được chen vào.

#### 6.4.2 Sáu tầng cap (đếm hết từ bảng `messages`, không cần bảng mới)

| Tầng | Quy tắc | Mặc định | Truy vấn đếm |
|---|---|---|---|
| T1 | 1 rep công khai / khách / post / 24h | 1 | `out+bot+comment` cùng `ConversationId` + `ParentPostId`, `SentAt >= now-24h` |
| T2 | tối đa N rep / khách / ngày trên mọi post | 3 | join `Conversations` theo `ContactId` |
| T3 | tối đa N rep / post / ngày | 20 | theo `ParentPostId` |
| T4 | cách nhau tối thiểu X giây trên cùng post | 20s | `MAX(SentAt)` theo `ParentPostId` |
| T5 | tối đa N rep / page / ngày (cầu dao tổng) | 200 | theo `Conversation.InboxId` |
| T6 | tối đa K rep / post trong 1 lần chạy job | 5 | biến đếm trong vòng lặp `RunScanAsync` |

T1 giữ lại đúng hành vi hiện tại nhưng **có cửa sổ 24h** thay vì vĩnh viễn — khách hỏi giá tuần này, tuần sau hỏi lại thì được rep tiếp.

T4 + T6 là phần quan trọng nhất khi webhook chết rồi job quét `*/5` gom về 50 comment một lượt: không có chúng thì 50 reply bắn trong vài giây.

Cap là hằng số `private const` trong job cho P2 (không rải magic number), nâng lên tenant config ở P3 nếu vận hành cần chỉnh. Chạm T5 thì log Warning + `INotificationPublisher` với `GroupKey: "comment.autoreply.capped"` — chạm cầu dao mà im lặng là quay lại đúng lỗi đang có.

#### 6.4.3 Chặn vòng lặp bot-tự-rep

Ba lớp, cần cả ba:

1. **Echo page**: `value.from.id == page_id` → `Metadata["is_owner"] = "true"`, ingestor xử lý ([ChannelMessageIngestor.cs:46-51](src/shared/Clawbot.Infrastructure/Channels/ChannelMessageIngestor.cs#L46-L51)).
2. **Chỉ rep comment top-level**: webhook trả `value.parent_id` — chỉ xử lý khi `parent_id == post_id` hoặc thiếu. Comment lồng dưới reply của bot bị bỏ. Mất một chút: khách trả lời tiếp dưới reply của bot sẽ không được bot rep — chấp nhận, vì lúc đó DM riêng đã mở và sale nên vào tay. Ghi rõ vào doc là hành vi có chủ ý.
3. **Id reply trả về phải được lưu**: `SendCommentReplyAsync` của adapter Meta bắt buộc trả `id` từ response Graph để dòng out có `ExternalMessageId` — nếu trả null, reply của bot quay lại qua webhook sẽ không dedup được và thành vòng lặp thật. Trả null thì phải log Error, không âm thầm.

#### 6.4.4 Xoay biến thể text

Rep 10 comment bằng 10 câu y nguyên là tín hiệu spam rõ nhất với Facebook. Thay 1 chuỗi tĩnh bằng pool 4 biến thể duyệt sẵn, chọn theo `soRepDaCoTrenPost % 4` (xác định, không random — `Math.Random` cũng không dùng được trong test tái lập):

```csharp
// Review-gate QĐ6: 4 biến thể dưới là text TĨNH 100%, không interpolate dữ liệu ngoài,
// duyệt 1 lần tại đây. Thêm biến động (tên khách, LLM sinh) thì PHẢI qua toxicity trước khi gửi.
private static readonly string[] PublicReplyVariants =
[
    "Cảm ơn bạn đã quan tâm. Mình đã nhắn riêng để tư vấn chi tiết ngay.",
    "Chào bạn, mình vừa gửi thông tin qua tin nhắn riêng nhé.",
    "Bạn kiểm tra tin nhắn giúp mình nhé, mình đã gửi chi tiết ở đó.",
    "Mình đã nhắn riêng cho bạn thông tin đầy đủ rồi nhé.",
];
```

Vẫn đúng QĐ6: text tĩnh, duyệt một lần trong code review, không đổi sang LLM sinh trong plan này.

#### 6.4.5 DM riêng

- Meta cho **1 private reply / comment**, cửa sổ **7 ngày**. Comment cũ hơn 7 ngày: bỏ hẳn bước DM, chỉ rep công khai (đừng để nó fail rồi log warning mỗi vòng).
- Thêm cap: 1 DM / khách / post / 24h, kể cả khi khách comment nhiều lần được rep công khai nhiều lần. Bị DM lặp gây khó chịu hơn nhiều so với reply công khai lặp.
- Chỉ đúng nhánh này giữ `try/catch` bọc riêng như hiện tại ([CommentAutoReplyJob.cs:126-136](src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs#L126-L136)) — DM fail không được làm mất reply công khai đã gửi.

#### 6.4.6 Không có rủi ro chi phí LLM

Đã kiểm: `IIntentClassifier` bind vào `KeywordIntentClassifier` (singleton, keyword bucket VI/EN/中 — [DependencyInjection.cs:141](src/shared/Clawbot.Infrastructure/DependencyInjection.cs#L141)), **không gọi LLM**. Nên mở từ per-post lên per-comment không làm tăng token/chi phí gateway — khác với đường chat. Comment chỉ có emoji hoặc chỉ tag người khác tự bị loại ở `!ActionableLabels.Contains(detected.Label)`, không cần pre-filter riêng.

### 6.5 Kết quả triển khai P2 ngày 2026-07-26

**Đã làm (P2a — adapter + resolver)**

- [MetaCommentChannelAdapter.cs](src/shared/Clawbot.Infrastructure/Channels/Meta/MetaCommentChannelAdapter.cs) — `ICommentChannelAdapter` chạy trên `IMetaGraphClient`, giữ nguyên chữ ký nên `CommentAutoReplyJob` không phải đổi:
  - rep công khai: `{comment_id}/comments` (FB) hoặc `{comment_id}/replies` (IG);
  - DM: `{page_id}/messages` với `recipient={"comment_id":...}`, đọc cả `id` lẫn `message_id` trong response vì đường DM trả `message_id`;
  - response thiếu id → ném `ChannelDeliveryAmbiguousException`, job ghi `outcome_unknown` thay vì gửi lại (không có idempotency key ở phía Graph).
- Rate limit 5 req/s theo `tenant:{id}:meta:{pageId}` bằng `PartitionedRateLimiter` sliding window — tương đương limiter của Pancake. **Hạn chế đã biết**: limiter nằm trong process, chạy nhiều host thì trần thực tế nhân theo số host.
- `TenantCommentChannelAdapterResolver` thay cho cast vô điều kiện ở DI: Pancake trước khi `IPancakeConfigResolver` trả config (và `PageId` trống hoặc khớp page của thread), sau đó Meta cho facebook/instagram, còn lại trả null → nhánh skip 9203 có log thật. Đúng nguyên tắc đã chốt: *chưa nối Pancake mới fallback Graph*.
- Token chọn theo capability (§4.4): `ResolvePageForCommentsByExternalIdAsync` cho rep công khai, `ResolvePageForPrivateRepliesByExternalIdAsync` / `ResolveInstagramForPrivateRepliesAsync` kiểm trước khi gửi DM. `RequiredPageScopes` **không** bị đụng vào.
- Chuẩn hóa đầu vào: id chỉ nhận `[A-Za-z0-9_-]` tối đa 256 ký tự, text cắt ở 4.000 ký tự.

**Đã làm (P2b — per-comment + chống spam)**

- Idempotency chuyển sang từng comment qua `messages.parent_comment_id` (migration `0090`, index `0091`, unique claim `0092`). Điều kiện dedup cố ý **không** lọc `sender_type = 'bot'`: sale đã trả lời tay comment đó thì bot đứng ngoài.
- Sáu tầng cap giữ đúng §6.4.2 (1 rep/khách/post/24h, 3/khách/ngày, 20/post/ngày, cách nhau 20s, 200/inbox/ngày, 5/post/lần chạy), cộng 1 DM/khách/post/24h và cửa sổ 7 ngày cho DM.
- **Khác plan gốc, có chủ ý**: các cap là đọc-rồi-ghi nên chỉ dựa vào unique index theo comment là không đủ — nhiều worker cùng lúc vẫn vượt trần theo post. Bước kiểm cap + ghi claim được bọc trong một transaction kèm `sys.sp_getapplock` theo `post_id` (`@LockOwner='Transaction'`, timeout 5s). Trên provider không phải SQL Server thì bỏ qua applock.
- Chặn vòng lặp đủ ba lớp: echo page đánh dấu `is_owner`, chỉ xử lý comment top-level (có `ParentCommentId` là bỏ, có log), và external id của reply bắt buộc được lưu lại.

**Sửa trong lúc review**

- `0092` ban đầu thiếu `status <> N'send_failed'` trong filtered index, trong khi mọi guard C# đều loại `send_failed` — một lần gửi hỏng sẽ khóa vĩnh viễn việc thử lại comment đó. Migration được sửa tại chỗ theo kiểu tự chữa (đọc `sys.indexes.filter_definition`, drop rồi tạo lại nếu định nghĩa cũ) vì file chưa từng deploy; khối repair trong `run-all.bat` sửa khớp.
- `Conversation.DiscardMessage` (mới): detach entity **không** gỡ nó khỏi navigation collection, `DetectChanges` sẽ insert lại ở `SaveChanges` kế tiếp. Cả nhánh claim công khai và DM đều gọi `DiscardClaim` (gỡ khỏi collection + detach), kèm `catch { DiscardClaim(...); throw; }`.
- `RunScanAsync` dùng chung một `DbContext` cho cả lô nên catch của nó thêm `db.ChangeTracker.Clear()` — một candidate hỏng không kéo entity dở dang sang candidate sau (cùng lớp lỗi với memory `agent-schedule-batch-shared-dbcontext`).

**Chấp nhận tồn tại**

- Applock theo từng post, nên hai cap cắt ngang post (3/khách/ngày, 200/inbox/ngày) vẫn có thể vượt nhẹ khi nhiều post chạy song song. Đây là trần an toàn, không phải trần thanh toán, nên đổi sang applock theo tenant (làm nghẽn toàn bộ) là không đáng.
- Bộ đếm 5/post/lần chạy và rate limiter đều trong process.

### 6.6 Kết quả triển khai P3 ngày 2026-07-26

Phần "cap lên tenant config" **không làm** — plan ghi rõ điều kiện *"nâng lên tenant config nếu vận hành cần chỉnh"*, và chưa có yêu cầu đó. Cap vẫn là hằng số có tên trong job.

Phần đã làm là hiển thị trạng thái nguồn comment và quyền, vì đây là điều kiện để §7.1 "suy giảm êm, không im lặng" thành sự thật:

- `meta_assets.feed_subscribed_at` (migration `0093` + repair trong `run-all.bat`) — `MetaAsset.MarkFeedSubscribed` được gọi khi `SubscribePageFeedAsync` thành công. NULL nghĩa là chưa đăng ký được webhook (thường do thiếu `pages_manage_metadata`), comment page đó chỉ về qua job đối soát `*/5`.
- `MetaEngagementCapabilitySnapshot` trong `MetaIntegrationService`: scope còn thiếu cho comment và cho DM (đọc từ token), cộng số page active / rep được comment / DM được / đã subscribe (đọc từ task `MODERATE`, `MESSAGING` trên từng page). Phải soi cả hai vì task nằm trên page còn scope nằm trên token.
- Snapshot đi qua `AdminMetaIntegrationEndpoints` lên `MetaIntegrationStatus.engagement`; `AdminSocialChannelsSection` hiện chip từng page ("Trả lời bình luận", "Nhắn riêng từ bình luận", "Webhook bình luận") và một khối tổng kèm cảnh báo liệt kê đúng tên scope còn thiếu, kèm ghi chú rằng page chưa subscribe vẫn nhận comment qua job đối soát.

**Verify (2026-07-26)**

| Bước | Kết quả |
|---|---|
| `dotnet build Clawbot.sln -c Release` | Build succeeded, 0 warning, 0 error |
| `dotnet test Clawbot.sln -c Release` | 138 pass / 0 fail (130 Agents + 8 Infrastructure) |
| `deploy/ci/verify-migrations.ps1` | pass, 111 file `.sql` |
| `npx tsc --noEmit` | sạch |
| `npx eslint` trên file FE đã đổi | sạch |
| `git diff --check` | sạch |

**Verify `run-all.bat` trên DB thật (2026-07-26)**

Chạy nguyên khối stage database của `run-all.bat` (trích đúng file, chỉ bỏ `:stop_app_ports` và dừng trước `dotnet restore`) trên hai đường:

| Đường | Kết quả |
|---|---|
| DB mới hoàn toàn (`clawbot_freshverify`, tạo từ trắng) | exit 0 — replay đủ 111 migration, 95 bảng, cả 3 cổng verify (tenant columns, `lead_revenues`, `content_render_tasks`) pass |
| DB dev đang có dữ liệu (`clawbot`, 1.622 message) | exit 0 — `0087`–`0093` đã ghi trong `schema_migrations`, khối repair no-op, data patch skip |
| Khối repair chạy lặp 2 lần | exit 0 cả hai lần, `UX_messages_bot_parent_comment_type` giữ nguyên `index_id`, không drop/recreate |

Xác nhận trên cả hai DB: 4 cột mới có mặt, 3 index mới có mặt, và `filter_definition` của index claim chứa `[status]<>N'send_failed'` — nghĩa là guard `CHARINDEX` nhận đúng định nghĩa mới và không tạo lại index mỗi lần chạy. `parent_comment_id` NVARCHAR(256) khớp `HasMaxLength(256)`, `external_post_id` NVARCHAR(256) khớp `MaxExternalPostIdLength`. Hai DB nháp đã drop sau khi verify.

**Còn nợ**

- Chưa chạy phần sau stage database của `run-all.bat` (restore/build/khởi động 4 service) trong lần verify này để không dừng service đang chạy của máy dev; phần đó không bị plan này sửa.
- Chưa có unit test cho phần claim/cap: `tests/Clawbot.Infrastructure.Tests` không có EF provider nào (chỉ xunit/FluentAssertions/NSubstitute), 8 test hiện có đều là parse JSON Graph thuần. Muốn phủ phần này phải thêm provider in-memory hoặc Testcontainers SQL Server — SQL Server còn cần thật vì logic dựa vào applock và filtered index.
- Chưa verify tay §8.3 trên page thật (bước 3-4 cần advanced access).

---

## 7. Rủi ro

### 7.1 App Review — ràng buộc vận hành, không phải quyết định kiến trúc

Đã chốt: **cứ làm fallback**. Phần này chỉ để biết giới hạn khi chạy thật, code không đổi theo nó.

`pages_manage_engagement`, `pages_messaging`, `instagram_manage_comments`, `instagram_manage_messages` là **advanced access**:

| Trạng thái app Meta | Fallback chạy trên page nào |
|---|---|
| Standard access (mặc định) | Chỉ page mà user cấp quyền có role admin/dev/tester **trong app Meta** — đủ cho dev, demo, và tenant nội bộ |
| Advanced access (qua App Review) | Mọi page của mọi khách hàng |

Nghĩa là: viết code xong thì test được ngay trên page nội bộ; muốn bán cho khách ngoài thì phải submit App Review (vài tuần, cần screencast mô tả use case, có thể bị từ chối). Pancake không vướng vì họ đã được duyệt sẵn.

Yêu cầu thiết kế rút ra: khi quyền thiếu, hệ thống phải **suy giảm êm, không im lặng** — resolver trả null có log kèm reason, UI /channels hiển thị "Tự trả lời comment: chưa đủ quyền", và P1 (comment hiện trong inbox) vẫn hoạt động để sale trả lời tay. Không được để nó fail như hiện tại: exception bị catch nuốt, không ai biết vì sao.

### 7.2 Giới hạn nền tảng (đã xác minh qua tài liệu Meta)

- Rep comment cần **page token của người có task `MODERATE`** trên page, kèm `pages_manage_engagement`.
- Field `can_comment` trên từng comment cho biết comment đó có rep được không — phải kiểm, không phải comment nào cũng cho rep.
- Private reply: **1 lần duy nhất mỗi comment**, và **trong 7 ngày** kể từ comment. Quá hạn là fail cứng.
- IG: `POST /{ig-comment-id}/replies` chỉ tạo reply trên comment, không tạo comment mới trên media. Đọc `username` của người comment cần `instagram_manage_comments` (thay đổi từ 27/08/2024).
- IG comment webhook là `object: "instagram"`, field `comments` — **khác** `object: "page"` field `feed`. Nếu làm IG realtime thì cần nhánh parse thứ hai, hoặc chỉ dựa vào job quét ở §5.3 cho IG (đơn giản hơn, đề xuất làm vậy trước).

### 7.3 Kỹ thuật

- Webhook cần HTTPS public + verify token. Dev local cần tunnel — hoặc bỏ webhook, chỉ dùng job quét `*/5`.
- Comment do bot rep sẽ quay lại qua webhook/quét như comment mới. Dedup theo `external_message_id` xử lý được **chỉ khi** `SendCommentReplyAsync` lưu đúng id trả về (dòng [CommentAutoReplyJob.cs:107-110](src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs#L107-L110) đã làm đúng). Adapter Meta phải trả `id` thật, không trả null.
- EF Core 8: `HasQueryFilter` không cộng dồn (memory `ef8-query-filters-not-additive`) — entity mới thêm phải dùng guard `DeletedAt` tường minh.
- Text rep hiện là **tĩnh 100%**, được duyệt một lần theo QĐ6 review-gate ([CommentAutoReplyJob.cs:100-104](src/shared/Clawbot.Infrastructure/Jobs/CommentAutoReplyJob.cs#L100-L104)). Plan này **không** đổi sang LLM sinh. Nếu sau này muốn LLM soạn thì bản render bắt buộc qua toxicity filter trước khi gửi — và đó là plan riêng.

---

## 8. Kiểm thử

### 8.1 Unit

- `ExtractPostId`: permalink FB feed, FB photo, IG, URL rác, null → giữ test cũ nếu có, thêm ca IG.
- Parse webhook page/feed: payload comment add, payload post (bỏ), payload verb remove (bỏ), signature sai (401), `object` sai (bỏ), entry rỗng.
- `MetaCommentChannelAdapter`: shape body đúng cho 4 ca (FB reply, FB private, IG reply, IG private); response thiếu `id` → outcome unknown.
- `TenantCommentChannelAdapterResolver`: có Pancake → Pancake; không Pancake + Meta MODERATE → Meta; không cả hai → null.
- `ReadCounts` FB (summary) vs IG (`like_count`/`comments_count`).
- Chống spam (§6.4), mỗi tầng một test: cùng comment 2 lần → rep 1 lần; sale đã rep tay comment đó → bot bỏ; khách comment 2 lần trong 1h → 1 rep (T1); comment lại sau 25h → được rep (T1 hết hạn); 25 comment cùng post trong ngày → dừng ở 20 (T3); 2 comment liền nhau trong 5s → cái sau bị hoãn (T4); 1 lần job có 50 comment cùng post → tối đa 5 (T6); comment lồng dưới reply bot → bỏ; `SendCommentReplyAsync` trả null → log Error, không ghi sent; xoay text: 4 rep liên tiếp trên cùng post ra 4 chuỗi khác nhau; comment 8 ngày tuổi → chỉ rep công khai, không gọi DM.

Lưu ý: bộ test .NET đã bị gỡ có chủ đích ở commit `5e24566` (memory `dotnet-test-suite-removed`). Muốn viết test thì scaffold lại project từ `5e24566^`, kèm `tests/Directory.Build.props` với `NoWarn CA1707`.

### 8.2 E2E mock

- POST webhook giả với HMAC hợp lệ → comment xuất hiện trong `/inbox`, `message_type = 'comment'`, `parent_post_id` đúng.
- Trigger `CommentAutoReplyJob` với Graph mock → verify body request tới `/{comment_id}/comments`, và dòng `out/bot/comment` được lưu kèm external id.
- Ca skip: conversation `AiAutoReplyEnabled = false` → không gọi Graph (bảo vệ handover, memory `ai-handover-pause-resume`).

### 8.3 Tay, trên page thật

Trước khi code P2, verify bằng Graph Explorer với page token thật:
1. `GET /{page_id}?fields=tasks` → có `MODERATE`?
2. `GET /{post_id}/comments?fields=id,message,from,can_comment` → trả về gì
3. `POST /{comment_id}/comments` với `message=test` → thành công?
4. `POST /{page_id}/messages` với `recipient={"comment_id":"..."}` → thành công?

Bước 3 và 4 fail nghĩa là §7.1 đang chặn, và toàn bộ P2 nên hoãn.

---

## 9. Thứ tự làm và ước lượng

| Phase | Nội dung | Ước lượng | Phụ thuộc |
|---|---|---|---|
| **P0** | Log + `external_post_id` + mở IG + `ResolvePageForEngagementAsync` | 0.5 ngày | không |
| **Verify** | Graph Explorer §8.3 + SQL §4.5 | 1 giờ | P0 để có log |
| **P1** | Webhook page/feed + job quét + upsert Inbox row | 2-3 ngày | verify |
| **P2a** | Adapter Meta + resolver + cờ năng lực scope | 1-2 ngày | P1 (không có comment thì không có gì để rep) |
| **P2b** | Rep per-comment + 6 tầng cap + chống vòng lặp + xoay text (§6.4) | 1 ngày | P2a — **không được deploy P2a mà thiếu P2b** |
| **P3** | Cap lên tenant config + UI trạng thái nguồn comment/quyền | 1 ngày | P2b |

Trạng thái thực tế 2026-07-26: P0 (§4.6), P1 (§5.5), P2a+P2b (§6.5), P3 (§6.6) đều đã code và verify build/test/lint trên branch `thang/ai-autoreply-kb-improvements`. Riêng hạng mục "cap lên tenant config" của P3 bỏ lại theo đúng điều kiện của chính nó (chưa có yêu cầu vận hành).

Cắt được: P0 đứng một mình, giá trị ngay, không rủi ro, không phụ thuộc quyền Meta. Nếu §8.3 bước 3-4 fail (chưa có advanced access) thì dừng sau P1 — sale vẫn thấy comment trong inbox và trả lời tay, đã tốt hơn hẳn hiện tại.

Ràng buộc thứ tự duy nhất phải giữ: **P2a và P2b ra cùng một lần**. Ship adapter gửi mà chưa có cap là ship một máy spam.

---

## 10. Nguồn tham khảo

- [Graph API Reference: Comment](https://developers.facebook.com/docs/graph-api/reference/comment/) — `pages_manage_engagement`, page token với task MODERATE, field `can_comment`
- [Webhooks Reference: Page](https://developers.facebook.com/docs/graph-api/webhooks/reference/page/) — field `feed`, `value.item`/`verb`/`comment_id`/`post_id`/`from`
- [IG Comment reference](https://developers.facebook.com/docs/instagram-platform/instagram-graph-api/reference/ig-comment/) — `replies` edge, `instagram_manage_comments`
- [Messenger Platform: Private Replies](https://developers.facebook.com/docs/messenger-platform/discovery/private-replies) — 1 lần/comment, cửa sổ 7 ngày
- [Private Replies (Instagram)](https://developers.facebook.com/docs/instagram-platform/private-replies/)
