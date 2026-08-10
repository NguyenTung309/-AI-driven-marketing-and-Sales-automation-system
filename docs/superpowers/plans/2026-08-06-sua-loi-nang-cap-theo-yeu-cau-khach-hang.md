# Plan: Sửa lỗi và nâng cấp theo 6 yêu cầu khách hàng

Ngày lập: 2026-08-06
Trạng thái: chờ duyệt (chưa viết code)
Nguồn: 6 yêu cầu khách hàng kèm ảnh chụp màn hình (mục 1, 2, 3.1, 3.2, 5, 6 — không có mục 4)

---

## 0. Tóm tắt điều hành

| # | Yêu cầu | Bản chất | Mức độ | Lớp ảnh hưởng | Ước lượng |
|---|---------|----------|--------|---------------|-----------|
| 1 | Gỡ "Chi phí quảng cáo" + "Doanh thu" khỏi báo cáo | Thu hẹp phạm vi tính năng | Thấp | FE (2 màn) + export | 0.5 ngày |
| 2a | Upload ảnh lỗi định dạng, không hiển thị ảnh | Lỗi hạ tầng lưu trữ + allowlist | **Cao** | BE storage + endpoint + FE | 1.5 ngày |
| 2b | Hiển thị list hot key / hot search sau khi quét | Thiếu tính năng (dữ liệu bị vứt) | Trung bình | Agent + BE + FE | 1.5 ngày |
| 3.1 | Tạm dừng luồng, sửa output task, chạy tiếp | Thiếu tính năng + lãng phí chi phí | **Cao** | Domain + gRPC + BE + FE | 2.5 ngày |
| 3.2 | Ô kết quả người dùng đọc được | Thiếu tính năng UI | Thấp | FE (1 component) | 0.5 ngày |
| 5 | Xóa bản KB chưa phát hành | Thiếu endpoint | Trung bình | BE + Qdrant + FE | 1 ngày |
| 6 | Chọn ngày giờ đăng bài luôn báo lỗi | Lỗi hiển thị lỗi + lệch hợp đồng | **Cao** | FE 1 file (gấp) + BE | 1 ngày |

Phát hiện quan trọng nhất: yêu cầu 6 gần như chắc chắn **không phải lỗi bộ chọn ngày giờ**. API trả về mã lỗi và câu tiếng Việt cụ thể, nhưng frontend vứt toàn bộ nội dung phản hồi và luôn in câu chung "Thông tin gửi lên chưa hợp lệ" cho mọi lỗi 400. Sửa 1 hàm ở frontend sẽ lộ ngay nguyên nhân thật.

### Quyết định cần chốt trước khi code

- **QĐ-1 — Phạm vi gỡ Ads/Doanh thu.** Khuyến nghị: gỡ **lớp 1 + lớp 2** (giao diện + file xuất báo cáo), **giữ nguyên** database, job nền, agent Ads và tab "Doanh thu" trong hồ sơ Lead. Lý do ở mục 1.
- **QĐ-2 — Cách phục vụ ảnh.** Khuyến nghị: thêm endpoint đọc ảnh có kiểm quyền theo tenant, **không** bật static file. Lý do ở mục 2a.
- **QĐ-3 — Cách giảm chi phí Orchestrator.** Khuyến nghị: làm cả hai pha, pha A (tạm dừng chờ người sửa) trước, pha B (giữ lại task đã xong khi buộc phải lập lại kế hoạch) sau.
- **QĐ-4 — Xóa bản KB.** Khuyến nghị: xóa cứng kèm nhiều lớp chặn, vì entity `KbVersion` không có cột `DeletedAt` lẫn `TenantId`; thêm soft-delete sẽ kéo theo migration và sửa 8+ nơi đang đọc bảng này.

---

## 1. Yêu cầu 1 — Gỡ "Chi phí quảng cáo" và "Doanh thu"

### 1.1 Hiện trạng

Khách chỉ chụp màn hình "Báo cáo thống kê", nhưng hai chỉ số này xuất hiện ở **hai màn hình** và trong **file xuất báo cáo**:

`src/frontend/clawbot-web/src/features/analytics/AnalyticsReportsPage.tsx`
- dòng 37-38: trường `adSpend`, `revenue` trong `AggregateMetrics`
- dòng 84-86: nhãn "Chi phí quảng cáo", "Doanh thu", "Chi phí/lead"
- dòng 142-143: cộng dồn `row.adSpend`, `row.revenue`
- dòng 256-257: ô "Chi phí/lead" trong lưới KPI theo kênh
- dòng 593: truy vấn bất thường theo `metric: "cpl"`
- dòng 645-655: hai thẻ chỉ số "Chi phí quảng cáo" và "Doanh thu"
- dòng 759: thẻ "Chi phí/lead trung bình"

`src/frontend/clawbot-web/src/features/dashboard/DashboardPage.tsx`
- dòng 85, 87: nhãn "Chi phí quảng cáo", "Chi phí/lead"
- dòng 113, 121-122: cộng dồn `adSpend` và tính `cpl`
- dòng 188: "{tiền} / lead" trong danh sách kênh
- dòng 446-447: thẻ bất thường theo `metric: "cpl"`
- dòng 508: câu tóm tắt "…tổng chi phí {tiền}."

`src/api/Clawbot.Api/Services/AnalyticsExportService.cs`
- dòng 40-41: cột CSV `AdSpend`, `Cpl`
- dòng 95-96: ô tương ứng trong bảng PDF

`src/api/Clawbot.Api/Services/AnalyticsAggregationService.cs`
- dòng 59-60: hai mục delta `adSpend`, `revenue`

### 1.2 Ba lớp phạm vi

| Lớp | Nội dung | Khuyến nghị |
|-----|----------|-------------|
| Lớp 1 — Giao diện | Xóa thẻ, cột, nhãn, phép tính ở 2 file FE trên | **Làm** |
| Lớp 2 — Xuất báo cáo và delta | Xóa cột CSV/PDF, xóa 2 mục delta | **Làm** (đây vẫn là "thông tin" khách nhìn thấy) |
| Lớp 3 — Dữ liệu và hệ thống Ads | `KpiAggregator`, `KpiDaily`, DTO, các job `WeeklyAdsReportJob`/`AdsDaypart*`/`AdsCreativeRotation*`, `LeadRevenue*`, migration `0073`, `0074` | **Không làm** |

Lý do không làm lớp 3:
- Chạm hơn 20 file backend, 2 migration đã phát hành, và `AppDbContextModelSnapshot`.
- `LeadRevenue` chính là cơ chế "lead thanh toán thì chuyển thành customer" mà khách mô tả trong ngoặc — xóa nó sẽ phá vòng đời lead đã chốt trước đó.
- Xóa cột trong SQL Server là thao tác không hoàn tác được; giữ cột nhưng ẩn giao diện thì bật lại chỉ mất vài phút.

