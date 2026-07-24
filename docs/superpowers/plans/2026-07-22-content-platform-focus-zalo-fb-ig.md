# Plan: Tập trung content vào 3 nền tảng (Zalo OA · Facebook · Instagram)

**Ngày:** 2026-07-22
**Trạng thái:** APPROVED / IN PROGRESS
**Nhánh:** thang/ai-autoreply-kb-improvements
**Tiến độ:** khảo sát repository/research đã hoàn tất; implementation platform-focus đang thực hiện.

## 1. Mục tiêu

Thu gọn phần Content về đúng 3 platform có thể ghi/chọn mới: **`facebook`, `zalo`, `instagram`**.

- **Ẩn TikTok + YouTube** khỏi mọi bề mặt ghi mới của Content (bộ chọn đăng bài, nguồn trend-scan, kênh analytics).
- **Thêm Instagram** vào đường đăng bài (hiện backend chỉ có `facebook` + `zalo`).
- Giá trị platform legacy vẫn **đọc/render được** để bảo toàn lịch sử, nhưng không được chọn mới, không được dùng ngầm cho generation/repurpose và không được suy diễn thành một trong ba platform writable.

## 2. Quyết định đã chốt (từ review với chủ dự án)

| # | Quyết định | Chốt |
|---|-----------|------|
| Q1 | Cấu hình đăng Instagram | **Cả 2 chế độ, mặc định dùng chung FB Graph** (IG Business account liên kết Page đã kết nối Meta); cho nhập credential IG riêng khi tenant muốn tách |
| Q2 | Nền tảng `website` trong bộ chọn | **Ẩn luôn** — chỉ còn đúng 3 nền tảng |
| Q3 | Mức độ "ẩn" TikTok/YouTube | **Ẩn UI + guard backend, GIỮ code** (đảo ngược dễ, ít rủi ro) |
| Q4 | Brief lịch sử có platform unsupported | Text-only edit được giữ nguyên platform cũ; không được chọn mới hoặc dùng ngầm cho generate/repurpose |
| Q5 | Platform writable | Closed set chính xác: `facebook`, `zalo`, `instagram` |

## 3. Ràng buộc kỹ thuật cần lưu (Instagram Graph)

- IG feed publish là **2 bước**: `POST {ig-user-id}/media` (tạo container) → `POST {ig-user-id}/media_publish`.
- **Bài IG BẮT BUỘC có media** (ảnh/video). Không đăng text-only được → item IG không có asset ảnh phải bị chặn với thông báo rõ.
- Ảnh phải là **URL public HTTPS** để Meta fetch được (giống ảnh FB `photos` hiện tại).
- Ở chế độ dùng chung FB Graph: lấy `instagram_business_account` từ Page qua `GET {page-id}?fields=instagram_business_account{id}`. Page chưa liên kết IG → lỗi `instagram_not_linked`.
- Capability/quyền Instagram là **scope riêng** với kết nối Facebook. Thiếu `instagram_basic`/`instagram_content_publish` chỉ làm Instagram unavailable; không được đánh dấu kết nối Meta/Facebook của tenant là invalid hoặc buộc tenant Facebook-only reconnect.
- Public HTTPS media storage và Meta Instagram permissions là **external enablement blockers** cho live E2E, không phải lý do chặn phần code có thể cấu hình/test. Instagram publish và renderer phải configurable, testable và **disabled by default** cho tới khi các điều kiện ngoài hệ thống được provision.

## 4. Bản đồ điểm chạm (từ khảo sát mã nguồn)

### A. Bộ chọn nền tảng đăng bài (FE)
- `src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx:81` — mảng `PLATFORMS`: bỏ `tiktok`, `website`; thêm `instagram`. Kết quả: `facebook`, `zalo`, `instagram`.
- `src/frontend/clawbot-web/src/shared/theme/colors.ts:14` — `PlatformKey` + palette + `normalizePlatform`: thêm `instagram`. **Giữ** `tiktok`/`website` trong bảng màu để badge của item/brief LỊCH SỬ (dữ liệu cũ trong DB) vẫn render — chỉ bỏ khỏi danh sách *chọn được*.

