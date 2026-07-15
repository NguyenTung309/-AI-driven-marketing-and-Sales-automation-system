# Kế hoạch: Phân trang / Infinite scroll cho toàn bộ danh sách

- **Ngày**: 2026-07-15
- **Nhánh**: `thang/chat_v5`
- **Quyết định đã chốt**:
  1. **Hybrid**: infinite scroll ở mọi danh sách + **luôn hiển thị tổng số record**.
  2. **Keyset (cursor) cho feed**, **offset + total cho bảng ổn định**.
  3. **Phạm vi**: làm toàn bộ một lượt — chuẩn hoá hạ tầng rồi áp cho tất cả list (kể cả admin, competitors, ads, prompts).
  4. **Sort Conversations**: mặc định feed đổi sang `last_message_at DESC, id DESC` (keyset chuẩn); "ưu tiên theo điểm lead" thành chế độ sort **tuỳ chọn** dùng offset.

---

## 1. Vấn đề (đã xác nhận trong code)

**Anti-pattern lặp ở mọi trang danh sách**: FE hardcode `page: 1` với `pageSize` lớn rồi lọc/tìm kiếm phía client → **âm thầm cắt cụt dữ liệu**.

Bằng chứng:
- [ConversationsPage.tsx:648-674](../../../src/frontend/clawbot-web/src/features/conversations/ConversationsPage.tsx#L648-L674) — chỉ lấy 50 hội thoại đầu (`page:1, pageSize:50`), rồi lọc `search` + "của tôi" **trong bộ nhớ**. Hội thoại khớp ở trang 2+ không bao giờ xuất hiện.
- [LeadsPage.tsx:543](../../../src/frontend/clawbot-web/src/features/leads/LeadsPage.tsx#L543) — `listLeads({ page:1, pageSize:200 })`.
- [NotificationsPage.tsx:168](../../../src/frontend/clawbot-web/src/features/notifications/NotificationsPage.tsx#L168) — `page:1, pageSize:30`.
- [TaskLogsPage.tsx:215-231](../../../src/frontend/clawbot-web/src/features/logs/TaskLogsPage.tsx#L215-L231) — runs `page:1, pageSize:25`, audit `page:1, pageSize:12`.
- [ContentWorkspacePage.tsx:1041](../../../src/frontend/clawbot-web/src/features/content/ContentWorkspacePage.tsx#L1041) — queue `page:1, pageSize:80`.
- [AdminConsolePage.tsx:103-137](../../../src/frontend/clawbot-web/src/features/admin/AdminConsolePage.tsx#L103) — users + audit `page:1, pageSize:50`.

**Không có sẵn**: infinite-scroll primitive, hạ tầng cursor/keyset, thanh filter dùng chung. Chỉ có [DataTable.tsx](../../../src/frontend/clawbot-web/src/shared/ui/DataTable.tsx) (render thuần, không phân trang).

## 2. Hiện trạng backend theo 3 nhóm

| Nhóm | Endpoint | Trạng thái | Filter hiện có |
|---|---|---|---|
| **A. Đã offset + `total`, FE bỏ qua** | Inbox conversations, Content queue/items, Logs task-runs, Logs audit, Notifications, Admin users, Agent traces | Có `Skip/Take` + `CountAsync` + envelope `{items,total,page,pageSize}` | status/platform/inboxId, action/resourceType, q, unread |
| **B. Có offset nhưng KHÔNG trả `total`** | Leads | Trả mảng thuần → FE không biết còn nữa không | stage |
| **C. KHÔNG có offset, cap cứng `.Take(N)`** | Jobs (`Take(PageSize)`), Content briefs, Documents generated (`Take(100)`), Documents templates, KB modules/versions/test-cases, Orchestration runs (`Take(20)`), Competitors posts, Ads campaigns/actions/rules, Prompts configs | Cắt cứng, không lấy thêm được | rải rác |
| **D. List nhỏ, bị chặn** | Skills, Roles, Permissions, ApiKeys, ChatScenarios, Inbox channels, Agents list, Contact memories | Full/nhỏ | — |

## 3. Kiến trúc mục tiêu

### 3.1 Backend — 2 envelope chuẩn

Tạo trong `Clawbot.Api.Contracts` (dùng chung DTO):

```csharp
// Offset — cho bảng ổn định (leads, users, briefs, kb...)
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

// Keyset/cursor — cho feed theo thời gian (conversations, notifications, jobs, logs...)
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, int? Total);
```

**Cursor codec** (`src/api/Clawbot.Api/Common/Pagination/CursorCodec.cs`):
- Cursor = Base64Url của JSON `{ ts: DateTimeOffset, id: Guid }` (khoá sắp xếp + tie-breaker).
- Query keyset: `WHERE (created_at, id) < (@ts, @id) ORDER BY created_at DESC, id DESC LIMIT pageSize + 1`.
- Dùng row thứ `pageSize+1` để tính `NextCursor` và phát hiện hết trang (`NextCursor = null`).
- `Total`: tính 1 lần ở **trang đầu** (cursor null) để thoả "hybrid hiển thị tổng"; trang sau trả `Total = null` (đỡ đếm lại).
- Chống giả mạo: decode fail → coi như trang đầu (không throw ra client).

**Helper offset** (`PageRequest`): bind + clamp `page>=1`, `pageSize` trong `[1, MAX]`; extension `ToPagedResultAsync(query, page, pageSize, selector, ct)` gói `CountAsync` + `Skip/Take`.

**Chỉ mục DB** (migration, theo convention repo — mỗi file 1 `SqlCommand`, không `GO`, index cột ALTER thêm phải ở file riêng):
- Composite index `(tenant_id, created_at DESC, id DESC)` cho các bảng keyset: `conversations`, `notifications`, `background_jobs`, `agent_sessions`, `audit_logs`, `orchestration_runs`, `generated_documents`, `competitor_posts`, `ad_actions`.
- Cập nhật **khối repair hardcode trong run-all.bat** nếu thêm cột (đợt này chỉ thêm index → thêm vào block index).

### 3.2 Frontend — primitive dùng chung

`src/frontend/clawbot-web/src/shared/ui/`:
1. **`useInfiniteList`** — bọc `useInfiniteQuery` (React Query v5, cần `initialPageParam`). `getNextPageParam` xử lý cả 2 kiểu:
   - cursor: dùng `nextCursor` (null = hết).
   - offset: `page+1` khi `Σ items < total`.
   Trả về `{ items, total, hasNextPage, fetchNextPage, isFetchingNextPage, ... }`.
2. **`InfiniteScrollSentinel`** — `<div>` + IntersectionObserver, tự gọi `fetchNextPage` khi hiện + `hasNextPage` + không đang fetch. Kèm nút **"Tải thêm"** dự phòng (a11y + khi observer miss).
3. **`ListToolbar`** — thanh filter/search dùng chung: ô search **debounce** (`useDebounce`), các select/chip filter, hiển thị **"Hiển thị X / Y"** (Y = total), nút reset. Đổi filter/search → reset infinite query.
4. **`InfiniteDataTable`** — bọc `DataTable` sẵn có + sentinel + sticky header + footer đếm tổng. Dùng cho bề mặt dạng bảng.
5. **`useDebounce`** — hook chung (theo patterns.md).

FE luôn là infinite scroll (nhất quán); backend keyset hay offset đều được `useInfiniteList` che đi. Total hiển thị ở mọi nơi. Đây chính là mô hình "hybrid" đã chốt.

### 3.3 Sửa cốt lõi: chuyển filter về server

Bỏ lọc client. Ví dụ conversations: truyền `q`/`status`/`assignedTo=mine` xuống BE thay vì lọc 50 record trong RAM. Gộp endpoint `/inbox/search` (đang có `InboxSearchService` nhưng **FE chưa gọi** — `searchConversations` 0 caller) vào `ListAsync` để 1 đường đi thống nhất filter + cursor.

## 4. Điểm cần quyết định phụ

- **Sắp xếp Conversations** — **ĐÃ CHỐT** (quyết định #4): hiện `OrderByDescending(leadScore)` không keyset được (khoá tính toán, không đơn điệu) → mặc định feed đổi sang `last_message_at DESC, id DESC` (keyset chuẩn); giữ "ưu tiên theo điểm" thành chế độ sort tuỳ chọn dùng offset. Áp dụng ở Phase 1.
- **KB versions/test-cases, Contacts memories, Roles/Permissions/Skills/ApiKeys**: khối lượng nhỏ, để offset + soft cap; ưu tiên thấp.

## 5. Phân rã công việc (scope = tất cả, chia phase để review)

### Phase 0 — Hạ tầng dùng chung (không đổi hành vi)
- BE: `PagedResult<T>`, `CursorPage<T>`, `CursorCodec`, `PageRequest` + extension `ToPagedResultAsync`/`ToCursorPageAsync`. Unit test codec (roundtrip, tamper) + clamp.
- FE: `useInfiniteList`, `InfiniteScrollSentinel`, `ListToolbar`, `InfiniteDataTable`, `useDebounce`. Cập nhật `shared/ui/index.ts`.

### Phase 1 — Feed lớn (keyset)
Conversations · Notifications · Logs (runs + audit) · Jobs · Content queue.
- BE: đổi sang `CursorPage`, filter server-side đầy đủ; conversations gộp search + đổi sort mặc định.
- FE: thay `useQuery` → `useInfiniteList`, gắn `ListToolbar` + sentinel; **xoá lọc client** ở ConversationsPage.
- Realtime (SSE `useInboxRealtime`, notifications): item mới **prepend** vào trang đầu trong cache, không reset scroll.

### Phase 2 — Bảng (offset + total)
Leads (thêm `total` vào BE) · Admin users (đã có total, chỉ wire FE) · Content briefs (thêm offset) · Documents generated + templates · KB modules.

### Phase 3 — Phần còn lại
Orchestration runs · Competitors posts · Ads (campaigns/actions/rules) · Prompts configs · Agent traces · Contact memories · Skills/Roles/Permissions/ApiKeys/ChatScenarios (soft cap + offset nếu cần).

### Phase 4 — Dọn dẹp & test
- Xoá hết `page:1` hardcode + `pageSize` phồng + `.Take(N)` cap cứng.
- Grep đảm bảo không còn lọc client trên tập bị cắt.
- Test: BE integration mỗi endpoint (biên trang, tổ hợp filter, rỗng, trang cuối `nextCursor=null`, chèn row giữa 2 lần fetch → không trùng/sót). FE: test `useInfiniteList` (cả offset & cursor), sentinel chỉ gọi `fetchNextPage` 1 lần.

## 6. Rủi ro & lưu ý (từ memory dự án)

- **Đa tenant**: mọi query keyset phải giữ filter tenant (đã có qua query filter HTTP). Không lặp lại lỗi [[hangfire-job-scope-has-no-tenant]] — nhưng đây là HTTP scope nên OK.
- **Migration**: theo [[clawbot-migration-no-go]] — 1 `SqlCommand`/file, không `GO`; và [[run-all-skips-migration-replay]] — schema cũ dùng khối repair hardcode, index mới phải thêm vào đó.
- **Conversations score-sort** vs keyset (mục 4).
- **PII**: text phái sinh vẫn phải redact ([[pii-redact-derived-content]]) — không đổi, chỉ lưu ý khi đụng logs/traces.
- **RBAC**: các endpoint đã `RequirePermission`, không đổi ([[rbac-perm-seed-required]]).

## 7. Tiêu chí hoàn thành

- [ ] Không endpoint list nào cắt cứng `.Take(N)` mà không có cách lấy tiếp.
- [ ] Mọi trang list: cuộn tới cuối tải thêm mượt + hiển thị tổng số + filter/search chạy server-side.
- [ ] Không còn lọc/tìm kiếm client trên tập đã bị phân trang.
- [ ] Test BE/FE cho phân trang + keyset (không trùng/sót) đạt.
- [ ] Build FE + `dotnet build` xanh; gate build repo ([[clawbot-build-gates]]) không vỡ.