### 1.3 Việc cần làm

1. `AnalyticsReportsPage.tsx`: xóa 2 trường trong `AggregateMetrics`, 3 nhãn, 2 dòng cộng dồn, ô "Chi phí/lead" trong lưới kênh, 2 thẻ chỉ số (645-655), thẻ "Chi phí/lead trung bình" (759).
2. `AnalyticsReportsPage.tsx:593` và `DashboardPage.tsx:446-447`: đổi `metric: "cpl"` sang `metric: "leads"`. Đã xác minh `ReportAgentRunner.cs:107` chấp nhận `"leads"`, nên thẻ bất thường vẫn hoạt động thay vì phải xóa hẳn.
3. `DashboardPage.tsx`: xóa 2 nhãn, biến `adSpend`, trường `cpl`, dòng 188, và sửa câu tóm tắt dòng 508 bỏ phần "tổng chi phí".
4. `AnalyticsExportService.cs`: xóa 2 cột khỏi header + thân CSV (40-41) và bảng PDF (95-96).
5. `AnalyticsAggregationService.cs:59-60`: xóa 2 mục delta.
6. `src/frontend/clawbot-web/src/shared/api/analytics.ts`: giữ `adSpend`/`cpl`/`revenue` ở dạng optional để không vỡ khi backend vẫn trả về; chỉ ngừng đọc.

### 1.4 Tuyệt đối không xóa nhầm

- `AnalyticsReportsPage.tsx:744` — thẻ "**Chi phí AI**" là chi phí gọi mô hình, không phải chi phí quảng cáo. Giữ nguyên.
- `LeadsPage.tsx` — tab "Doanh thu" trong ngăn kéo hồ sơ lead (dòng 30, 312-643, 759-761), cùng `shared/api/leads.ts:145-188` và `shared/api/admin.ts:300-352`. Giữ nguyên toàn bộ.

### 1.5 Kiểm thử

- `pnpm tsc --noEmit` sạch sau khi bỏ trường khỏi interface.
- Mở `/analytics` và `/dashboard`: không còn chữ "Chi phí quảng cáo", "Doanh thu", "Chi phí/lead"; bố cục lưới không bị hụt ô.
- Tải file CSV và PDF: không còn 2 cột; số cột header khớp số cột dữ liệu.
- Mở hồ sơ 1 lead: tab "Doanh thu" vẫn chạy.

---

## 2a. Yêu cầu 2 (phần 1) — Upload ảnh lỗi định dạng, không hiển thị ảnh

Đây là hai triệu chứng của bốn khuyết tật độc lập.

### 2a.1 Nguyên nhân gốc

**RC-1 (chí mạng — gây "không hiển thị ảnh").**
`src/agents/Clawbot.Agents.Core/Docs/DocsServices.cs:192-206`, `LocalDocumentStorage.SaveAsync`:
- gọi `Path.GetFileName(fileName)` → khóa lưu trữ `tenants/{tenant}/content/{item}/{assetId}` bị **làm phẳng** thành `{assetId}`, mọi tenant đổ chung một thư mục;
- bỏ qua `contentType` (`_ = contentType;`) nên file lưu xuống không có đuôi;
- trả về URL `"/generated-docs/{assetId}"`.

Trong khi đó **không có `UseStaticFiles`/`wwwroot` nào trong toàn bộ mã nguồn** phục vụ thư mục `generated-docs`. Thêm nữa URL này là đường dẫn tương đối, trình duyệt sẽ ghép vào origin của frontend chứ không phải origin API. Kết quả: mọi thẻ `<img>` ở `ContentWorkspacePage.tsx:795` và `:1030` trỏ tới một URL 404.

Đã xác minh không có mục cấu hình `Docs:Storage` hay `Minio` nào trong `src/api/Clawbot.Api/*.json` cũng như trong các file `.bat/.ps1/.env`. Theo `src/api/Clawbot.Api/Program.cs:143-150`, MinIO chỉ được đăng ký khi có `Docs:Storage:Minio:Endpoint`, nên **môi trường đang chạy chắc chắn dùng `LocalDocumentStorage`** — tức đang dính RC-1.

**RC-2 (gây "lỗi định dạng").**
`src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs:~395-500` chặn theo `AllowedAssetContentTypes` chỉ gồm `image/gif`, `image/jpeg`, `image/png`, `image/webp`. Trong khi hàm `LooksLikeAllowedImage` ở dòng 2052-2066 lại chấp nhận rộng hơn (`image/jpg`, HEIC, HEIF, AVIF). Hai nguồn sự thật lệch nhau, và nhánh chặn chạy trước. Hệ quả thực tế:
- ảnh chụp từ iPhone (HEIC/HEIF) → 400 `content.asset_invalid_type`;
- một số trình duyệt/OS gửi `image/jpg` → bị từ chối;
- kéo-thả từ một số nguồn gửi content-type rỗng hoặc `application/octet-stream` → bị từ chối dù nội dung là ảnh hợp lệ.

**RC-3 (đường MinIO, sẽ vỡ khi lên production).**
`src/shared/Clawbot.Infrastructure/Documents/MinioDocumentStorage.cs:59` dùng `Uri.EscapeDataString(fileName)` — hàm này mã hóa cả dấu `/` thành `%2F`, làm hỏng URL có đường dẫn nhiều cấp. Nhánh còn lại (dòng 61-64) trả URL ký sẵn **hết hạn sau 7 ngày** (`ExpirySeconds` dòng 12), mà URL đó lại bị ghi cứng vào `AssetsJson` → sau một tuần toàn bộ ảnh cũ chết.

**RC-4 (phụ).** `src/shared/Clawbot.Domain/Content/ContentAsset.cs:53` sinh `StorageKey` không có phần mở rộng, buộc mọi tầng phía sau phải đoán kiểu nội dung.

**RC-5 (phụ).** `ContentDtos.cs:98` khai báo `ContentAssetUploadResponse(string Url, string AssetsJson, Guid AssetId = default)` và endpoint trả đủ 3 trường ở dòng 493, nhưng interface phía frontend chỉ khai 2 trường → không lấy được `assetId`, nên nút xóa ảnh (`DELETE /items/{id}/assets/{assetId}`, đã có ở dòng 73-74) không dùng được.

### 2a.2 Phương án sửa

**F1 — Endpoint đọc ảnh có kiểm quyền (giải quyết RC-1 và RC-3).**

