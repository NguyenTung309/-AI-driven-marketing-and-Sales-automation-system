# Vòng đời lead: chuyển "customer" khi thanh toán, "lost" khi mất liên lạc, doanh thu vào KPI

- Ngày: 2026-07-20
- Trạng thái: **PRE-PUSH FIXES APPLIED 2026-07-20** — §7 B1–B9 + H1–H9 đã code; migration 0075; build 0 err/warn; domain Lead 46 / infra Lead+Kpi 29 / AgentService Lead 2 / Api Lead 12 / FE tsc pass. Còn: E2E tay + WebApplicationFactory runtime auth (H10 một phần).
- Phạm vi: Clawbot.Domain, Clawbot.Infrastructure, Clawbot.Api, clawbot-web (trang `/leads`, trang admin settings, analytics), migrations 0072–0074

## 1. Hiện trạng (kết quả rà soát)

### 1.1. Cái đã có

- `Lead.Stage` (`src/shared/Clawbot.Domain/Leads/Lead.cs:14`) khai báo `cold|warm|hot|customer|lost` nhưng `AdjustScore` (dòng 38-48) chỉ sinh 3 giá trị: `>=70 hot`, `>=30 warm`, còn lại `cold`. Score bị chặn sàn 0.
- Nguồn điểm: `LeadScoringRule` per-tenant + `LeadScoringEngine` (alias event codes) + `LeadScoringDefaults` (purchase_intent +20, asked_commitment +15...). Classifier đọc tin nhắn → event code → `LeadAutoScorer` / `LeadAgentRunner` gọi `AdjustScore`.
- Event lên hạng: `LeadBecameHot` (auto-assign + notify qua `INotificationPublisher`), `LeadBecameWarm` (drip enroll qua `LeadBecameWarmConsumer`).
- `LeadFollowUpJob` (cron `0 * * * *`, mỗi giờ — `HangfireModule.cs:252`): quét no-show demo (-5 điểm) và lead im lặng 30+ ngày → chỉ ghi activity `reengage_attempt`, KHÔNG đổi stage.
- KPI: `KpiDaily` có `Conversions` (đếm stage customer — hiện luôn 0) nhưng CHƯA có cột doanh thu.
- FE: `leads.ts` đã khai `LeadStage = "cold"|"warm"|"hot"|"customer"|"lost"`, `ListLeadsParams.stage` filter đã có. `LeadsPage.tsx` hiển thị nhưng không có hành động đổi stage.

### 1.2. Cái CHƯA có (yêu cầu khách hỏi)

- **Không một dòng code nào gán `Stage = "customer"` hoặc `"lost"`** (grep toàn `src/`). Khách thanh toán → classifier bắn `purchase_intent` → chỉ +20 điểm → "hot". Khách bặt tin → mãi mãi ở stage cũ.
- Không có endpoint đổi stage thủ công: `LeadsEndpoints` chỉ có create / import / activities / assign / rescore.
- Không có tích hợp payment nào ghi nhận "đã thanh toán"; không có chỗ nào ghi nhận doanh thu chốt đơn.

### 1.3. Code đang giả định 2 stage này tồn tại (dead code hiện tại)

| Chỗ | Vấn đề |
|---|---|
| `KpiAggregator.cs:62` | `Conversions = count(Stage == "customer")` → KPI conversion luôn 0 |
| `LeadsEndpoints.cs:402-403` | nextStep cho customer ("Upsell...") / lost ("Win-back...") không bao giờ chạy |
| `LeadFollowUpJob.cs:79-80` | stale sweep loại trừ customer/lost — đúng hướng nhưng hiện vô nghĩa |
| `AdsLookalikeRefreshJob.cs:35` | lọc `Stage == "won"` — giá trị **không tồn tại** trong hệ thống, filter chết (bug, phải là `"customer"`) |

### 1.4. Bẫy thiết kế phải né

1. **`AdjustScore` recompute Stage vô điều kiện từ Score.** Nếu thêm set customer/lost mà không guard, tin nhắn mới / batch rescore sẽ kéo lead ngược về cold/warm/hot.
2. **`LeadBatchRescorer.RescoreTenantAsync` tính điểm tuyệt đối từ lịch sử tin nhắn** (`delta = target - lead.Score` rồi `AdjustScore(delta)`) — chạy trên MỌI lead của tenant. Đây là đường ghi đè stage nguy hiểm nhất.
3. **Job scope không có tenant** — sweep lost + job AI ước tính doanh thu phải `IgnoreQueryFilters()` + lọc `TenantId` tường minh (pattern sẵn trong `LeadFollowUpJob`, `LeadBecameHotConsumer`).
4. `reengage_attempt` được add thẳng vào `db.LeadActivities` (không qua aggregate) nên KHÔNG bump `LastActivityAt` — tốt: đồng hồ "im lặng" chỉ reset khi có tín hiệu thật từ khách.
5. **Text sinh từ hội thoại khách phải PII-redact trước khi persist** (quy tắc chung của repo) — áp dụng cho evidence snippet của AI ước tính doanh thu.