### B. Nguồn trend-scan (ẩn YouTube + TikTok, giữ Google)
- FE `src/frontend/clawbot-web/src/features/content/TrendSettingsDialog.tsx` — gỡ 2 block YouTube + TikTok khỏi form; giữ Google (geo). Bỏ các field state `youTube*`, `tikTok*`.
- Backend `src/api/Clawbot.Api/Endpoints/ContentTrendSettingsEndpoints.cs` — **giữ nguyên mapping** (code dormant). NHƯNG: `TrendSettingsDialog`/DTO default `YouTube.enabled = true` (dòng 209 endpoint + dòng 36 dialog). Vì UI bị ẩn nên tenant không tắt được → **thêm guard backend**: khi build danh sách nguồn quét, ép `youtube`/`tiktok` = disabled bất kể setting (điểm chặn thực sự, xem C/§trend-scan bên dưới).
- `src/shared/Clawbot.SharedKernel/Content/ContentTrendSettings.cs` — giữ record (không đụng).

### C. Backend publishing + guard platform + Instagram
- `src/shared/Clawbot.Infrastructure/Content/Publishing/RoutingSocialPublisher.cs:10` — thêm `instagram` vào nhánh native; thêm nhánh fallback `instagram_not_configured`.
- `src/shared/Clawbot.Infrastructure/Content/Publishing/GraphSocialPublisher.cs`:
  - `PublishAsync` switch (dòng 51): thêm case `"instagram"`.
  - Thêm `PublishInstagramAsync`: (1) resolve IG account — ưu tiên credential DB riêng (`ResolveChannelAsync(tenant,"instagram")`), nếu không có thì dùng Meta-linked IG từ Page; (2) chặn nếu thiếu ảnh → `instagram_media_required`; (3) 2 bước media container + publish; (4) map lỗi token giống FB (`instagram_reconnect_required`).
- `src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaGraphClient.cs` + `IMetaGraphClient` (dòng 107) — thêm `ResolveInstagramAccountAsync(pageId, pageToken)` (đọc `instagram_business_account`) và `PublishInstagramAsync(igUserId, token, caption, imageUrl)` (2 bước).
- `src/shared/Clawbot.Infrastructure/Integrations/Meta/MetaIntegrationService.cs` — thêm `ResolveInstagramAsync(tenantId, assetId)` trả về `(igUserId, token)` từ Page mặc định/chỉ định.
- **Guard platform ghi mới** `src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs`: allow-list chính xác `{facebook, zalo, instagram}` cho `CreateBrief`, `Generate` và `Repurpose targetPlatforms`. `UpdateBrief` cho phép text-only edit giữ nguyên một platform legacy đã tồn tại, nhưng cấm chọn/chuyển sang platform unsupported. Platform unsupported không bao giờ được dùng ngầm làm generation target; vi phạm → 400 `content.platform_unsupported`. (Chặn cả agent lẫn UI cũ.)
- `src/agents/Clawbot.AgentService/Services/ContentTools.cs:43` — sửa `InputSchemaJson` từ `facebook|instagram|tiktok|youtube|zalo` → `facebook|instagram|zalo`; và guard tương tự trong `ExecuteAsync`.

### D. Cấu hình Instagram (admin, chế độ credential riêng)
- `src/api/Clawbot.Api/Endpoints/AdminSocialCredentialsEndpoints.cs:37` — `Providers` từ `["zalo"]` → `["zalo","instagram"]`; validate + DTO cho instagram (lưu `ig-user-id` + `token` vào `social_credentials`, encrypted, giống zalo).
- FE `src/frontend/clawbot-web/src/features/admin/AdminSocialChannelsSection.tsx` — thêm card Instagram (tùy chọn): nếu để trống thì mặc định chạy chế độ dùng chung Page/FB Graph; nếu nhập thì tách riêng.
- Chế độ mặc định (dùng chung FB Graph) **không cần cấu hình gì thêm** ngoài việc Page đã kết nối Meta có liên kết IG Business.