Thêm `GET /api/content/items/{id:guid}/assets/{assetId:guid}` với `RequirePermission("content:read")`:
- nạp `ContentAsset` theo `assetId`, kiểm `TenantId` khớp tenant hiện tại và `ContentItemId` khớp `{id}`; sai hoặc `Status != ready` → 404 (không phân biệt, tránh dò tài nguyên);
- đọc nội dung qua `IDocumentStorage.ReadAsync(asset.StorageKey, ct)` — **cả hai bản cài đặt đều đã có sẵn hàm này** (`DocsServices.cs:169` và `MinioDocumentStorage.cs:67`), nên không cần mở rộng interface;
- trả `Results.File(bytes, asset.ContentType ?? "application/octet-stream")`;
- gắn `ETag` từ `asset.Sha256` và `Cache-Control: private, max-age=86400`.

Sau đó `BuildDisplayUrlMap` (`ContentEndpoints.cs:547-586`) ghi vào `AssetsJson` **URL ổn định** `"/api/content/items/{itemId}/assets/{assetId}"` thay cho chuỗi do storage trả về. Ưu điểm: hết phụ thuộc vào `PublicBaseUrl`, hết URL hết hạn, và ảnh được kiểm quyền theo tenant.

Frontend đã có `apiClient` với baseURL trỏ về API, nhưng thẻ `<img>` không đi qua axios. Vì vậy cần một hàm nhỏ ghép `import.meta.env.VITE_API_BASE_URL` vào đường dẫn tương đối trước khi đưa vào `src` (đặt cạnh `parseAssets` ở `ContentWorkspacePage.tsx:355`), và bảo đảm cookie/token đi kèm — nếu API dùng Bearer token thì thay bằng cách tải ảnh qua axios rồi tạo object URL, hoặc cho phép endpoint này nhận token qua cookie phiên.

*Phương án thay thế đã cân nhắc và loại:* bật `UseStaticFiles` cho thư mục `generated-docs`. Loại vì file của mọi tenant nằm chung một thư mục sau khi bị làm phẳng khóa, ai đoán được GUID là xem được ảnh của tenant khác — rò rỉ dữ liệu đa tenant.

**F2 — Hợp nhất allowlist định dạng (giải quyết RC-2).**

- Gộp `AllowedAssetContentTypes` và `LooksLikeAllowedImage` thành **một** nguồn sự thật.
- Chấp nhận thêm: `image/jpg` (bí danh của `image/jpeg`) và trường hợp content-type rỗng/`application/octet-stream` **nếu** magic bytes hợp lệ — phần kiểm magic bytes đã có sẵn, chỉ cần cho phép nó quyết định.
- Với HEIC/HEIF: **từ chối có chủ đích**, kèm mã lỗi riêng `content.asset_heic_unsupported` và câu hướng dẫn tiếng Việt: "Ảnh iPhone định dạng HEIC chưa đăng được lên Facebook/Instagram. Vào Cài đặt > Camera > Định dạng > Tương thích nhất, rồi chụp lại; hoặc chuyển ảnh sang JPG trước khi tải lên." Lý do không nhận rồi tự chuyển đổi: sẽ phải thêm thư viện xử lý ảnh (ImageSharp/Magick.NET) và một nhánh xử lý mới, trong khi nhận vào rồi để hỏng ở bước đăng bài còn tệ hơn từ chối sớm.
- Chuẩn hóa content-type về chữ thường và cắt tham số (`image/jpeg; charset=...`) trước khi so khớp.

**F3 — Sửa đường MinIO (giải quyết RC-3 cho phần tài liệu).**
`MinioDocumentStorage.cs:59`: mã hóa theo từng đoạn đường dẫn thay vì `EscapeDataString` trên cả chuỗi. Sau F1 thì ảnh nội dung không còn đi đường này, nhưng tài liệu sinh tự động vẫn dùng.

**F4 — Giữ đuôi file trong `StorageKey` (RC-4).**
`ContentAsset.Reserve` nhận thêm phần mở rộng đã chuẩn hóa và ghép vào `StorageKey`. Chỉ áp dụng cho asset mới; asset cũ vẫn đọc đúng vì `StorageKey` được lưu trong DB, không phải suy ra.

**F5 — Bổ sung `assetId` cho frontend (RC-5).**
Thêm `assetId` vào interface `ContentAssetUploadResponse` trong `src/frontend/clawbot-web/src/shared/api/content.ts`, và nối nút xóa ảnh vào endpoint `DELETE` đã có.

**F6 — Ghi log chẩn đoán.** Thêm log có cấu trúc khi từ chối upload (mã lỗi + content-type nhận được + kích thước), để lần sau khách báo lỗi là tra được ngay.

### 2a.3 File cần sửa

| File | Nội dung |
|------|----------|
| `src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs` | thêm route đọc ảnh; hợp nhất allowlist (~395-500, 2052-2090); đổi URL trong `BuildDisplayUrlMap` (547-586) |
| `src/api/Clawbot.Api.Contracts/Content/ContentDtos.cs` | không đổi hợp đồng, chỉ xác nhận `AssetId` đã có (dòng 98) |
| `src/shared/Clawbot.Domain/Content/ContentAsset.cs` | thêm đuôi file vào `StorageKey` (dòng 53) |
| `src/agents/Clawbot.Agents.Core/Docs/DocsServices.cs` | (tùy chọn) ngừng làm phẳng khóa, chặn path traversal bằng kiểm tiền tố thư mục gốc |
| `src/shared/Clawbot.Infrastructure/Documents/MinioDocumentStorage.cs` | sửa mã hóa URL (dòng 59) |
| `src/frontend/clawbot-web/src/shared/api/content.ts` | thêm `assetId`; hàm ghép base URL cho ảnh |
| `src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx` | dùng URL đã ghép ở dòng 371-373, 795, 1030; nối nút xóa ảnh; hiển thị mã lỗi mới |

### 2a.4 Kiểm thử

- Unit: bảng tham số content-type × magic bytes → chấp nhận/từ chối đúng (jpeg, jpg, png, gif, webp, heic, octet-stream + magic png, text/plain).
- Integration (`tests/Clawbot.Api.Tests`, theo mẫu `ContentPostPerformanceTests.cs`): upload → `GET` ảnh trả 200 đúng content-type; tenant khác gọi cùng URL → 404; asset chưa `ready` → 404.
- E2E Playwright: tải ảnh trong workspace nội dung → thẻ ảnh hiện đúng, không có request 404 trong Network.
- Thủ công: kiểm tra ảnh cũ (đã lưu URL `/generated-docs/...` trong `AssetsJson`) — cần một bước chuyển đổi dữ liệu hoặc chấp nhận ảnh cũ vẫn hỏng; xem mục Rủi ro.

---