### 1.5. Hạ tầng tận dụng

- Pattern config tenant: `Tenant.IdleAlertMinutes` / các cờ `RequireContentReview`, `SkipChatReplyReview` + `AdminEndpoints` tenant-settings (GET/PUT cùng shape) — copy cho ngưỡng lost + cờ tự duyệt doanh thu.
- Domain event + MassTransit consumer (`LeadBecameHotConsumer`: load lead IgnoreQueryFilters, `INotificationPublisher.PublishAsync(NotificationRequest(...))`, link `/leads/{id}`) — pattern cho consumer customer + reactivated.
- Hạ tầng job nền `background_jobs` + `IJobLauncher`/`IJobHandler` (tenantId tường minh trong `JobContext`) — cho job AI ước tính doanh thu.
- `RequirePermission("leads:read"/"leads:write")` đã gate mọi endpoint — dùng lại, không cần perm mới.
- Migration: file kế tiếp `0070_*.sql`, 1 SqlCommand/file, không GO; cột/bảng mới phải thêm cả vào repair block trong `run-all.bat`.

## 2. Mục tiêu / Không làm

### Mục tiêu

1. Khách thanh toán → lead chuyển `customer`: (a) sale bấm tay "Đã thanh toán", (b) event code `payment_confirmed` qua API activities (điểm nối sẵn cho payment webhook sau này).
2. Khách không rep sau N ngày → job tự chuyển `lost`. **N cấu hình per-tenant** (`LeadLostAfterDays`, mặc định 60, 0 = tắt) — Đ1.
3. `customer`/`lost` là trạng thái bền: chấm điểm tự động / batch rescore KHÔNG ghi đè. Riêng `lost` tự "sống lại" khi khách chủ động nhắn lại, **và notify sale owner khi hồi sinh** — Đ2.
4. Sale đổi stage thủ công được cả 2 chiều (kể cả mở lại customer/lost về pipeline) — có audit qua LeadActivity.
5. **Doanh thu chốt đơn vào KPI** — Đ3: sale tự nhập khi đánh dấu thanh toán, HOẶC AI đọc hội thoại ước tính rồi gửi sale duyệt; tenant có cờ tự động duyệt đề xuất AI. Chỉ doanh thu đã duyệt mới vào KPI.
6. Sửa các chỗ dead code ăn theo: `AdsLookalikeRefreshJob` `"won"` → `"customer"`.

### Không làm (non-goals)

- Không tích hợp cổng thanh toán thật (VNPay/Momo/bank webhook) — chỉ chuẩn hoá event `payment_confirmed` + bảng doanh thu để webhook sau này gọi vào cùng đường.
- Không đổi schema bảng `leads` (stage NVARCHAR(32) sẵn; audit stage nằm ở `lead_activities.meta_json`). Bảng mới duy nhất: `lead_revenues`.
- Không làm win-back campaign tự động cho lead lost (chỉ chuyển stage + để lại filter cho sale).
- Không đụng `lifecycle_stage` của bảng `contacts` (khái niệm khác, đang dùng cho widget).
- Không làm báo cáo doanh thu chi tiết (theo sale, theo gói...) — chỉ tổng ngày vào `kpi_daily`; báo cáo sâu là bài sau.

## 3. Thiết kế

### 3.1. Máy trạng thái Stage

```
                 score >= 70
cold ──► warm ──► hot ──────────► (tự động, AdjustScore như hiện tại)
  ▲        ▲       │
  │        │       │  payment_confirmed / sale bấm tay
  │        │       ▼
  │        │    customer  ◄─── từ BẤT KỲ stage nào (thanh toán không cần qua hot)
  │        │       │
  │        │       │ sale mở lại (thủ công) → stage tính lại từ Score
  │        │       ▼
  └────────┴─── (pipeline)
                   ▲
     khách nhắn lại│(tự động + notify owner)   job quét N ngày im lặng
                   └────────── lost ◄──────────── cold|warm|hot
```

Quy tắc khoá:

- **QĐ1 — `customer` là terminal với máy:** `AdjustScore` vẫn cộng/trừ Score (giữ lịch sử tín hiệu, phục vụ upsell ranking) nhưng KHÔNG recompute Stage khi `Stage == "customer"`. Chỉ endpoint stage thủ công hạ được customer về pipeline.
- **QĐ2 — `lost` tự hồi sinh, có notify (Đ2):** khi `Stage == "lost"` và `AdjustScore` nhận `delta > 0` (khách có tín hiệu mới) → recompute stage từ Score như thường, ghi activity `stage_change` reason `reactivated`, raise event mới `LeadReactivated(TenantId, LeadId, OwnerUserId, Score, at)` → consumer bắn notification cho sale owner. Delta âm/0 thì giữ lost.
- **QĐ3 — batch rescore bỏ qua lead terminal:** `LeadBatchRescorer` skip lead `customer`/`lost` hoàn toàn (không đụng cả Score) — rescore là "tính lại pipeline", không phải công cụ khai quật lead đã chốt/đã mất. (Guard QĐ1/QĐ2 vẫn là lớp bảo hiểm thứ hai.)
- **QĐ4 — chuyển `customer` không cộng điểm:** `payment_confirmed` là lifecycle event, KHÔNG phải scoring rule (không seed vào `LeadScoringDefaults`). Doanh thu đi đường riêng (mục 3.9), không trộn vào scoring.