### E. Luồng lên lịch/đăng (FE)
- `ContentWorkspacePage.tsx` các chỗ `normalize(item.platform) === "facebook"` (1113, 1126, 1501, 1808, 2039) — mở rộng để **instagram cũng chọn Meta Page target** (Page resolve ra IG account). Gom điều kiện thành helper `usesMetaPage(platform)` = `facebook | instagram`.
- Thêm validate FE: item `instagram` không có ảnh → chặn nút Lên lịch + thông báo "IG cần ít nhất 1 ảnh".
- `getContentPublishTargets("facebook")` (1253) — cân nhắc dùng chung target list cho cả IG (cùng Page).

### F. Analytics (thứ yếu)
- FE `src/frontend/clawbot-web/src/features/analytics/AnalyticsReportsPage.tsx:49` — `CHANNELS`: bỏ `tiktok`, `youtube`; thêm `instagram` (+ label/icon dòng 68-95). Backend `KpiAggregator.cs:13` **đã có `instagram`** — không đụng; giữ `tiktok`/`youtube` (dữ liệu lịch sử vô hại).
- **Posture deprecation metric Graph (đã tra v25.0, xác minh 2026-07-22): ClawBot zero exposure.** Hai đợt gỡ Page Insights — page `impressions`/`page_fans` từ 15/11/2025; `post_impressions*` + `page_impressions_unique` sau v25, hiệu lực mọi API version từ 15/06/2026 — không chạm điểm nào của ClawBot:
  - Engagement sync `src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs:55` dùng **edge** `likes.summary(true),comments.summary(true)` (object connection trên post node), KHÔNG phải `/insights?metric=` → nằm ngoài diện gỡ.
  - Ads metrics `src/shared/Clawbot.Infrastructure/Ads/MetaAdsConnector.cs:39` dùng **Ads Insights API** (`{campaign}/insights` với `cpc,impressions,clicks,spend,actions`) — track quản trị riêng, không thuộc Page Insights deprecation.
  - Ràng buộc nếu về sau mở rộng analytics FB: chỉ lấy reach/media-view qua edge hoặc `*_media_view`, **tuyệt đối không** dùng `post_impressions`; tách từng metric thành call riêng vì `/insights` gộp mà dính 1 metric hỏng sẽ `(#100)` fail cả request, và cutover sang `post_media_view` **không backfill** (chuỗi lịch sử sẽ đứt gãy).

## 5. Chia phase thực thi

| Phase | Nội dung | Rủi ro |
|-------|----------|--------|
| **P1 — Ẩn (đảo ngược được)** | PLATFORMS picker (bỏ tiktok/website), TrendSettingsDialog (bỏ YT/TikTok), Analytics CHANNELS, guard platform ở ContentEndpoints + ContentTools schema, guard trend-scan ép YT/TikTok off | Thấp |
| **P2 — Instagram publish lõi** | MetaGraphClient (resolve IG + publish 2 bước), MetaIntegrationService, GraphSocialPublisher nhánh instagram, RoutingSocialPublisher route, guard media-required; capability IG tách khỏi health FB | Trung bình |
| **P2.5 — Renderer + public-media handoff** | Template slot → JPEG; attach asset trước review; public-media resolver từ durable asset identity; disabled by default tới khi external enablement sẵn sàng | Trung bình |
| **P3 — Credential IG riêng + admin** | AdminSocialCredentials providers + FE section | Thấp |
| **P4 — FE lịch/đăng IG** | usesMetaPage helper, target selection, validate ảnh, badge/màu instagram | Thấp |
| **P5 — Test + docs + deploy** | Unit/integration + cập nhật docs + ghi chú migration nếu cần | Thấp |

## 6. Kiểm thử