## 2b. Yêu cầu 2 (phần 2) — Hiển thị list hot key / hot search

### 2b.1 Nguyên nhân gốc

Dữ liệu thô **bị vứt trước khi kịp lưu**:

- `src/agents/Clawbot.Agents.Core/Research/ResearchAgent.cs` — `ScanAsync` lọc `RelevanceScore > 0` rồi `Take(25)`.
- Cùng file, dòng 74-76: `WeightedTrendScorer` gán điểm 0 cho mọi xu hướng **không khớp từ khóa nào** (chú thích trong mã ghi rõ "score 0 → bị loại ở ScanAsync"). Đây chính là tập "chưa filter" mà khách muốn xem.
- `src/agents/Clawbot.AgentService/Services/TrendScanService.cs` chỉ tạo `ContentBrief` từ danh sách đã lọc.
- `ContentEndpoints.cs:1693-1699` (`TrendsAsync`) đọc lại từ `ContentBrief` → API không thể trả cái không được lưu.

Ngoài ra, sau khi job quét xong, `TrendModal` không tự mở và không tự nạp lại, nên trải nghiệm hiện tại là "quét xong không thấy gì".

### 2b.2 Phương án sửa

1. **Giữ lại dữ liệu thô.** Đổi `IResearchAgent.ScanAsync` (`ResearchAgent.cs:27-30`) trả về bản ghi mới:
   `ResearchScanResult(IReadOnlyList<ScoredTrend> Trends, IReadOnlyList<RawTrend> RawTrends)`.
   Giữ danh sách thô **trước** bước lọc, khử trùng lặp theo `Topic`, sắp theo `SourceScore` giảm dần, giới hạn 200 mục để không phình dữ liệu.
2. **Lưu song song.** `TrendScanService.ScanAndPersistAsync` tạo thêm một `ContentBrief` với marker `[trend-raw:{weekOf}]`, nội dung do `ContentTrendBriefFormatter` sinh (thêm cặp `FormatRaw`/`ParseRaw` bên cạnh `Format` hiện có, mỗi dòng `topic | source | metric`).
3. **Mở rộng API.** `GET /api/content/trends?week=&include=raw` (`ContentEndpoints.cs:59, 1660-1700`) trả `TrendScanResponse(trends, rawTrends)`. Mặc định không kèm `rawTrends` để không làm nặng lần gọi cũ.
4. **Giao diện.** `TrendModal` (`ContentWorkspacePage.tsx:636-729`) chuyển thành 2 tab:
   - "Đã lọc (N)" — giữ nguyên thẻ hiện tại (chủ đề, nguồn, chỉ số, điểm liên quan, 2 ý tưởng nội dung);
   - "Tất cả từ khóa (M)" — danh sách gọn: từ khóa + nguồn + chỉ số, kèm ô tìm kiếm tại chỗ, nút sao chép từ khóa, nút "Đưa vào yêu cầu nội dung" (tái dùng `applyTrendIdea` ở dòng 2032).
   - Badge đếm số lượng ở mỗi tab, để khách thấy ngay "quét được 180 từ khóa, 24 khớp chủ đề".
5. **Tự mở sau khi quét.** `useJobWatcher` (dòng 1913-1927) khi job hoàn tất: gọi lại `trendsQuery.refetch()`, mở `TrendModal`, hiện thông báo "Đã quét xong: N từ khóa liên quan / M từ khóa thô".

### 2b.3 File cần sửa

`ResearchAgent.cs`, `TrendSources.cs`, `TrendScanService.cs`, `ContentTrendBriefFormatter.cs`, `ContentEndpoints.cs`, `ContentDtos.cs`, `shared/api/content.ts` (163-174, 267-276), `ContentWorkspacePage.tsx` (575-729, 1619-1628, 1913-1927, 2100-2125).

### 2b.4 Kiểm thử

- Unit: `FormatRaw`/`ParseRaw` khứ hồi; khử trùng lặp và cắt 200 mục.
- Integration: chạy quét giả lập → `GET /api/content/trends?include=raw` trả cả hai danh sách; `include` mặc định không kèm raw.
- E2E: bấm "Quét" → job xong → modal tự mở → chuyển tab thấy danh sách thô → bấm "Đưa vào yêu cầu nội dung" thì ô soạn brief nhận nội dung.

---

## 3.1. Yêu cầu 3.1 — Tạm dừng luồng, sửa output của task, chạy tiếp

### 3.1.1 Phần đã có sẵn (tin tốt)

Đã kiểm chứng trong mã, ba mảnh khó nhất **đã chạy đúng**:

- **Tạm dừng có hiệu lực sau MỖI task**, không phải cuối đợt: `AutonomousOrchestrator.cs:155-157` gọi `PersistPlanAsync` rồi `IsStoppedAsync` sau từng task; nếu đã dừng thì thoát mà **không** gọi `FailAsync`, nên phiên giữ nguyên trạng thái `paused`.
- **Chạy tiếp không làm lại việc đã xong**: `ReadyTasks` (dòng 360-367) chỉ lấy task `pending` có toàn bộ phụ thuộc đã `completed`.
- **Output đã sửa sẽ tự chảy sang task kế tiếp**: `ToAgentTask` (dòng 375-407) gom `Output` của các task phụ thuộc vào khóa `upstream_results`, và `PromoteUpstreamIds` (dòng 414+) nâng các id (`content_id`, `schedule_id`, …) từ khối `[tool_results]` lên thẳng input của task sau.

Nghĩa là chỉ cần sửa được `Output` trong `PlanJson` là toàn bộ cơ chế phía sau hoạt động.

### 3.1.2 Ba điểm chặn

- **B1.** `src/shared/Clawbot.Domain/Agents/AgentSession.cs` — `UpdatePlan` ném lỗi nếu trạng thái không phải `draft` hoặc `pending_approval`. Phiên đang `paused` không sửa được kế hoạch.
- **B2.** Không có đường sửa output của một task cụ thể. Endpoint `PUT /api/orchestration/v2/runs/{id}/plan` (`OrchestrationV2Endpoints.cs:91`, quyền `orchestration:run`) sửa cả kế hoạch dạng JSON thô.
- **B3 — đây mới là gốc của vấn đề chi phí khách nêu.** Trong `AutonomousOrchestrator.cs`:
  - dòng 183: khi có task fail → `_planner.ReplanAsync(tenantId, goal, entries, failed, ct)` sinh **kế hoạch hoàn toàn mới**;
  - dòng 185: `PersistPlanAsync(plan)` **ghi đè** kế hoạch cũ, kèm theo đó là mọi `Status`/`Output` của các task đã hoàn thành;
  - `AutonomousPlanner.BuildReplanGoal` chỉ nhận danh sách task **thất bại**, không biết gì về việc đã làm xong.
  - Hệ quả đúng như khách mô tả: 1 task hỏng → lập kế hoạch mới → chạy lại từ đầu → trả tiền lần nữa cho những việc đã xong.