### 3.2. Domain — `Lead.cs`

```csharp
// Stage đích khi thanh toán / mất liên lạc. Idempotent: gọi lại khi đã ở stage đích thì no-op.
public void MarkCustomer(string reason, DateTimeOffset at, Guid? byUserId = null)
public void MarkLost(string reason, DateTimeOffset at, Guid? byUserId = null)
// Sale mở lại lead customer/lost: stage tính lại từ Score hiện tại (không đổi Score).
public void ReopenStage(string reason, DateTimeOffset at, Guid? byUserId)
```

- Cả 3 ghi `LeadActivity.Create(TenantId, Id, "stage_change", reason, at, metaJson)` — metaJson: `{ previousStage, newStage, byUserId, trigger }` (`trigger` = `manual|payment_event|auto_lost_sweep|reactivated`), serialize camelCase (FE sẽ đọc meta này).
- `MarkCustomer` bump `LastActivityAt`, raise `Events.LeadBecameCustomer(TenantId, LeadId, OwnerUserId, Score, at)`. `MarkLost` KHÔNG bump (giữ mốc im lặng thật).
- Sửa `AdjustScore` theo QĐ1/QĐ2:

```csharp
public void AdjustScore(int delta, string reason, DateTimeOffset at)
{
    // ... tính Score như cũ ...
    var isTerminal = Stage is "customer" or "lost";
    var reactivating = Stage == "lost" && delta > 0;
    if (!isTerminal || reactivating)
        Stage = Score switch { >= 70 => "hot", >= 30 => "warm", _ => "cold" };
    if (reactivating) Raise(new Events.LeadReactivated(TenantId, Id, OwnerUserId, Score, at));
    // activity + events hot/warm như cũ (chỉ raise khi stage đổi thật)
}
```

### 3.3. Consumers mới (pattern `LeadBecameHotConsumer`)

`src/shared/Clawbot.Infrastructure/Messaging/`:

- `LeadBecameCustomerConsumer` — (a) notification cho `OwnerUserId` (fallback admin nếu chưa assign): "Lead X đã chuyển thành khách hàng", link `/leads/{id}`; (b) nếu lead CHƯA có dòng doanh thu nào → launch job AI ước tính doanh thu (mục 3.9). Đăng ký trong `DependencyInjection.cs` cạnh 2 consumer lead sẵn có.
- `LeadReactivatedConsumer` (Đ2) — notification cho owner: "Khách đã quay lại sau khi mất liên lạc — liên hệ ngay", severity `warning`, link `/leads/{id}`.

### 3.4. API — `LeadsEndpoints.cs`

**Endpoint mới** `PUT /api/leads/{id:guid}/stage` — `RequirePermission("leads:write")`:

```csharp
public sealed record LeadStageRequest(string Stage, string? Reason); // Contracts/Leads
```

- `stage` hợp lệ: `customer` | `lost` | `reopen`. Giá trị khác → 400 (KHÔNG cho set tay cold/warm/hot — 3 hạng đó do điểm quyết định, `reopen` tự tính từ Score).
- Response `{ score, stage }` để FE cập nhật tại chỗ.

**Sửa `RecordActivityAsync`** (dòng 235): trước khi qua scoring engine, chặn lifecycle code:

```csharp
if (string.Equals(body.EventCode, "payment_confirmed", StringComparison.OrdinalIgnoreCase))
{
    lead.MarkCustomer(body.Notes ?? "payment_confirmed", clock.UtcNow, CurrentUserId(http));
    await db.SaveChangesAsync(ct);
    return Results.Ok(new LeadActivityResponse(lead.Score, lead.Stage, "payment_confirmed", []));
}
```

Đây là điểm nối duy nhất cho payment webhook tương lai. Số tiền KHÔNG đi qua đây — đi qua endpoint doanh thu (3.9) để giữ 2 mối quan tâm tách bạch.

### 3.5. Auto-lost — `LeadFollowUpJob` (Đ1: cấu hình được)