- `GraphSocialPublisherTests`: IG happy path 2 bước; `instagram_media_required` khi thiếu ảnh; `instagram_not_linked` khi Page chưa liên kết IG; nhánh credential riêng.
- `RoutingSocialPublisher`: route `instagram` → native; `instagram_not_configured` → fallback.
- `ContentEndpoints` (integration): create/generate/repurpose với `tiktok`/`youtube`/`website` → 400 `content.platform_unsupported`; `instagram` → OK; text-only edit của brief legacy giữ nguyên platform cũ → OK; đổi brief đó sang một platform unsupported khác hoặc generation không nêu target để ngầm dùng platform cũ → bị chặn.
- Trend-scan: YT/TikTok bị ép off dù setting còn bật.
- FE: `PLATFORMS` chỉ còn 3; TrendSettingsDialog không còn YT/TikTok; badge item cũ (tiktok) vẫn render.
- E2E frontend dùng **Node Playwright hiện có**. Repo chưa có `Microsoft.Playwright` .NET hoặc Chromium cho renderer; không tính renderer .NET là test/runtime dependency sẵn có.

## 7. Rủi ro / điểm cần xác nhận khi làm

1. **IG video async**: container video cần poll status; scope P2 chỉ làm **ảnh** trước (đăng ngay), video để sau.
2. **GoldenHourResolver** (`item.Platform` → giờ vàng, ContentEndpoints:1375): kiểm tra `ResolveNext` xử lý `instagram` không rơi lỗi — thêm entry nếu cần.
3. **Dữ liệu lịch sử** platform=`tiktok`/`website` trong DB: giữ render badge (đã tính ở A/F), không cần migrate.
4. **Quyền publish IG**: app Meta cần scope `instagram_basic` + `instagram_content_publish` và page-discovery scope phù hợp. Đây là capability riêng: tenant thiếu scope IG vẫn dùng Facebook bình thường; chỉ surface trạng thái Instagram unavailable/reconnect-required cho nhánh IG. Live enablement cần app review/re-consent nếu scope chưa có, còn code path vẫn triển khai/test bằng stub và feature flag mặc định off.
5. **Rate limit IG**: 25 bài/24h/tài khoản — thấp hơn FB; nên log rõ khi chạm giới hạn.

## 8. Việc KHÔNG làm (giữ nguyên)

- Không xóa `YouTubeDataApiSource`/`TikTokScrapeSource`, cột trend, `KpiAggregator` (chế độ ẩn — giữ code).
- Không đụng đường engagement/auto-reply (Pancake) — ngoài phạm vi.

## 9. Component bổ sung: Render ảnh từ template (agent tạo content → gen ảnh → review → đăng)

**Bối cảnh:** Agent điền dữ liệu vào template ràng buộc, hệ thống render ra **JPEG chuẩn** rồi đính kèm như một asset server-owned. Dùng chung cho **FB, IG, Zalo**. Với IG, card ảnh là đường mặc định để thỏa ràng buộc *bắt buộc media*.

### 9.1 Quyết định thiết kế (chốt)

| # | Quyết định | Lý do |
|---|-----------|------|
| R1 | **Template ràng buộc, KHÔNG cho agent xuất HTML tự do** | Agent chỉ điền slot (`headline / body / ảnh nền / brand token`) vào template Scriban/Razor. Giữ nhất quán thương hiệu, tránh markup vỡ, test được bằng golden-image snapshot. |
| R2 | **Renderer dùng headless Chromium; lựa chọn .NET cần thêm dependency** | Repo hiện chỉ có **frontend Node Playwright**. `Microsoft.Playwright` cho .NET và Chromium browser binary **chưa được cài**; implementation phải thêm package/audit, browser install và deploy step, hoặc tách renderer service nếu không muốn tăng host image. Không được mô tả đây là hạ tầng sẵn có. |
| R3 | **Output chuẩn là JPEG** | FB/IG/Zalo nhận JPEG phổ biến; renderer chuẩn hóa MIME `image/jpeg`, quality và kích thước theo preset. PNG không phải output mục tiêu của feature này. |
| R4 | **Public-media delivery dùng storage PUBLIC HTTPS (không auth)** | FB/IG fetch ảnh qua URL và không truy cập endpoint asset có auth. Đây là external enablement blocker cho live publish. |
| R5 | **Renderer chạy trước Agent review/approval** | Đính asset làm tăng content revision và vô hiệu review/approval cũ; vì vậy render sau approval là sai invariant. |
| R6 | **Render tách khỏi request đồng bộ, configurable và disabled by default** | Chromium nặng; code và test vẫn triển khai được khi storage/Meta chưa provision, nhưng live path chỉ bật sau enablement. |

