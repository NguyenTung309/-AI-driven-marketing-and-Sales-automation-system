# Kế hoạch: Dashboard hiệu quả bài đăng (Facebook + Instagram)

> Ngày lập: 2026-08-04 · Trạng thái: ĐANG XÁC MINH TRIỂN KHAI (2026-08-05)
> Nguồn khảo sát: `MetaEngagementSyncJob`, `ContentEndpoints`, `ContentWorkspacePage`, `HangfireModule`
> Liên quan: [2026-07-22-content-platform-focus-zalo-fb-ig.md](2026-07-22-content-platform-focus-zalo-fb-ig.md)

---

## Trạng thái triển khai (2026-08-05)

- [x] GĐ1 API `GET /api/content/post-performance`, DTO và gộp dữ liệu SQL đã hoàn thành; không migration và không sửa job sync.
- [x] `days` là tùy chọn: không truyền, ngoài `1..90` đều trở về 30; `platform` khác Facebook/Instagram trả HTTP 400.
- [x] Bài chỉ được tính số liệu khi cả `LikeCount` và `CommentCount` có giá trị. Giá trị `null` vẫn hiện `—`; số 0 thật vẫn hiện `0`.
- [x] Độ tươi dùng watermark **lần thử đồng bộ cũ nhất** trên toàn tập đã lọc, không coi đó là thời điểm đồng bộ thành công.
- [x] Bảng bài đăng chỉ mở nội dung còn tồn tại; liên kết ngoài chỉ chấp nhận HTTPS tại các host Facebook/Instagram được cho phép.
- [x] Tab, bộ lọc, biểu đồ ngày đăng, làm mới cache và E2E mock đã được thêm; E2E được đưa vào CI cùng các bài Platform/Instagram.
- [x] CI hiện chạy `dotnet test Clawbot.sln --no-build --configuration Release` sau bước build, nên test tổng hợp API không còn chỉ được biên dịch.
- [x] Kiểm tra đã chạy: 11 test tổng hợp API, toàn bộ 196 test .NET (1 skip), `dotnet build -c Release`, frontend lint (0 lỗi, còn 3 cảnh báo cũ) và frontend production build.
- [ ] Chạy Playwright trực tiếp trong Windows harness đang treo ngay cả với `--list`; cần xác nhận E2E trên runner CI/Linux trước khi đóng nghiệm thu.

---

## 0. Vấn đề và các quyết định đã chốt

**Vấn đề.** Số like/comment của bài đã đăng được đồng bộ đều đặn 15 phút một lần và lưu đầy đủ trong
`content_schedule`, nhưng nơi duy nhất hiển thị là một badge nhỏ trên từng thẻ lịch. Không có chỗ nào
trả lời được: tháng này đăng bao nhiêu bài, bài nào hiệu quả nhất, Page nào chạy tốt hơn.

Hai màn hình dễ bị nhầm là dashboard nhưng không phải:

- Tab **Chỉ số chuỗi AI** (`/content`) đo cỗ máy sinh nội dung: lượt chạy, token, tỉ lệ fallback, reviewer duyệt.
- **Báo cáo thống kê** (`/analytics`) đo hội thoại và lead: omnichannel, funnel, agent, cost, forecast, anomaly.

Không màn hình nào đọc `like_count` / `comment_count`.