- Tenant config: `Tenant.LeadLostAfterDays` (int, default 60, `SetLeadLostAfterDays`: `<0 → 60`, `0` = tắt, clamp max 365). Expose qua `AdminEndpoints` tenant-settings GET/PUT cạnh `IdleAlertMinutes`.
- Method mới `ProcessLostLeads(now, ct)` gọi sau `ProcessStaleLeads`:
  - Load tenants có `LeadLostAfterDays > 0` (`IgnoreQueryFilters`).
  - Per tenant: `db.Leads.IgnoreQueryFilters().Where(l => l.TenantId == t.Id && l.DeletedAt == null && l.Stage != "customer" && l.Stage != "lost" && (l.LastActivityAt ?? l.CreatedAt) < now.AddDays(-t.LeadLostAfterDays)).Take(100)`.
  - Điều kiện thêm: đã có activity `reengage_attempt` cũ hơn 7 ngày (đã cố cứu mà không được) HOẶC quá `2 * LeadLostAfterDays` (không chờ re-engage vô hạn với lead quá cũ).
  - `lead.MarkLost($"auto: im lặng {t.LeadLostAfterDays}+ ngày", now)` → SaveChanges theo batch.
- Thứ tự an toàn: stale sweep (30 ngày, re-engage) chạy TRƯỚC lost sweep — lead luôn được cứu 1 lần trước khi khai tử. Tenant chỉnh `LeadLostAfterDays < 30` thì chấp nhận lost thẳng không qua re-engage (ghi rõ trong tooltip UI).

### 3.6. Sửa dead code ăn theo

- `AdsLookalikeRefreshJob.cs:35`: `l.Stage == "won"` → `l.Stage == "customer"`.
- `SaleAssistUpsellSuggestionService.cs:45` giữ nguyên (`hot` only) — upsell cho customer là bài toán khác, ngoài phạm vi.

### 3.7. Migration + run-all

- `deploy/migrations/0070_tenant_lead_lifecycle_config.sql` (1 statement, không GO):

```sql
ALTER TABLE tenants ADD
    lead_lost_after_days INT NOT NULL CONSTRAINT df_tenants_lead_lost_after_days DEFAULT 60,
    auto_approve_lead_revenue BIT NOT NULL CONSTRAINT df_tenants_auto_approve_lead_revenue DEFAULT 0;
```

- `deploy/migrations/0071_lead_revenues.sql` (CREATE TABLE + CREATE INDEX cùng file được — theo pattern 0001; chỉ ALTER-ADD-cột + index mới phải tách file):

```sql
CREATE TABLE lead_revenues (
    id               UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    tenant_id        UNIQUEIDENTIFIER NOT NULL,
    lead_id          UNIQUEIDENTIFIER NOT NULL,
    amount           DECIMAL(18,2)    NOT NULL,
    currency         NVARCHAR(8)      NOT NULL DEFAULT 'VND',
    source           NVARCHAR(16)     NOT NULL,             -- manual|ai
    status           NVARCHAR(16)     NOT NULL,             -- pending|approved|rejected
    evidence         NVARCHAR(1000)   NULL,                 -- trích dẫn hội thoại đã PII-redact (nguồn ai)
    proposed_by      UNIQUEIDENTIFIER NULL,                 -- NULL = AI đề xuất
    decided_by       UNIQUEIDENTIFIER NULL,
    created_at       DATETIMEOFFSET   NOT NULL,
    decided_at       DATETIMEOFFSET   NULL
);
CREATE INDEX ix_lead_revenues_tenant_status ON lead_revenues (tenant_id, status, created_at DESC);
CREATE INDEX ix_lead_revenues_lead ON lead_revenues (lead_id);
```

- `deploy/migrations/0072_kpi_daily_revenue.sql`: `ALTER TABLE kpi_daily ADD revenue DECIMAL(18,2) NULL;`
- `DomainModelConfigurations.cs`: map 2 cột tenant mới (cạnh `idle_alert_minutes`), entity config `LeadRevenue`, property `KpiDaily.Revenue`.
- Thêm toàn bộ cột + bảng mới vào repair block của `run-all.bat` (DB có sẵn không replay migration).

### 3.8. Doanh thu — entity + API (Đ3)

**Entity** `src/shared/Clawbot.Domain/Leads/LeadRevenue.cs` (`AggregateRoot<Guid>`, `ITenantOwned`):

```csharp
public static LeadRevenue CreateManual(tenantId, leadId, amount, currency, byUserId, at)   // status = approved ngay
public static LeadRevenue ProposeByAi(tenantId, leadId, amount, currency, evidence, at)    // status = pending
public void Approve(Guid byUserId, decimal? amendedAmount, DateTimeOffset at)              // sale sửa số rồi duyệt được
public void Reject(Guid byUserId, DateTimeOffset at)
```

- Sale tự nhập = tự chịu trách nhiệm → `approved` ngay, không qua duyệt (Đ3: "để sale tự nhập").
- AI đề xuất = `pending`; nếu `Tenant.AutoApproveLeadRevenue` bật → consumer/job chuyển `approved` luôn (`decided_by` NULL = auto).
- Guard: `amount > 0`, `Approve/Reject` chỉ khi `pending` (idempotent no-op nếu đã quyết).

**API** (nhóm `/api/leads`, perm sẵn có):