### 9.2 Mắt xích hạ tầng và public-media handoff

- `IStorage` + Docs storage (`src/agents/Clawbot.Agents.Core/Docs/DocsServices.cs:169` — `LocalDiskStorage`, `PublicBaseUrl`, MinIO để dành) là primitive gần nhất để lưu bytes.
- URL hiện tại của `content_assets` phục vụ qua endpoint API có auth nên Meta không fetch được. Card phải có object/delivery representation trong bucket/CDN public HTTPS với ownership, hash và revision vẫn do server quản lý.
- Publish attempt bình thường snapshot **asset identity/metadata**, không snapshot URL public. `GraphSocialPublisher.FirstImageUrl(AssetsJson)` chỉ hỗ trợ payload compatibility đã có URL; nó **không tự giải quyết scheduled publishing** theo durable asset snapshot.
- Bổ sung requirement **public-media resolution/handoff**: từ asset identity + hash + revision đã snapshot, resolve ra một delivery URL HTTPS ổn định, đủ thời gian để Meta fetch, không tin URL do client gửi, và fail closed trước provider call nếu không thể tạo/kiểm chứng handoff. Việc resolve delivery representation không được làm đổi content revision.

### 9.3 Pipeline đúng revision

```text
generate/update template slots
  → RenderCardJob (tenantId explicit) render JPEG
  → lưu asset server-owned + attach vào item
  → increment revision / invalidate review-approval cũ / enqueue review revision mới
  → Agent review (text + asset theo capability)
  → automatic hoặc human approval
  → schedule
  → publish claim snapshot asset identity/hash/revision
  → public-media resolution/handoff
  → Graph publisher dùng delivery URL để đăng
```

Render/storage/handoff lỗi giữ item ngoài publish path; riêng IG tiếp tục trả lỗi rõ `instagram_media_required` hoặc media-handoff error. Tỉ lệ: FB feed 1200×630, IG feed 1080×1080 hoặc 1080×1350, Zalo article khoảng 1200×630.

### 9.4 Điểm chạm dự kiến

- **Mới:** `IHtmlToImageRenderer` abstraction + implementation Chromium trong Infrastructure; template brand; `RenderCardJob` chạy sau generation/revision edit và **trước review/approval**.
- **Asset/storage:** ghi JPEG vào asset server-owned; thêm public bucket/CDN delivery key hoặc resolver riêng; không dùng `AssetsJson` làm authorization/source of truth.
- **Publish:** thêm public-media resolver/handoff giữa durable asset snapshot và Meta call; URL tuyệt đối HTTPS phải được kiểm chứng trước external call.
- **DI/deploy:** nếu chọn `Microsoft.Playwright`, thêm NuGet package, cài Chromium và bundle font có chủ đích; hiện chưa có các dependency này. Không mặc định sửa `run-all.bat` cho đến khi chọn host/deploy strategy.
- **Agent:** prompt/tool chỉ sinh slot data; caption text vẫn được giữ riêng khỏi visual template.

### 9.5 Kiểm thử