Ghi chú thêm (nợ kỹ thuật, không chặn): `RunningSessions` trong `OrchestratorGrpcService` được đọc bởi `CancelRunningSession` nhưng **không nơi nào ghi vào**, nên hủy phiên chỉ dựa vào vòng lặp `IsStoppedAsync`.

### 3.1.3 Phương án sửa — Pha A: dừng chờ người, sửa, chạy tiếp

1. **Domain.** Trong `AgentSession`, thêm phương thức riêng thay vì nới lỏng `UpdatePlan` (giữ nguyên ràng buộc cũ để không phá test hiện có):
   ```
   public void EditPausedPlan(string planJson)  // chỉ cho phép khi Status == Paused
   ```
   Vẫn cấm tuyệt đối khi `running`, tránh tranh chấp ghi với orchestrator.

2. **Endpoint mới.** `PUT /api/orchestration/v2/runs/{id:guid}/tasks/{taskId}/output`, quyền `orchestration:run`, body `{ humanText, toolResults?, etag }`:
   - từ chối nếu `session.Status != "paused"` → 409, mã `orchestration.session_not_paused`;
   - từ chối nếu task không tồn tại hoặc trạng thái không thuộc `completed|failed` → 400;
   - kiểm `etag` bằng cơ chế `EnsureEtagMatches` đã có;
   - ghép lại output: `humanText` + `"\n[tool_results]\n"` + JSON. **Nếu người dùng không sửa phần `toolResults` thì giữ nguyên khối cũ** — bắt buộc, vì `PromoteUpstreamIds` phụ thuộc vào khối này để chuyển `content_id`/`schedule_id` sang task sau;
   - gọi `plan.WithTaskStatus(taskId, "completed", output, error: null)`;
   - chạy `OrchestrationPlanValidator.Validate` (đã xác minh validator chỉ xét id, agent, kích thước input, phụ thuộc và chu trình — **không** đụng tới `Status`/`Output`, nên kế hoạch đã sửa vẫn hợp lệ);
   - redact bằng đúng bộ lọc `AutonomousRunSink` đang dùng, rồi `session.EditPausedPlan(json)`.

   *Vì sao không để frontend gửi cả kế hoạch qua `PUT /plan` sẵn có:* frontend sẽ phải tự lắp lại JSON của toàn bộ task, rủi ro ghi đè nhầm task khác và làm mất khối `[tool_results]`. Xử lý phía server an toàn hơn và giữ frontend đơn giản.

3. **Dừng thay vì lập lại kế hoạch.** Thêm tùy chọn `PauseOnTaskFailure` (mặc định bật) vào `AutonomousOrchestratorOptions`. Trong vòng lặp, khi phát hiện task `failed` và trước khi tiêu tốn ngân sách replan:
   - gọi `_sink.PauseAsync(...)` (thêm vào `IAutonomousRunSink` + `AutonomousRunSink`, gọi `session.Pause()`),
   - ghi trace `waiting_for_human` với thông điệp tiếng Việt "Một bước đã lỗi. Luồng đang chờ người kiểm tra và sửa kết quả.",
   - trả `AutonomousRunResult` trạng thái tạm dừng.

   Đây là phần trả lời trực tiếp cho "giảm chi phí vận hành Orchestrator": mặc định không còn tự đốt tiền lập lại kế hoạch.

4. **Giao diện.** Trong `OrchestrationPanel.tsx`, hộp chi tiết task (dòng ~468-474):
   - nút "Tạm dừng để sửa" — nếu phiên đang `running` thì gọi control `pause` rồi chờ tới khi trạng thái thành `paused` (poll hoặc tín hiệu Redis đã có) mới mở trình sửa;
   - vùng soạn thảo cho phần văn bản người đọc (lấy từ `splitToolResults(task.output).text` — xem mục 3.2);
   - bảng khóa/giá trị chỉ đọc cho `toolResults`, có tùy chọn "sửa nâng cao";
   - hai nút "Lưu" và "Lưu và chạy tiếp" (lưu xong gọi control `resume`);
   - băng thông báo khi phiên `paused`: "Luồng đang tạm dừng. Bước tiếp theo sẽ chạy khi bạn bấm Tiếp tục."

### 3.1.4 Phương án sửa — Pha B: giữ lại việc đã xong khi buộc phải lập lại kế hoạch

Áp dụng cho trường hợp vẫn cần replan tự động (không có người trực):

- `AutonomousPlanner.ReplanAsync` nhận thêm danh sách task đã `completed`; `BuildReplanGoal` liệt kê "các bước đã hoàn thành (không cần làm lại)" kèm tóm tắt output.
- Sau khi có kế hoạch mới, **hợp nhất** thay vì thay thế: giữ nguyên các task `completed` cũ cùng `Output`; task mới trùng `Id` với task đã `completed` thì giữ bản cũ; chỉ thêm task thật sự mới.
- Sau hợp nhất, chạy lại validator để chắc chắn không có phụ thuộc treo hay chu trình.

### 3.1.5 Rủi ro

| Rủi ro | Giảm thiểu |
|--------|-----------|
| Người sửa trong lúc orchestrator vẫn đang ghi | Chỉ cho sửa khi `paused`; kiểm `etag`; frontend chờ tới khi trạng thái thật sự là `paused` |
| Người sửa làm mất khối `[tool_results]` → task sau thiếu id | Server giữ nguyên khối cũ nếu không được gửi kèm; hiển thị cảnh báo khi người dùng chủ động xóa |
| `PauseOnTaskFailure` khiến các luồng chạy đêm treo chờ người | Cho cấu hình theo tenant; thêm hết hạn chờ (ví dụ 24 giờ) rồi chuyển sang hành vi cũ; thông báo qua kênh cảnh báo đã có |

### 3.1.6 Kiểm thử

- Unit domain: `EditPausedPlan` chấp nhận khi `paused`, ném lỗi khi `running`/`completed`.
- Unit orchestrator: task fail + `PauseOnTaskFailure` bật → phiên `paused`, `replans == 0`, chi phí không phát sinh thêm.
- Integration: chạy kế hoạch 3 task → dừng sau task 1 → sửa output → chạy tiếp → task 2 nhận đúng `upstream_results` đã sửa; task 1 **không** chạy lại.
- Integration pha B: ép task 2 fail → replan → task 1 vẫn `completed` và giữ nguyên `Output`.
- E2E: kịch bản đầy đủ trên giao diện.