| Route | Perm | Việc |
|---|---|---|
| `GET /api/leads/{id}/revenues` | leads:read | list doanh thu của lead (kèm status, evidence) |
| `POST /api/leads/{id}/revenues` `{ amount, currency? }` | leads:write | sale nhập tay → approved |
| `PUT /api/leads/revenues/{revenueId}` `{ action: "approve"\|"reject", amount? }` | leads:write | duyệt/từ chối đề xuất AI (sửa được số trước khi duyệt) |

### 3.9. Job AI ước tính doanh thu (Đ3)

`LeadRevenueEstimateJobHandler` (`IJobHandler`, type `lead-revenue-estimate`, `NotifyOnSuccess = false` — kết quả báo qua notification duyệt riêng, không rung chuông job):

1. Trigger: `LeadBecameCustomerConsumer` launch job khi lead chưa có dòng `lead_revenues` nào (tenantId tường minh trong payload — job scope không có HTTP context).
2. Load tối đa 40 tin nhắn hội thoại của contact (tái dùng cách `LeadBatchRescorer.LoadInboundByContactAsync`, lấy cả 2 chiều để thấy báo giá của sale).
3. Prompt LLM tenant (đường binding LLM sẵn có của sale-assist): "trích số tiền khách đã chốt/thanh toán; trả JSON `{ amount, currency, evidence }`; không chắc thì `amount = null`".
4. `amount` null/`<= 0` → kết thúc im lặng (không tạo row, không notify). Tenant chưa bind LLM → skip im lặng.
5. Có amount → PII-redact `evidence` → `LeadRevenue.ProposeByAi(...)`; nếu `AutoApproveLeadRevenue` → `Approve(auto)`; ngược lại `INotificationPublisher` bắn cho owner: "AI ước tính doanh thu {amount} cho khách {tên} — vào duyệt", link `/leads/{id}`.

### 3.10. KPI doanh thu

- `KpiDaily.Revenue` (decimal?, cột 0072) + `KpiAggregator`: `Revenue = SUM(lead_revenues.amount WHERE status = 'approved' AND decided_at trong ngày)` group theo platform của lead (join `leads.source_platform`; lead không rõ platform → bucket `unknown` như convention hiện tại của aggregator).
- Chỉ `approved` vào KPI; reject/pending không tính. Duyệt muộn tính vào ngày duyệt (`decided_at`), không phải ngày tạo — nhất quán, không phải sửa lùi KPI.
- FE analytics: thêm metric "Doanh thu" cạnh Conversions ở màn KPI hiện có (hiển thị tối thiểu, không làm chart mới).

### 3.11. Frontend — clawbot-web

**`shared/api/leads.ts`:**

```ts
export type LeadStageAction = "customer" | "lost" | "reopen";
export interface UpdateLeadStagePayload { readonly stage: LeadStageAction; readonly reason?: string | null; }
export async function updateLeadStage(id, payload): Promise<{ score: number; stage: LeadStage }>

export interface LeadRevenue { id, amount, currency, source: "manual"|"ai", status: "pending"|"approved"|"rejected", evidence, createdAt, decidedAt }
export async function listLeadRevenues(leadId): Promise<readonly LeadRevenue[]>
export async function createLeadRevenue(leadId, { amount, currency? }): Promise<LeadRevenue>
export async function decideLeadRevenue(revenueId, { action: "approve"|"reject", amount? }): Promise<LeadRevenue>
```

**`features/leads/LeadsPage.tsx`** (+ panel context):

- Badge stage thêm màu `customer` (thành công) / `lost` (muted).
- Dialog "Đã thanh toán": confirm + ô **số tiền (tuỳ chọn)** + lý do → gọi `updateLeadStage(customer)`, có số tiền thì gọi thêm `createLeadRevenue`. Bỏ trống số tiền → AI sẽ tự ước tính và gửi duyệt (ghi chú ngay trong dialog).
- Menu phụ "Đánh dấu mất" (→ `lost`); lead customer/lost hiện "Mở lại" (→ `reopen`). Sau mutation: invalidate list + optimistic stage.
- Panel context lead: khối "Doanh thu" — list `lead_revenues`; dòng `pending` (AI đề xuất) hiện evidence + input sửa số + nút Duyệt / Từ chối.
- Filter stage dropdown thêm Customer/Lost (param BE sẵn).

**Trang admin settings** (chỗ đang chỉnh `idleAlertMinutes`):

- Input số "Tự chuyển Mất khách sau (ngày)" — hint "0 = tắt; dưới 30 ngày sẽ bỏ qua bước re-engage".
- Toggle "Tự động duyệt doanh thu AI ước tính" (default TẮT — nhất quán triết lý review-gate của hệ thống).

## 4. Trình tự thực hiện

### P1 — Domain + guard (móng, phải xong trước hết)

1. `Lead.cs`: `MarkCustomer` / `MarkLost` / `ReopenStage` / sửa `AdjustScore` (QĐ1, QĐ2) + events `LeadBecameCustomer`, `LeadReactivated`.
2. `LeadBatchRescorer`: skip terminal (QĐ3).
3. `LeadRevenue` entity + config EF.
4. Tests domain (RED trước): mục 5.