| # | Vấn đề | Quyết định |
|---|---|---|
| QĐ-1 | Đặt ở đâu | Tab thứ tư **Hiệu quả bài đăng** trong `/content`, cạnh Hàng đợi / Lịch xuất bản / Chỉ số chuỗi AI. Đặt ở đây vì người dùng vừa lên lịch xong là xem được kết quả, không phải nhảy màn hình. `/analytics` giữ nguyên phạm vi hội thoại và lead. |
| QĐ-2 | Chia giai đoạn | **GĐ1** dùng đúng dữ liệu đang có (like, comment) — không sửa job, không migration, ship được ngay. **GĐ2** mở rộng job sync để có thêm share và tổng reaction. |
| QĐ-3 | Nguồn số liệu | Chỉ dùng **object edge** trên node bài viết. **Cấm** dùng Page Insights (`/insights?metric=...`). Xem mục 2.1. |
| QĐ-4 | API | Một endpoint mới `GET /content/post-performance`, quyền `content:read`, theo đúng mẫu `/chain-metrics`. |
| QĐ-5 | Nơi tính toán | Gộp số **trong SQL** bằng `GroupBy`, không kéo toàn bộ row về bộ nhớ. `ChainMetricsAsync` đang kéo hết row về ([ContentEndpoints.cs:835-838](../../../src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs#L835-L838)); bảng lịch lớn hơn trace nhiều nên không lặp lại cách đó. |
| QĐ-6 | Lưu trữ | GĐ1 đọc thẳng `content_schedule`, **không** thêm bảng. Bảng lịch sử engagement chỉ đặt ra ở GĐ2 nếu thực sự cần đường xu hướng theo ngày đo. |
| QĐ-7 | Trục thời gian | Xu hướng vẽ theo **ngày đăng bài**, không phải ngày đo. Lý do ở mục 2.2 — đây là ràng buộc bắt buộc, không phải lựa chọn thẩm mỹ. |
| QĐ-8 | Kênh | Chỉ Facebook và Instagram có số liệu. Zalo và các bài đăng qua Pancake **không có** và phải nói rõ trên giao diện, không hiển thị số 0 gây hiểu nhầm. |

---

## 1. Hiện trạng — bản đồ điểm chạm

Đường ống dữ liệu đã thông suốt, chỉ thiếu lớp tổng hợp và giao diện.

| Tầng | Vị trí | Nội dung |
|---|---|---|
| Job | [MetaEngagementSyncJob.cs:26](../../../src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L26) | Quét 100 bài mỗi lượt, sắp theo `EngagementSyncedAt` tăng dần (NULL lên đầu nên bài chưa đo bao giờ được ưu tiên) |
| Job | [MetaEngagementSyncJob.cs:94](../../../src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L94) | Facebook lấy `likes.summary(true),comments.summary(true)` |
| Job | [MetaEngagementSyncJob.cs:182](../../../src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L182) | Instagram lấy `like_count,comments_count` |
| Lịch chạy | [HangfireModule.cs:213-217](../../../src/shared/Clawbot.Infrastructure/Jobs/HangfireModule.cs#L213-L217) | `meta-engagement-sync`, cron `*/15 * * * *` |
| Miền | [ContentSchedule.cs](../../../src/shared/Clawbot.Domain/Content/ContentSchedule.cs) | `LikeCount`, `CommentCount`, `EngagementSyncedAt`; ghi qua `SetEngagement` |
| API | [ContentEndpoints.cs:1667-1669](../../../src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs#L1667-L1669) | `ToDto` trả ba trường ra ngoài |
| API | [ContentEndpoints.cs:1691-1692](../../../src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs#L1691-L1692) | `BuildCalendarRows` trả like/comment cho lịch |
| FE kiểu | [content.ts:114-116](../../../src/frontend/clawbot-web/src/shared/api/content.ts#L114-L116) | `likeCount`, `commentCount`, `engagementSyncedAt` |
| FE hiển thị | [ContentWorkspacePage.tsx:1232-1243](../../../src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx#L1232-L1243) | **Chỗ duy nhất** — badge `thumb_up N` / `mode_comment N` trên thẻ lịch |
| FE tab | [ContentWorkspacePage.tsx:59](../../../src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx#L59) | `type ContentWorkspaceTab = "queue" \| "calendar" \| "metrics"` — cần thêm nhánh thứ tư |

Điểm cần sửa khi thêm tab: khai báo kiểu dòng 59, nút tab quanh dòng 2148-2156, panel quanh dòng 2242,
và đoạn đọc `tabParam` dòng 1514 (hiện chỉ chấp nhận `calendar` và `metrics`).

---

## 2. Ba ràng buộc bắt buộc đọc trước khi code

### 2.1 Không được chạm vào Page Insights

Meta đang khai tử bộ chỉ số Page Insights theo hai đợt:

| Mốc | Chỉ số bị bỏ | Thay bằng |
|---|---|---|
| 15/11/2025 (đã có hiệu lực) | `impressions` cấp Page, `page_fans` | `views`, `page_media_view`, `page_follows` |
| 15/06/2026, áp cho **mọi phiên bản API** | `post_impressions`, `post_impressions_unique`, `page_impressions_unique` | `post_media_view`, `page_media_view` |

Hệ thống hiện **không dính** đợt nào, vì đếm tương tác bằng object edge trên node bài viết
(`likes.summary(true)`) chứ không gọi `/insights`. Chỗ duy nhất trong mã nguồn gọi `/insights` là
[MetaAdsConnector.cs:36](../../../src/shared/Clawbot.Infrastructure/Ads/MetaAdsConnector.cs#L36) — đó là
Ads Insights trên node chiến dịch, thuộc nhánh quản trị khác, không bị ảnh hưởng.

Hai điều rút ra:

1. **Dashboard không được vẽ ô "Lượt tiếp cận" hay "Hiển thị".** Không có dữ liệu đó, và cách lấy nó
   (`post_impressions`) chính là thứ sắp chết. Muốn có thì phải chuyển sang `post_media_view` — việc riêng, không nằm trong kế hoạch này.
2. **Gọi insights theo lô là được ăn cả ngã về không.** Một chỉ số sai làm hỏng toàn bộ request với lỗi
   `(#100) must be a valid insights metric`. Nếu sau này có thêm insights thì mỗi chỉ số một request, hoặc bắt lỗi từng chỉ số.

### 2.2 Không có lịch sử — chỉ có ảnh chụp hiện tại

`SetEngagement` **ghi đè** giá trị cũ mỗi 15 phút. Không có bảng nào giữ lại chuỗi giá trị theo thời gian.

Hệ quả: câu hỏi "bài này hôm qua được bao nhiêu like, hôm nay tăng bao nhiêu" **không trả lời được**, và
không có cách nào lách. Vì vậy QĐ-7 chốt trục thời gian là **ngày đăng bài**: mỗi cột trong biểu đồ là
"các bài đăng ngày đó hiện đang có tổng bao nhiêu tương tác". Đây là con số hợp lệ và hữu ích, nhưng phải
đặt nhãn đúng, tránh để người đọc tưởng là tăng trưởng theo ngày.

Nếu sau này thực sự cần đường tăng trưởng thì phải thêm bảng `content_schedule_engagement_history`
(mỗi lượt sync ghi một dòng) — việc này để GĐ2 cân nhắc, không làm ở GĐ1.

### 2.3 Chỉ Facebook và Instagram có số liệu

Job lọc đúng hai nền tảng này ([MetaEngagementSyncJob.cs:31-33](../../../src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L31-L33)),
và bài phải có `ExternalPostId` hoặc `PostUrl` phân tích ra được id hợp lệ. Bài Zalo, và bài đăng qua
Pancake không trả về id bài, sẽ không bao giờ có số.

Giao diện phải phân biệt rõ ba trạng thái, không được gộp thành số 0:

- **Chưa đo lần nào** — `engagementSyncedAt` null. Hiển thị dấu gạch ngang và chú thích "chưa đồng bộ".
- **Đã đo, bằng 0** — bài thật sự không có tương tác. Hiển thị `0`.
- **Kênh không hỗ trợ** — Zalo. Hiển thị "không có số liệu".

---

## 3. Giai đoạn 1 — dashboard trên dữ liệu sẵn có

### 3.1 Các chỉ số hiển thị

Bộ lọc đầu trang: **Khoảng thời gian** (7 / 30 / 90 ngày, mặc định 30) và **Kênh** (Tất cả / Facebook / Instagram).

| Khối | Nội dung | Ghi chú |
|---|---|---|
| Thẻ tổng quan | Số bài đã đăng · Tổng lượt thích · Tổng bình luận · TB tương tác mỗi bài | Bốn ô `MetricTile`, dùng lại thành phần của tab Chỉ số chuỗi AI |
| Độ tươi dữ liệu | "Đã đồng bộ N/M bài · lần đo cũ nhất lúc HH:mm" | Bắt buộc có. Thiếu ô này người xem không biết số đang cũ tới mức nào |
| Xếp hạng bài | Bảng 10 bài tương tác cao nhất: trích đoạn nội dung, kênh, ngày đăng, thích, bình luận, tổng | Bấm mở bài; có liên kết ra `postUrl` |
| Theo kênh | Facebook so với Instagram: số bài, thích, bình luận, TB mỗi bài | |
| Theo Page | Nhóm theo `MetaAssetId`, kèm tên Page | Cần join sang bảng tài sản Meta để lấy tên; thiếu tên thì hiện id |
| Xu hướng | Cột theo ngày đăng: số bài và tổng tương tác | Nhãn phải ghi rõ "theo ngày đăng" (mục 2.2) |

Trạng thái rỗng: nếu kỳ đang chọn chưa có bài `posted` nào thì hiện hướng dẫn ngắn dẫn sang tab Lịch xuất bản,
không hiện bảng trống.

### 3.2 API mới

`GET /content/post-performance?days=30&platform=facebook` · quyền `content:read` ·
đăng ký cạnh [ContentEndpoints.cs:79](../../../src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs#L79).

Tham số: `days` chặn trong khoảng 1..90, ngoài khoảng thì về 30 (theo đúng cách `ChainMetricsAsync` xử lý
`days`). `platform` không truyền nghĩa là tất cả.

Hình dạng phản hồi:

```
{
  windowDays, from, to,
  totals:    { posts, likes, comments, avgEngagementPerPost },
  freshness: { syncedPosts, unsyncedPosts, oldestSyncedAt },
  byPlatform: [{ platform, posts, likes, comments }],
  byTarget:   [{ metaAssetId, targetName, posts, likes, comments }],
  daily:      [{ date, posts, likes, comments }],
  topPosts:   [{ scheduleId, contentItemId, platform, excerpt, postUrl, postedAt, likes, comments, total }]
}
```

Điều kiện lọc: `Status == "posted"`, `PostedAt` nằm trong kỳ, nền tảng thuộc facebook/instagram.

Ba điểm phải làm đúng:

- **Gộp trong SQL.** `byPlatform`, `byTarget`, `daily`, `totals` viết bằng `GroupBy` trên `IQueryable`.
  Chỉ `topPosts` được `Take(10)` rồi mới join sang `ContentItem` lấy trích đoạn nội dung.
- **`freshness` đếm trên cùng tập lọc**, không đếm toàn bảng, nếu không con số sẽ vô nghĩa khi lọc theo kênh.
- **`null` khác `0`.** Bài chưa đo có `LikeCount` null; khi cộng tổng phải bỏ qua, và đếm riêng vào
  `unsyncedPosts`. Không được `?? 0` rồi cộng — làm vậy là bịa ra số 0 giả.

Kiểu DTO đặt cùng chỗ với các DTO nội dung hiện có trong
[ContentDtos.cs](../../../src/api/Clawbot.Api.Contracts/Content/ContentDtos.cs).

### 3.3 Frontend

| Việc | Vị trí |
|---|---|
| Thêm `"performance"` vào kiểu tab | [ContentWorkspacePage.tsx:59](../../../src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx#L59) |
| Cho `tabParam` nhận giá trị mới | [ContentWorkspacePage.tsx:1514](../../../src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx#L1514) |
| Nút tab **Hiệu quả bài đăng** | quanh dòng 2148-2156, giữ nguyên `aria-selected` / `aria-controls` như ba tab cũ |
| Panel `role="tabpanel"` | quanh dòng 2242 |
| Kiểu + hàm gọi API | [content.ts](../../../src/frontend/clawbot-web/src/shared/api/content.ts) — thêm `PostPerformance*` và `getPostPerformance` |
| Truy vấn | `useQuery` với `queryKey: ["content", "post-performance", days, platform]` |

Cạm bẫy về `queryKey`: đã từng có sự cố dùng chung key giữa `useInfiniteList` và `useQuery` làm
`data.items` thành `undefined` rồi vỡ giao diện. Key trên là key mới, không trùng với `["content", "chain-metrics", ...]`
hay key của hàng đợi — giữ nguyên như vậy.

Biểu đồ theo hệ màu và thành phần sẵn có của `/analytics`, không thêm thư viện mới.
Dùng biểu tượng material trung tính (`thumb_up`, `mode_comment`, `trending_up`), không dùng biểu tượng kiểu emoji.

---

## 4. Giai đoạn 2 — mở rộng chỉ số

Chỉ làm sau khi GĐ1 đã chạy thật và có phản hồi. Ba việc, độ chắc chắn giảm dần:

**4.1 Thêm lượt chia sẻ và tổng reaction cho Facebook.**
Đổi `fields` tại [MetaEngagementSyncJob.cs:94](../../../src/shared/Clawbot.Infrastructure/Jobs/MetaEngagementSyncJob.cs#L94)
thành `likes.summary(true),comments.summary(true),shares,reactions.summary(true)`.
`shares` trả về `{ "count": N }` và `reactions.summary(true)` trả tổng số reaction mọi loại — đều là trường/edge
trên node bài viết nên không dính deprecation. **Phải xác minh bằng một lần gọi thật trên Page thử trước khi viết code.**

Tách reaction theo từng loại (thương, haha, wow...) cần sáu request `reactions.type(LIKE).summary(true)`
riêng biệt — chi phí gấp sáu. Không làm, trừ khi có yêu cầu nghiệp vụ rõ ràng.

**4.2 Instagram.** IG media không có khái niệm chia sẻ. Các chỉ số như lượt lưu hay lượt tiếp cận nằm ở
IG Media Insights — **chưa xác minh** nhánh này có nằm trong đợt khai tử nào không. Phải tra tài liệu Meta
và thử thật trước khi đưa vào kế hoạch. Không giả định là dùng được.

**4.3 Bảng lịch sử.** Chỉ làm nếu người dùng thực sự cần đường tăng trưởng theo ngày đo (mục 2.2).

### Ba cạm bẫy migration đã biết của dự án

GĐ2 cần thêm cột nên phải theo đúng ba quy tắc dưới, đều là lỗi đã từng gặp:

1. **Không có `GO`.** Mỗi file migration chạy như một `SqlCommand` duy nhất. File tiếp theo là `0095_`.
2. **Chỉ mục trên cột vừa thêm phải nằm ở file riêng.**
3. **Phải thêm vào khối repair trong `run-all.bat`.** `run-all.bat` chỉ replay `*.sql` khi database còn trống;
   máy đã có schema thì chạy khối lệnh vá cứng ở [run-all.bat:706-709](../../../run-all.bat#L706-L709).
   Quên bước này thì cột mới không xuất hiện trên máy đang chạy, và triệu chứng là dữ liệu im lặng không lên.

---

## 5. Các giai đoạn thực hiện

| GĐ | Nội dung | Kết quả bàn giao |
|---|---|---|
| 1 | Endpoint `/content/post-performance` + DTO + test gộp số (kỳ rỗng, bài chưa sync, lọc theo kênh) | API trả đúng trên dữ liệu thật |
| 2 | Tab **Hiệu quả bài đăng**: thẻ tổng quan, độ tươi dữ liệu, bảng xếp hạng | Tab dùng được, có trạng thái rỗng |
| 3 | Khối theo kênh, theo Page, biểu đồ xu hướng | Tab hoàn chỉnh |
| 4 | Kiểm thử E2E theo mẫu mock sẵn có; soi lại nhãn và trạng thái rỗng | E2E xanh |
| 5 | Xác minh `shares` + `reactions.summary(true)` trên Page thử, rồi mới sửa job và thêm migration `0095` | Chỉ số mở rộng (chỉ khi 4.1 xác minh được) |

GĐ 1-4 không đụng tới job, không migration, không rủi ro dữ liệu. GĐ5 tách riêng vì có thay đổi schema.

---

## 6. Rủi ro

| Rủi ro | Mức | Cách xử lý |
|---|---|---|
| Cộng `null` thành `0` làm tổng bị thổi phồng và TB bị kéo xuống | Cao | Bỏ qua null khi cộng; đếm riêng vào `unsyncedPosts`; có test cho trường hợp lô toàn bài chưa sync |
| Người xem hiểu nhầm biểu đồ là tăng trưởng theo ngày | Cao | Nhãn ghi rõ "theo ngày đăng"; có chú thích dưới biểu đồ |
| Kéo toàn bộ row lịch về bộ nhớ rồi mới gộp | Trung bình | QĐ-5: gộp trong SQL; xem lại truy vấn sinh ra trước khi merge |
| Thêm ô "Lượt tiếp cận" vì thấy thiếu | Trung bình | Mục 2.1 cấm rõ; người rà soát phải chặn |
| Bài Zalo hiện 0 tương tác gây hiểu nhầm là bài kém | Trung bình | Mục 2.3: ba trạng thái tách bạch |
| GĐ2 thêm cột nhưng quên khối repair `run-all.bat` | Trung bình | Mục 4 quy tắc 3; kiểm tra trên máy đã có schema, không chỉ trên database mới |
| Sync 15 phút một lô 100 bài, tenant nhiều bài thì số liệu cũ | Thấp | Ô độ tươi dữ liệu cho thấy ngay; cần thì tăng `BatchSize` |

---

## 7. Checklist nghiệm thu

- [ ] Tab **Hiệu quả bài đăng** mở được và giữ nguyên khi tải lại trang (tham số tab trên URL hoạt động).
- [ ] Bốn thẻ tổng quan khớp với số đếm tay trên cùng khoảng thời gian.
- [ ] Bài chưa đồng bộ hiện gạch ngang, không hiện `0`.
- [ ] Ô độ tươi dữ liệu hiển thị đúng số đã/chưa đồng bộ và thời điểm đo cũ nhất.
- [ ] Bảng xếp hạng mở đúng bài và liên kết `postUrl` đúng.
- [ ] Lọc theo kênh làm đổi cả sáu khối, kể cả ô độ tươi dữ liệu.
- [ ] Kỳ không có bài nào hiện trạng thái rỗng có hướng dẫn, không hiện bảng trống.
- [ ] Không có chuỗi `post_impressions`, `page_impressions`, `page_fans` nào trong mã nguồn.
- [ ] Truy vấn sinh ra là truy vấn gộp, không phải `SELECT` toàn bảng rồi gộp trong bộ nhớ.
- [ ] `queryKey` mới không trùng với key nào đang dùng.
- [ ] Không có emoji trong nhãn giao diện.
- [ ] Người không có quyền `content:read` bị chặn ở endpoint.