---

## 3.2. Yêu cầu 3.2 — Ô kết quả cho người dùng đọc được

### 3.2.1 Hiện trạng

`src/frontend/clawbot-web/src/features/agents/TaskResultDetails.tsx` (71 dòng) là **điểm hiển thị duy nhất**, dùng chung cho bảng điều khiển (`OrchestrationPanel.tsx:474`) và trang chi tiết phiên. Nó đã gọi `splitToolResults(task.output)` — hàm này (`shared/utils/userText.ts:119-141`) tách sẵn phần văn bản người đọc khỏi khối `[tool_results]`. Nhưng phần văn bản đang bị hiển thị ngang hàng với JSON thô và `JSON.stringify(task.input)`, trong khối `<pre>` đơn sắc, nên người dùng không phân biệt được đâu là kết quả dành cho mình.

### 3.2.2 Phương án sửa

1. Thêm `toHumanTaskSummary(task)` vào `shared/utils/userText.ts`:
   - lấy `splitToolResults(output).text`;
   - nếu rỗng (agent chỉ trả JSON), sinh câu tóm tắt từ `toolResults`: `content_id` → "Đã tạo nội dung", `schedule_id` → "Đã lên lịch đăng", `post_url` → hiển thị dưới dạng liên kết, v.v.;
   - lược bỏ dấu rào mã (` ``` `), khối JSON lọt vào văn bản, và các marker nội bộ.
2. Bố cục mới trong `TaskResultDetails.tsx`:
   - **"Kết quả cho người đọc"** — mở sẵn, nền sáng, chữ thường (không phải `<pre>` đơn sắc), giữ xuống dòng, giới hạn khoảng 12 dòng kèm nút "Xem đầy đủ";
   - **"Dữ liệu bàn giao cho agent kế tiếp"** — thu gọn sẵn, chứa bảng `toolResults` và `input` JSON như hiện nay;
   - lỗi vẫn đi qua `toUserFriendlyOrchestrationError` như đang làm.
3. Đây cũng là chỗ đặt trình sửa của mục 3.1 → **làm 3.2 trước 3.1**.

### 3.2.3 Kiểm thử

- Unit `toHumanTaskSummary`: output chỉ có văn bản; chỉ có JSON; có cả hai; output rỗng.
- Kiểm tra mắt trên cả hai màn đang dùng component này.

---

## 5. Yêu cầu 5 — Xóa các bản tài liệu chưa phát hành trong kho tri thức

### 5.1 Hiện trạng

`src/api/Clawbot.Api/Endpoints/KbEndpoints.cs:31-60` **không có route DELETE nào cho phiên bản**. Hiện chỉ có tạo, sửa, lưu trữ module, tạo/tải lên/phát hành/khôi phục phiên bản.

`src/shared/Clawbot.Domain/KnowledgeBase/KbVersion.cs` (39 dòng): các trường là `KbModuleId`, `Version`, `ContentMd`, `Embedding`, `AccuracyScore`, `Status` (`draft|deployed|archived`), `DeployedAt`, `CreatedAt`. **Không có `DeletedAt`, không có `TenantId`, không có số lượng chunk.**

### 5.2 Phương án sửa

**Endpoint:**
- `DELETE /api/kb/modules/{moduleId:guid}/versions/{versionId:guid}` — xóa một bản.
- `DELETE /api/kb/modules/{moduleId:guid}/versions?scope=unpublished` — xóa hàng loạt, trả `{ deleted, skipped, skippedReasons }`.

Quyền: dùng đúng quyền đang gác `deploy` (đối xứng về mức rủi ro).

**Các lớp chặn (bắt buộc đủ):**
1. `KbModule.TenantId` phải khớp tenant hiện tại — đây là cách duy nhất kiểm tenant vì `KbVersion` không có cột này.
2. `version.KbModuleId == moduleId`.
3. `version.Status == "deployed"` → 409, mã `kb.version_deployed_not_deletable`, câu tiếng Việt "Không xóa được bản đang phát hành."
4. Có `ExperimentVariant.KbVersionId` trỏ tới (`src/shared/Clawbot.Domain/Experiments/ExperimentVariant.cs:13`) → 409, mã `kb.version_in_experiment`.
5. **Bảo vệ khả năng khôi phục:** mặc định giữ lại bản `archived` gần nhất (bản dự phòng của bản đang phát hành). Xóa nó phải truyền cờ rõ ràng `includeRollbackTarget=true`, và giao diện phải cảnh báo "Sau thao tác này bạn sẽ không khôi phục về bản trước được nữa."

**Dọn vector Qdrant:**
- Điểm vector được đánh khóa bằng `KbDeployService.ChunkPointId(version.Id, idx)` (`KbDeployService.cs:115-119`), còn số chunk sinh ra từ `ChunkContent(version.ContentMd)` (dòng 59-86) — **hàm thuần và tất định** với cùng `maxChunkChars = 1000`. Vì vậy tái tạo được danh sách id cần xóa mà không cần thêm cột: `ChunkContent(version.ContentMd).Count` → sinh id `0..count-1` → `IVectorStore.DeleteAsync(collection, ids)`.
- `collection` lấy như lúc nạp: `ConfiguredEmbeddingProvider.CollectionName(embeddingConfig)` của tenant.
- `ChunkContent` và `ChunkPointId` hiện là `internal` → bổ sung một hàm công khai `EnumerateChunkPointIds(KbVersion version)` trong `KbDeployService` thay vì nới `internal` bừa bãi.
- Bản `draft` chưa từng phát hành thì không có điểm vector — bỏ qua lỗi không tìm thấy, không để nó chặn việc xóa hàng trong DB.
- Theo ghi chú đã lưu, một số tenant đang chạy chế độ truy hồi bằng LLM (không dùng Qdrant) — khi không có `embedding_configs` đang bật thì bỏ qua bước này.
- Thứ tự an toàn: xóa vector **trước**, xóa hàng DB **sau**. Vector mồ côi thì vô hại (không còn ai tra tới); hàng DB mồ côi thì gây lỗi hiển thị.

**Giao diện:** `src/frontend/clawbot-web/src/features/kb/KnowledgeBaseWorkspace.tsx`, component `VersionRail` (dòng 140-202):
- thêm nút xóa trên từng thẻ phiên bản, ẩn khi `status === "deployed"`;
- thêm nút "Xóa các bản chưa phát hành" ở đầu cột, kèm hộp xác nhận nêu rõ số bản sẽ xóa và số bản được giữ lại;
- `src/frontend/clawbot-web/src/shared/api/kb.ts`: thêm `deleteKbVersion(moduleId, versionId)` và `deleteUnpublishedKbVersions(moduleId, options)`.

### 5.3 Rủi ro

Xóa cứng không hoàn tác được. Giảm thiểu: hộp xác nhận nêu số lượng cụ thể, mặc định giữ bản dự phòng, ghi nhật ký kiểm toán (ai xóa, bản nào, lúc nào).

### 5.4 Kiểm thử

- Integration: xóa bản `draft` → 204 và bản biến mất; xóa bản `deployed` → 409; module thuộc tenant khác → 404; bản đang gắn thí nghiệm → 409.
- Xóa hàng loạt: 5 bản (1 deployed, 1 archived mới nhất, 3 draft) → xóa 3, giữ 2, trả đúng `skippedReasons`.
- Kiểm chứng vector: đếm điểm trong collection trước/sau khi xóa một bản đã phát hành rồi lưu trữ.

---

## 6. Yêu cầu 6 — Chọn ngày giờ đăng bài luôn báo "Thông tin gửi lên chưa hợp lệ"

### 6.1 Nguyên nhân — tầng hiển thị (đã xác minh chắc chắn)

API **có** trả về mã lỗi và câu mô tả cụ thể. `ContentEndpoints.cs:2117-2120`:
```
private static IResult Error(HttpContext http, int statusCode, string errorCode, string message) =>
    Results.Json(new { code = errorCode, errorCode, message, requestId = http.TraceIdentifier }, statusCode: statusCode);
```

Nhưng `src/frontend/clawbot-web/src/shared/utils/userText.ts:40-56` — `toUserFriendlyError` — **không đọc `error.response.data`**. Nó chỉ nhìn mã HTTP, và với 400 luôn trả `STATUS_MESSAGES[400]` = **"Thông tin gửi lên chưa hợp lệ. Vui lòng kiểm tra lại."** — đúng nguyên văn câu trong ảnh khách gửi.

Chuỗi gọi: `ScheduleDialog` → `errorMessage()` (`ContentWorkspacePage.tsx:350-353`) → `toUserFriendlyError`. Đáng chú ý, ngay bên trên ở dòng 343-348 đã có tiền lệ đọc `errorCode` từ thân phản hồi, nhưng chỉ dành riêng cho một mã 409 của Instagram.

Kết luận: **bộ chọn ngày giờ nhiều khả năng không hỏng.** Một lỗi 400 khác đang bị che bởi câu thông báo chung.

### 6.2 Nguyên nhân — tầng thật (cần xác minh một lần)

Các mã 400 mà endpoint lên lịch có thể trả, xếp theo khả năng:

1. **`content.meta_page_required`** (`ContentEndpoints.cs:1228`) — nội dung Facebook nhưng `GetPublishablePagesAsync` trả về rỗng. Khi đó **mọi** ngày giờ đều lỗi, khớp hoàn toàn triệu chứng "chọn ngày giờ nào cũng hiện lỗi". Câu message của backend đã là tiếng Việt: "Hãy kết nối và chọn Facebook Page trước khi lên lịch." — và đang bị frontend nuốt mất.
2. **`content.item_not_schedulable`** (dòng 1301-1309) — do **lệch hợp đồng giữa hai lớp**:
   - `ContentItem.CanScheduleCurrentRevision()` (`ContentItem.cs:362-367`) chỉ kiểm `DeletedAt`, `ActivePublishAttemptId`, `Status == "approved"`, đã có review hoàn tất, và `ApprovedRevision == ContentRevision`;
   - `ContentAutoScheduler.CreateIntentAsync` (`ContentAutoScheduler.cs:87-92`) đòi **thêm** `ApprovalMode` không rỗng và `PublishingPolicyVersionApplied` không null, nếu thiếu thì ném `content_approval_context_missing`.
   - Hệ quả: nội dung duyệt qua đường cũ `ContentItem.Approve(Guid, DateTimeOffset)` (không gọi `RecordPublishingApproval`) sẽ có `canSchedule = true` → giao diện bật nút "Lên lịch" → backend luôn từ chối.
3. Nhóm Instagram: `content.instagram_credentials_invalid`, `_target_mode_conflict`, `_target_required`, `_reconnect_required`, `_permissions_missing`, `_not_linked`, `_target_unavailable`, `_meta_unavailable` (dòng 1231+).
4. `content.schedule_in_past` (`ResolveScheduledAt`, dòng 1962-1974) — chỉ xảy ra khi chọn hôm nay ở giờ đã qua, không khớp "chọn ngày nào cũng lỗi".

**Bước xác minh (làm trước khi sửa backend, 5 phút):** mở DevTools > Network, bấm lên lịch, đọc trường `errorCode` trong phản hồi 400. Hoặc chạy truy vấn kiểm tra `ApprovalMode`, `PublishingPolicyVersionApplied` của bản ghi nội dung đang lỗi.

### 6.3 Phương án sửa

**FX-1 (làm ngay, sửa 1 file, giá trị cao nhất).**
Mở rộng `toUserFriendlyError` trong `shared/utils/userText.ts`:
- đọc `error.response.data`, lấy `errorCode`;
- tra bảng `CONTENT_ERROR_MESSAGES` (mới) ánh xạ khoảng 12 mã `content.*` sang câu tiếng Việt kèm hành động cụ thể, ví dụ:
  - `content.meta_page_required` → "Chưa có Facebook Page nào sẵn sàng đăng. Vào Kết nối kênh để nối Page rồi thử lại."
  - `content.item_not_schedulable` → "Nội dung này chưa đủ điều kiện lên lịch: cần duyệt lại ở bản hiện tại."
  - `content.schedule_in_past` → "Thời điểm đăng phải ở tương lai. Chọn lại ngày giờ."
- **không** hiển thị thẳng `message` từ backend khi nó là tiếng Anh kỹ thuật (ví dụ mã `content.item_not_schedulable` có message tiếng Anh); ưu tiên bảng nội bộ, sau đó mới tới `message` nếu là tiếng Việt, cuối cùng mới tới câu theo mã HTTP.

**FX-2 — Đồng bộ cờ `canSchedule`.**
Thêm trường `scheduleBlockedReason` vào DTO nội dung (`ContentEndpoints.cs:1848` đang chiếu `CanSchedule: item.CanScheduleCurrentRevision()`), tính đủ cả bộ ba điều kiện phê duyệt. Giao diện dùng nó để **tắt** nút "Lên lịch" kèm chú giải, thay vì để người dùng bấm rồi nhận lỗi. Chọn cách thêm trường DTO thay vì sửa `CanScheduleCurrentRevision()` để không thay đổi ngữ nghĩa hàm domain đang có test bao phủ.

**FX-3 — Tách mã lỗi.**
Ở dòng 1301-1309, tách `content_approval_context_missing` ra mã riêng `content.approval_context_missing` (thay vì gộp chung với `content_current_revision_not_schedulable`), kèm câu hướng dẫn "Hãy duyệt lại nội dung này rồi lên lịch." Việc này giúp mọi lần báo lỗi sau đều chỉ đúng nguyên nhân.

**FX-4 — Kiểm tra đích đăng trước khi mở hộp thoại.**
Khi mở "Lên lịch xuất bản nội dung" cho nội dung Facebook/Instagram, frontend nạp trước danh sách Page/tài khoản; nếu rỗng thì hiện hướng dẫn kết nối ngay trong hộp thoại và vô hiệu hóa nút gửi.

**FX-5 — Chặn chọn quá khứ ở giao diện.**
Đặt thuộc tính `min` cho ô ngày và ô giờ trong `ScheduleDialog` (`ContentWorkspacePage.tsx:1294-1501`) để không gửi được thời điểm đã qua. Lưu ý `scheduledAtIso` (dòng 333-338) dựng `new Date(\`${date}T${time}:00\`)` theo giờ máy rồi chuyển sang ISO — đúng với múi giờ Việt Nam (+7), không cần đổi.

### 6.4 File cần sửa

`shared/utils/userText.ts` (FX-1), `ContentWorkspacePage.tsx` (FX-4, FX-5), `ContentEndpoints.cs` (FX-2, FX-3), `ContentDtos.cs` (FX-2).

### 6.5 Kiểm thử

- Unit: `toUserFriendlyError` với phản hồi giả cho từng mã `content.*` → ra đúng câu tiếng Việt; không có `errorCode` → vẫn trả câu theo mã HTTP như cũ.
- Integration: lên lịch nội dung Facebook khi tenant không có Page → 400 `content.meta_page_required`.
- Integration: nội dung duyệt kiểu cũ (thiếu `ApprovalMode`) → 400 `content.approval_context_missing`, và DTO trả `scheduleBlockedReason` khác null.
- E2E: mở hộp thoại lên lịch trong trạng thái thiếu Page → thấy hướng dẫn kết nối, nút gửi bị tắt.

---

## 7. Thứ tự triển khai đề xuất

| Đợt | Nội dung | Vì sao xếp ở đây |
|-----|----------|------------------|
| Đợt 1 (1-1.5 ngày) | 6-FX1, Yêu cầu 1, Yêu cầu 3.2 | Rủi ro thấp, thấy kết quả ngay. FX-1 còn **lộ ra nguyên nhân thật** của yêu cầu 6, phục vụ đợt 2. 3.2 tạo sẵn chỗ đặt trình sửa cho 3.1 |
| Đợt 2 (2-2.5 ngày) | Yêu cầu 2a, 6-FX2..FX5 | 2a là lỗi nặng nhất đang ảnh hưởng vận hành hằng ngày |
| Đợt 3 (2.5 ngày) | Yêu cầu 3.1 pha A, rồi pha B | Phức tạp nhất, phụ thuộc 3.2 |
| Đợt 4 (2.5 ngày) | Yêu cầu 2b, Yêu cầu 5 | Tính năng mới, không chặn vận hành |

---

## 8. Rủi ro tổng thể

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|--------|-----------|-----------|
| Ảnh đã tải lên trước đây vẫn giữ URL `/generated-docs/...` hỏng trong `AssetsJson` | Ảnh cũ vẫn không hiện sau khi sửa | Viết script một lần ghi lại `AssetsJson` sang URL mới theo `assetId` (dữ liệu `ContentAsset` đủ để dựng lại); hoặc để `BuildDisplayUrlMap` tự ghi đè ở lần sửa nội dung kế tiếp |
| Endpoint đọc ảnh mới yêu cầu xác thực, thẻ `<img>` không tự gắn token | Ảnh vẫn không hiện | Chốt sớm cơ chế xác thực của ảnh (cookie phiên hay tải qua axios + object URL) ngay ở đầu đợt 2 |
| Xóa cứng bản KB không hoàn tác | Mất tài liệu | Giữ bản dự phòng mặc định, hộp xác nhận nêu số lượng, ghi nhật ký kiểm toán |
| Đổi chữ ký `IResearchAgent.ScanAsync` | Vỡ nơi gọi khác | Tìm hết nơi gọi trước khi đổi; giữ nạp chồng cũ nếu cần |
| `PauseOnTaskFailure` làm luồng chạy đêm treo | Công việc định kỳ không hoàn thành | Cấu hình theo tenant + hết hạn chờ 24 giờ + cảnh báo |
| Migration | Không có | **Toàn bộ plan này không cần thêm file DDL nào.** Nếu phát sinh, tuân thủ quy ước: mỗi file một câu lệnh, không dùng `GO`, và nhớ cả nhánh sửa chữa của `run-all.bat` |

---

## 9. Kế hoạch kiểm thử chung

- **Backend:** bổ sung test vào `tests/Clawbot.Api.Tests` theo mẫu `ContentPostPerformanceTests.cs` hiện có. Nhớ rằng harness test bỏ qua kiểm quyền, nên các endpoint mới cần **thêm bản ghi `role_permissions` qua `RbacSeeder`** và kiểm chứng riêng bằng tay.
- **Frontend:** `pnpm tsc --noEmit` sau mỗi thay đổi interface; đặc biệt lưu ý các chỗ ép kiểu phản hồi axios — ép kiểu không bắt được lỗi khi hình dạng dữ liệu đổi.
- **E2E:** thêm kịch bản Playwright trong `src/frontend/clawbot-web/e2e/` cho: tải ảnh và hiển thị ảnh; quét xu hướng và xem danh sách từ khóa; tạm dừng, sửa output, chạy tiếp; xóa bản KB; lên lịch đăng bài.
- **Kiểm thử thủ công bắt buộc trước khi giao:** chạy đúng 6 kịch bản khách đã báo, trên đúng màn hình khách chụp.

---

## 10. Điểm cần khách xác nhận

1. Có giữ lại tab "Doanh thu" trong hồ sơ từng Lead không? (Plan này **giữ**, chỉ gỡ khỏi báo cáo tổng hợp.)
2. Ảnh HEIC từ iPhone: từ chối kèm hướng dẫn (đề xuất) hay cần hệ thống tự chuyển đổi sang JPG?
3. Khi một bước trong luồng agent lỗi, mặc định nên **dừng chờ người sửa** (đề xuất, tiết kiệm chi phí) hay vẫn tự lập lại kế hoạch?
4. Xóa bản KB: có cần giữ lại bản dự phòng gần nhất để còn khôi phục không? (Plan này **mặc định giữ**.)