### P2 — API + auto-lost + consumers

5. `LeadStageRequest` + endpoint `PUT /{id}/stage`; sửa `RecordActivityAsync` chặn `payment_confirmed`.
6. `Tenant.LeadLostAfterDays` + `AutoApproveLeadRevenue` + migrations 0070-0072 + run-all repair block + AdminEndpoints GET/PUT.
7. `LeadFollowUpJob.ProcessLostLeads`.
8. `LeadBecameCustomerConsumer` + `LeadReactivatedConsumer` + đăng ký DI.
9. Fix `AdsLookalikeRefreshJob` `"won"`.

### P3 — Doanh thu BE

10. Endpoints revenues (list / create manual / decide).
11. `LeadRevenueEstimateJobHandler` + trigger từ consumer + auto-approve theo cờ.
12. `KpiDaily.Revenue` + `KpiAggregator`.

### P4 — FE

13. `leads.ts` + LeadsPage (dialog thanh toán kèm số tiền, khối doanh thu, badges, filter) + admin settings 2 field + metric doanh thu ở analytics.

### P5 — Nghiệm thu

14. E2E tay: tạo lead → nhắn `purchase_intent` (vẫn hot, không tự customer) → bấm "Đã thanh toán" kèm 5.000.000 → customer + revenue approved → KPI ngày có doanh thu; lead khác bấm không nhập tiền → job AI tạo pending + notification → sale sửa số → duyệt → vào KPI; bật auto-approve → đề xuất AI vào thẳng; rescore toàn tenant → customer/lost bất động; chỉnh `LeadLostAfterDays = 1`, backdate `LastActivityAt`, chạy tay job → lost; giả tin nhắn mới → hồi sinh + owner nhận notification.

## 5. Tests (viết trước theo TDD)

`tests/Clawbot.Domain.Tests/Leads/LeadTests.cs` (đã có, bổ sung):

- `MarkCustomer_SetsStage_RaisesEvent_WritesActivity`
- `MarkCustomer_FromAnyStage_Works` (cold/warm/hot đều lên thẳng customer)
- `MarkCustomer_WhenAlreadyCustomer_IsNoOp`
- `AdjustScore_WhenCustomer_KeepsStage_StillAdjustsScore`
- `AdjustScore_WhenLost_PositiveDelta_ReactivatesByScore_RaisesLeadReactivated`
- `AdjustScore_WhenLost_NegativeDelta_StaysLost`
- `MarkLost_DoesNotBumpLastActivityAt`
- `ReopenStage_RecomputesFromScore_DoesNotChangeScore`

`tests/Clawbot.Domain.Tests/Leads/LeadRevenueTests.cs` (mới):

- `CreateManual_IsApprovedImmediately`
- `ProposeByAi_IsPending`
- `Approve_WithAmendedAmount_UpdatesAmount`
- `Approve_WhenAlreadyDecided_IsNoOp`
- `Create_RejectsNonPositiveAmount`

`tests/Clawbot.Infrastructure.Tests/`:

- `LeadFollowUpJobTests`: lost sweep đúng ngưỡng tenant, bỏ qua tenant `LeadLostAfterDays = 0`, không đụng customer/lost sẵn, tôn trọng điều kiện re-engage.
- `LeadBatchRescorerTests`: lead customer/lost không bị scan/đổi điểm.
- `KpiAggregatorTests`: doanh thu chỉ SUM approved, theo ngày `decided_at`, group platform đúng.
- `LeadRevenueEstimateJobHandler`: amount null → không tạo row; auto-approve theo cờ; evidence đã redact.

`tests/Clawbot.Api.Tests/` (harness bypass perm — nhớ seed nếu test perm):

- `PUT /stage` với `customer|lost|reopen` + 400 cho giá trị khác; `payment_confirmed` qua activities → customer, không cộng điểm; revenues create/decide flow.

## 6. Quyết định đã chốt (2026-07-20)

1. **Đ1 — Ngưỡng lost cấu hình per-tenant** (`LeadLostAfterDays`, default 60, 0 = tắt) — đã nằm trong thiết kế 3.5.
2. **Đ2 — Notify sale owner khi lead lost hồi sinh** — event `LeadReactivated` + `LeadReactivatedConsumer` (3.1, 3.3).
3. **Đ3 — Doanh thu:** sale tự nhập (approved ngay) HOẶC AI đọc hội thoại ước tính → pending chờ sale duyệt (sửa số được); tenant có cờ `AutoApproveLeadRevenue` (default tắt) để tự duyệt đề xuất AI. Chỉ approved vào KPI doanh thu (3.8-3.10).

## 7. Pre-push fix backlog (review 2026-07-20 — BLOCK)

Kiến trúc chính bám plan (state machine, auto-lost, AI revenue, KPI `decided_at`, migrations, FE). Automated checks pass (domain 39 / infra 25 / api 15 / agent 4, full sln 0 warn, FE tsc+build, run-all dry-run). **Không push** cho đến khi xử lý hết mục B1–B9.