- Renderer unit/contract: output là JPEG hợp lệ (`image/jpeg` + magic bytes), đúng preset, tiếng Việt và brand font; golden-image dùng ngưỡng sai số phù hợp JPEG.
- Revision integration: attach ảnh render tăng revision, vô hiệu review/approval cũ và tạo review task mới; không có đường `approved → attach asset → publish` giữ nguyên review cũ.
- Publish integration: attempt snapshot asset identity/hash; public-media resolver trả URL ổn định; publisher không phụ thuộc URL nằm sẵn trong snapshot/`AssetsJson`.
- Failure: render timeout, storage lỗi, URL không public/HTTPS hoặc handoff lỗi → zero Meta calls; IG không rơi vào text-only publish.
- Tooling acceptance: ghi rõ chỉ Node Playwright hiện hữu; test/deploy .NET renderer chỉ chạy sau khi package + Chromium được cài có kiểm chứng.

### 9.6 Blocker và cách triển khai song song

1. **Public HTTPS media storage** và **Meta Instagram permissions** là blocker ngoài hệ thống cho live E2E. Chúng không chặn implementation/config/test bằng fake storage và fake Meta; feature flags mặc định off cho production path mới.
2. **Chromium/.NET Playwright chưa hiện hữu**: cần quyết định host renderer, package audit, image-size budget và browser-install strategy trước khi bật renderer thật.
3. **Font tiếng Việt**: bundle font brand để output nhất quán giữa dev/CI/deploy.
4. **Chi phí render**: dùng concurrency cap, browser reuse/pool và timeout để tránh tiến trình Chromium chồng khi backlog tăng.

### 9.7 Phạm vi P-render (chèn vào lộ trình)

Đưa thành **Phase 2.5** về mặt triển khai dependency (sau plumbing IG lõi, trước UI publish), nhưng trong workflow runtime renderer luôn nằm **trước review/approval**. Code renderer, public-media abstraction và tests có thể tiến hành ngay ở trạng thái disabled-by-default; chỉ live-enable sau khi public storage, browser runtime và Meta scopes sẵn sàng.

## 10. Trạng thái task đã reconcile

### Done
- [x] Khảo sát repository và research các điểm chạm Content/Meta/asset/testing.
- [x] Architecture review và các quyết định scope: đúng 3 platform writable; legacy read-only; renderer trước review; JPEG; capability IG tách FB.
- [x] Tra & xác minh posture deprecation metric Graph API v25.0 (đợt 15/11/2025 + 15/06/2026): ClawBot zero exposure — engagement sync dùng edge `summary(true)`, ads dùng Ads Insights riêng (chi tiết §4.F).

### In Progress
- [ ] P1 guard/bề mặt chỉ cho `facebook|zalo|instagram`, gồm semantics text-only edit cho brief legacy.
- [ ] P2/P3/P4 Instagram publish, capability/config admin và UI target selection.
- [ ] P2.5 renderer JPEG + durable public-media handoff, triển khai configurable và disabled by default.
- [ ] Unit/integration/Node Playwright regression theo §6 và §9.5.

### Blocked (external enablement only)
- [ ] Live Meta Instagram E2E: chờ app permissions/re-consent và IG Business liên kết Page.
- [ ] Live media publish/render E2E: chờ public HTTPS bucket/CDN và browser runtime được provision.

### Newly Discovered Work
- [ ] Thêm public-media resolution/handoff từ asset identity snapshot; không dựa vào URL trong `AssetsJson`/`FirstImageUrl`.
- [ ] Chọn và cài renderer runtime vì hiện chỉ có frontend Node Playwright, chưa có .NET Playwright/Chromium.
- [ ] Tách Instagram capability/permission health khỏi Facebook connectivity để tenant Facebook-only không bị invalid toàn cục.

**Next 3:** hoàn tất closed-set guards + legacy edit tests; hiện thực IG capability/publish behind default-off config; sau đó làm renderer JPEG và public-media handoff trước khi mở live E2E. Tiếp tục bằng `/execute-plan`; khi mọi implementation task hoàn tất, chạy `/check-implementation`.