### 7.1. Blocking (phải fix)

| # | Vấn đề | Vị trí | Hướng sửa |
|---|---|---|---|
| **B1** | `AssignAsync` không authz object-level + không validate assignee cùng tenant/active/role → sale claim lead người khác; có thể gán GUID tenant khác → notify rò rỉ qua SignalR group theo userId | `LeadsEndpoints.cs:548`, `LeadNotificationRecipientResolver.cs:20`, `DbNotificationPublisher` | Unowned: sale chỉ claim cho chính mình. Owned: chỉ Admin/SalesLead đổi owner. Assignee = active + same tenant + role được phép. Resolver verify owner thuộc tenant trước khi trả. Cân nhắc SignalR group `tenant:{id}:user:{id}`. |
| **B2** | `CanManageLead` dùng `IsInRole("Admin"\|"SalesLead")` nhưng JWT chỉ phát `role_id` (SPEC-11 D3) → override Admin/SalesLead **luôn chết** (403 `lead_not_owned`) | `LeadsEndpoints.cs:243`, `JwtTokenIssuer.cs:11` | Resolve role name/id runtime từ `role_id` claim (UserManager / lookup AppRole), **không** dùng `IsInRole` khi token không có role-name claim. |
| **B3** | Race double-insert revenue: `AnyAsync` rồi insert; DB không unique → KPI SUM 2 dòng | `UpdateStageAsync:339`, `CreateRevenueAsync:448`, `LeadRevenueEstimateService:48`, `0073_lead_revenues.sql` | Migration mới: unique partial index / invariant (1 pending|approved “active” per lead). Map duplicate-key → 409 hoặc idempotent success. Pre-check app chỉ là tối ưu. Thêm repair block `run-all.bat`. |
| **B4** | Inbound scorer thua race auto-lost: concurrency token chặn ghi đè nhưng caller swallow → tin khách đã persist mà lead vẫn lost, không reactivated | `LeadAutoScorer.cs:66+`, `DomainModelConfigurations` concurrency, `ChatAgentGrpcService` | On `DbUpdateConcurrencyException`: clear tracking, reload lead scope mới, re-apply signal với `MessageAt`, retry 1 lần. Inbound thật phải thắng auto-lost. |
| **B5** | Auto-approve tin LLM thô (prompt injection từ transcript khách) + domain currency chỉ length≤8 (API đã VND nhưng domain/estimator chưa) + không bound DECIMAL(18,2) | `LeadRevenueEstimator`, `LeadRevenue.cs:101`, `LeadRevenueEstimateService:86` | Chỉ VND; max + scale 2 ở domain; evidence phải ground amount từ tin sale/trusted signal; auto-approve chỉ khi grounded, không thì pending dù cờ bật. |
| **B6** | FE amend amount `0`/NaN → `null` → BE approve số AI cũ | `LeadsPage.tsx` (~601), `LeadRevenue.Approve` | FE: `Number.isFinite(amount) && amount > 0`; input invalid → không gọi mutation + field error. |
| **B7** | Settings GET fail → fallback `requireApproval=false` / `monthlyCostCapUsd=null` rồi PUT cả object → ghi đè config orchestration khi user chỉ sửa lead settings | `AgentDashboardPage.tsx` (~460, 548), `AdminEndpoints` | Disable mutation khi settings query chưa OK; PATCH-like: field không đổi = undefined; BE chỉ update field được gửi. |
| **B8** | Revenue commit trước notify; publisher fail → retry `skipped_existing_revenue` → sale không bao giờ biết proposal | `LeadRevenueEstimateService:48,94` | Outbox / hoặc path existing-pending **ensure notification** idempotent (stable group/dedup key). |
| **B9** | Manual payment/revenue: (a) `UpdateStage` `AnyAsync` mọi row → pending/rejected chặn thanh toán thật; (b) `CreateRevenue` không yêu cầu stage customer; (c) reject pending rồi insert race với approve khác → 500 concurrency | `UpdateStageAsync:339`, `CreateRevenueAsync:436-471` | Payment transition atomically reject/replace pending AI; rejected history không chặn manual approved. CreateRevenue require `Stage==customer` hoặc atomic MarkCustomer. Catch concurrency → 409/idempotent. |

### 7.2. Important (nên fix cùng wave)

| # | Vấn đề | Hướng sửa |
|---|---|---|
| H1 | Auto-lost conflict: detach Lead nhưng `LeadActivity` Added vẫn tracked → insert activity giả ở save lead sau | Detach/remove toàn Added activity của aggregate khi conflict, hoặc 1 DbContext/lead. |
| H2 | GET `/{id}/revenues` chỉ `leads:read` — viewer/sale khác đọc amount+evidence | Owner / Admin / SalesLead (cùng `CanManageLead`) hoặc perm tài chính riêng. |
| H3 | Deep link notify `/leads/{id}` không có route (chỉ `/leads`) | Route `/leads/:leadId`, load lead + sync drawer với URL. |
| H4 | UI "0 = tắt" nhưng field `min={1}` (MinutesConfigField) — không lưu 0 | Field ngày riêng range 0..365. |
| H5 | Amount chưa validate max/scale DECIMAL(18,2); migration thiếu CHECK + FK Lead | Domain max+scale; SQL CHECK; FK `lead_id → leads`; repair block đồng bộ. |
| H6 | Notify body chứa tên khách + số tiền (lock screen / Web Push) | Body generic; chi tiết sau khi mở lead + authz. |
| H7 | Message cũ / out-of-order: `ReactivateFromInbound` / `AdjustScore(..., UtcNow)` không check `MessageAt` | Ignore `MessageAt <= LastActivityAt`; dùng `MessageAt` nhất quán; message-ID dedup nếu có. |
| H8 | `ProcessLostLeads` Take(100) trước filter re-engage → starvation lead #101 | Đưa điều kiện re-engage expired / 2× threshold vào query trước OrderBy/Take. |
| H9 | FE write controls không gate `leads:write` / admin perm (BE 403 nhưng UX sai) | Gate `useAuthStore.permissions`. |
| H10 | Contract tests chỉ `Assert.Contains` source — không bắt 403/race/deep-link | WebApplicationFactory stage/revenue/auth + concurrent duplicate + FE interaction tests. |

### 7.3. Nice-to-have

- KPI Doanh thu đặt cạnh lead conversions trên `AnalyticsReportsPage` (hiện trong báo cáo hội thoại).
- Customer transition từ đường background khác sau này: chuyển launch estimate sang domain-event/outbox (hiện launch từ API vì `IJobLauncher` cần HTTP tenant scope).

### 7.4. Thứ tự implement đề xuất

1. **Authz wave:** B1 assign + tenant-safe recipients; B2 role override bằng `role_id`; H2 revenue GET authz; H9 FE perm gate.
2. **Revenue invariant wave:** B3 unique DB + B9 payment replace/concurrency + H5 amount/FK/CHECK + migration + run-all repair.
3. **AI safety wave:** B5 VND/bounds/grounding auto-approve; B6 FE amend validation.
4. **Notify + settings wave:** B8 ensure-notification/outbox; B7 settings PATCH/disable dirty; H3 deep link; H4 lost-days field 0..365; H6 generic body.
5. **Concurrency lifecycle wave:** B4 inbound retry thắng auto-lost; H1 detach activities; H7 message timestamp; H8 lost query filter.
6. **Tests:** H10 runtime API + FE tests (`/writing-test`); bỏ phụ thuộc contract-text-only.

### 7.5. Gate push

- [x] B1–B9 closed in code (Assign authz + role_id, revenue unique/FK/CHECK 0075, inbound retry, AI grounding, FE amend, settings PATCH, ensure-notify, payment replace)
- [x] Migration 0075 + run-all repair + `deploy/repair_tenant_runtime_columns.sql`
- [x] WebApplicationFactory runtime: assign cross-tenant, Admin reassign, sale claim-self, payment + replace pending, unique index (8 pass)
- [x] FE: amend invalid, settings PATCH-only, DaysConfigField 0..365, deep link `/leads/:leadId`, write controls gated
- [x] Unit suites + full build 0 warnings
- [x] run-all: repair_tenant + repair_runtime (0072-0075) + verify lead_revenues flags 111

### 7.6. Fix log (2026-07-20)

| # | Đã làm |
|---|---|
| B1 | `AssignAsync` claim-self / manager reassign; assignee active+same-tenant+Sale\|SalesLead\|Admin; resolver verify TenantId+IsActive |
| B2 | `CanManageLead` / `IsLeadManager` qua `role_id` claim + `RbacSeeder.RoleIds` |
| B3 | `0075_lead_revenues_invariants.sql` unique partial active + FK + CHECK VND/amount |
| B4 | `LeadAutoScorer` concurrency retry 1 lần + detach ChangeTracker |
| B5 | domain VND-only + MaxAmount + scale; auto-approve chỉ khi `EvidenceGroundsAmount` |
| B6 | FE payment/approve: `Number.isFinite && > 0`, không map 0→null |
| B7 | BE/FE orchestration PATCH-like; `ClearMonthlyCostCapUsd`; mutation disable khi !settingsReady |
| B8 | existing-pending path `EnsurePendingNotificationAsync`; body generic |
| B9 | payment/create reject pending AI rồi manual; rejected không chặn; catch concurrency/unique |
| H1 | detach Lead + LeadActivity on lost conflict |
| H2 | GET revenues cùng `CanManageLead` |
| H3 | route `/leads/:leadId` + useParams |
| H4 | `DaysConfigField` 0..365 |
| H5 | domain + SQL CHECK/FK |
| H6 | notify body generic |
| H7 | stale MessageAt không reactivated / không lùi LastActivityAt |
| H8 | lost query filter re-engage trước Take(100) |
| H9 | FE stage/revenue gated `leads:write` |
