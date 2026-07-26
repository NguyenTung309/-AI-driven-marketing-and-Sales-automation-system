# Plan: Fallback ước lượng token/chi phí khi provider không trả usage

- Ngày: 2026-07-25
- Trạng thái: ĐÃ TRIỂN KHAI P0-P4 (code) 2026-07-25 — build Release 0 warning / 0 error, `tsc --noEmit` + eslint sạch. Còn lại: §7.1 nhập rate thật vào `/llm-providers` và §8.2 verify SQL sau deploy (vận hành).
- Nguồn: điều tra "chạy cả tuần vẫn 0.0 USD" trên tenant dùng gateway `https://aigatewayport.com/v1`
- Quyết định của chủ sản phẩm: chọn **phương án 3** — tự đếm token bằng tokenizer local làm fallback khi response không có usage, kèm nhãn "ước lượng" rõ ràng.

---

## 1. Bối cảnh đã xác minh

Kết quả probe trực tiếp lên gateway (2026-07-25, có sự đồng ý của chủ sản phẩm):

| Đường gọi | Kết quả |
|---|---|
| `POST /responses` stream (`stream:true`) | HTTP 200, `text/event-stream`, 102.377 byte, 420 event. Có `response.completed` nhưng object `response` chỉ chứa `background, created_at, error, id, object, status` — **không có `usage`**. Grep `usage\|input_tokens\|output_tokens\|total_tokens\|prompt_tokens` trên toàn bộ 102KB: **0 match**. |
| `POST /responses` non-stream | Trả shape Chat Completions với `"usage":{}` (object rỗng) và `"cost":"0"`. |
| Endpoint usage/billing | `/v1/usage`, `/v1/dashboard/billing/usage`, `/v1/billing/usage`, `/v1/credits`, `/v1/me`, `/v1/key/info` đều 404. Chỉ `/v1/models` trả 200. |

Chuỗi hệ quả (3 tầng cùng im lặng):

1. `inTok = outTok = 0` → `Cost(0,0) = 0`.
2. `DbLlmCostTracker.RecordAsync` có `if (entry.UsdCost <= 0m) return;` → **không ghi dòng nào**, kể cả token.
3. `OrchestratorCostGuard.RecordAsync` trả `Task.CompletedTask` khi `UsdCost <= 0` → reservation không bao giờ được apply, sau đó bị release về 0 → còn lại dòng rác `__cost_reservation__`.
4. `TokensEndpoints` lọc bỏ dòng reservation → UI hiển thị $0.00.

Bằng chứng DB: 209 dòng ledger tổng; 7 ngày gần nhất = 85 dòng, **toàn bộ** là `__cost_reservation__` với usd 0 / token 0. Dòng usage thật cuối cùng: **2026-07-07**, model `cx/gpt-5.5-review` (config `openai` ở `localhost:20128`, hiện đã tắt — config đó *có* trả usage).

Hai hệ quả phụ phải nói rõ với khách:

- **Cấu hình 3/3 trong ảnh chưa từng được lưu.** Config đang active có cả hai cột rate = NULL → code dùng default hằng số `3.00 / 15.00` USD/1M.
- **Hạn mức chi phí $200/tháng đã vô hiệu từ 2026-07-07** vì `MonthToDateUsd` không bao giờ tăng. `TryReserveAsync` luôn pass.

### Cảnh báo về độ chính xác (bắt buộc truyền đạt cho khách)

Trong stream probe: **403 event `response.reasoning_summary_text.delta`** so với chỉ **4 event `response.output_text.delta`**. Model này đốt reasoning token nhiều hơn output nhìn thấy được rất nhiều bậc. Mọi con số đếm từ text nhìn thấy sẽ **thấp hơn hóa đơn thật**, không phải sai lệch nhỏ. Đây là giới hạn cố hữu của phương án 3, không phải bug triển khai.

---

## 2. Phạm vi

**Trong phạm vi**

- P0: mở van ghi ledger + log cảnh báo khi call thành công mà usage = 0.
- P1: tokenizer local + bộ ước lượng token.
- P2: gắn cờ `IsEstimated` xuyên suốt reply → cost entry → cột DB.
- P3: API + UI phân biệt "đo được" vs "ước lượng".
- P4: an toàn hạn mức + hướng dẫn cấu hình rate thật.

**Ngoài phạm vi**

- Không đổi provider, không đổi gateway.
- Không backfill 7 ngày đã mất (dữ liệu gốc không còn, mọi backfill là số bịa).
- Không sửa `system_logs` (đang là plan riêng: `2026-07-17-system-error-logs-admin.md`).
- Không đổi cơ chế reservation.

---

## 3. P0 — Mở van ghi ledger (làm trước, độc lập, giá trị ngay)

Đây là phần nên vào production đầu tiên: kể cả khi chưa có tokenizer, nó khiến provider *có* trả usage nhưng cost round về 0 vẫn ghi được token, và làm lộ ngay ca usage = 0 trong log.

### 3.1 `src/shared/Clawbot.Infrastructure/Agents/DbLlmCostTracker.cs:27`

```csharp
// Trước
if (entry.UsdCost <= 0m)
    return;

// Sau — chỉ bỏ qua khi KHÔNG có gì để ghi (cả token lẫn cost đều 0).
// Provider trả usage nhưng cost làm tròn về 0 (call rất nhỏ) vẫn phải vào sổ để báo cáo token đúng.
if (entry.UsdCost <= 0m && entry.InputTokens <= 0 && entry.OutputTokens <= 0)
    return;
```

Lưu ý thứ tự: hiện `CreateScope()` chạy *trước* guard — nên chuyển guard lên trước `CreateScope()` để không mở scope vô ích.

### 3.2 `src/agents/Clawbot.Agents.Core/Orchestrator/OrchestratorCostGuard.cs`

```csharp
// Trước
reply.UsdCost <= 0m ? Task.CompletedTask : _tracker.RecordAsync(...)

// Sau
reply.UsdCost <= 0m && reply.InputTokens <= 0 && reply.OutputTokens <= 0
    ? Task.CompletedTask
    : _tracker.RecordAsync(...)
```

Đây cũng là chỗ sửa lỗi rác reservation: khi có token, reservation sẽ được `ApplyReservation` thay vì bị release về 0.

### 3.3 `src/agents/Clawbot.Agents.Core/Skills/Ops/ILlmCostTracker.cs` — `InMemoryLlmCostTracker`

Sửa cùng điều kiện để test in-memory và production hành xử giống nhau.

### 3.4 Log cảnh báo usage = 0

Các chat client không có `ILogger` (khởi tạo bằng `new` trong `LlmChatClientFactory`, không qua DI). Không thêm logger vào client — thay vào đó log ở phễu duy nhất là `DbLlmCostTracker`:

- Inject `ILogger<DbLlmCostTracker>` (đã là singleton, an toàn).
- Khi `entry.InputTokens == 0 && entry.OutputTokens == 0` và agent code không phải reservation → `LogWarning` với structured props `{TenantId}`, `{AgentCode}`, `{Model}`, message dạng `llm_usage_missing`.
- Khi `entry.IsEstimated` (sau P2) → `LogWarning` `llm_usage_estimated`.
- **Chống spam:** dedupe trong process bằng `ConcurrentDictionary<string, DateTimeOffset>` key `tenantId|model`, log lại sau 60 phút. Nếu không dedupe, mỗi tin auto-reply sẽ sinh 1 warning.

---

## 4. P1 — Tokenizer local

### 4.1 Chọn thư viện

Dùng **`Microsoft.ML.Tokenizers`** + package dữ liệu vocab nhúng sẵn:

- `Microsoft.ML.Tokenizers`
- `Microsoft.ML.Tokenizers.Data.O200kBase` (gpt-4o, gpt-4.1, gpt-5.x, o-series)
- `Microsoft.ML.Tokenizers.Data.Cl100kBase` (gpt-4, gpt-3.5 legacy)

Lý do chọn thay vì `SharpToken`:

- Do Microsoft phát hành → sạch với `NuGetAudit` (repo bật audit ở mức error).
- Package `.Data.*` **nhúng vocab vào assembly**, không tải file `.tiktoken` từ `openaipublic.blob.core.windows.net` lúc chạy. Bắt buộc: môi trường khách có thể chặn outbound, và tải vocab lúc chạy sẽ làm chậm/treo call đầu tiên.
- Không dùng `TiktokenTokenizer.CreateForModel(...)` với tên model lạ — `gpt-5.5` không có trong bảng model của thư viện và sẽ **throw**. Phải dùng `CreateForEncoding("o200k_base")` và tự map.

Thêm version vào `Directory.Packages.props` (repo bật `ManagePackageVersionsCentrally`), `PackageReference` không version vào `src/agents/Clawbot.Agents.Core/Clawbot.Agents.Core.csproj`. Pin version stable hiện hành — kiểm tra bằng `dotnet package search Microsoft.ML.Tokenizers` trước khi ghi, đừng đoán.

### 4.2 File mới: `src/agents/Clawbot.Agents.Core/Chat/LlmTokenEstimator.cs`

Static, thread-safe, không qua DI — giữ blast radius bằng 0 (không phải sửa `LlmChatClientFactory`, không phải sửa 25 caller của `IClaudeChatClient`).

```csharp
namespace Clawbot.Agents.Core.Chat;

// Đếm token cục bộ để ước lượng chi phí KHI provider không trả usage
// (quan sát aigatewayport 2026-07: SSE có response.completed nhưng không có field usage).
// Con số này là ƯỚC LƯỢNG: không thấy được reasoning token nên luôn thấp hơn hóa đơn thật.
internal static class LlmTokenEstimator
{
    // Overhead định dạng hội thoại: mỗi message tốn thêm ~4 token khung, cộng ~3 token priming.
    private const int TokensPerMessage = 4;
    private const int PrimingTokens = 3;

    private static readonly ConcurrentDictionary<string, Tokenizer> Cache = new(StringComparer.Ordinal);

    public static int CountText(string model, string? text);          // text -> token
    public static int CountPrompt(string model, string systemPrompt, IReadOnlyList<ChatTurn> history, string userMessage);
    private static Tokenizer Resolve(string model);                   // map model -> encoding, cache
    private static string EncodingFor(string model);                  // xem 4.3
}
```

`Resolve` phải bọc try/catch: nếu tokenizer khởi tạo lỗi thì fallback về heuristic `text.Length / 4` (không được để ước lượng làm chết call LLM thật).

### 4.3 Map model → encoding

```
gpt-5*, gpt-4o*, gpt-4.1*, o1*, o3*, o4*   -> o200k_base
gpt-4*, gpt-3.5*                            -> cl100k_base
còn lại (kể cả claude*, model gateway lạ)    -> o200k_base (proxy)
```

Ghi rõ trong comment: `claude*` dùng o200k làm proxy vì Anthropic không public tokenizer — nhưng đường Anthropic *có* trả usage nên nhánh này gần như không bao giờ chạy.

Bỏ tiền tố gateway trước khi map (`cx/gpt-5.5-review` → so khớp phần sau dấu `/` và cả chuỗi gốc).

### 4.4 Đếm reasoning summary — tăng độ chính xác đáng kể

Trong `OpenAiResponsesChatClient.ReadSseReplyAsync`, ngoài `response.output_text.delta` hãy cộng dồn thêm text của `response.reasoning_summary_text.delta` vào một `StringBuilder` **riêng**.

Vì sao quan trọng: probe cho 403 reasoning delta / 4 output delta. Nếu chỉ đếm output nhìn thấy, ước lượng sẽ lệch hàng chục lần. Reasoning *summary* vẫn là bản tóm tắt của reasoning thật, nên vẫn undercount — nhưng gần hơn rất nhiều.

Output token ước lượng = `CountText(model, visibleText) + CountText(model, reasoningSummaryText)`.

Không đưa reasoning summary vào `ClaudeReply.Text` (không được đổi nội dung trả về cho người dùng).

---

## 5. P2 — Truyền cờ `IsEstimated` xuống DB

### 5.1 Record (thêm param optional ở CUỐI để 25 call site hiện tại vẫn compile)

`src/agents/Clawbot.Agents.Core/Chat/IClaudeChatClient.cs`

```csharp
public sealed record ClaudeReply(string Text, int InputTokens, int OutputTokens, decimal UsdCost, string Model = "", bool IsEstimated = false);
public sealed record ClaudeStreamChunk(string Text, bool Final, int InputTokens, int OutputTokens, decimal UsdCost, string Model = "", bool IsEstimated = false);
```

`src/agents/Clawbot.Agents.Core/Skills/Ops/ILlmCostTracker.cs`

```csharp
public sealed record CostEntry(Guid TenantId, string AgentCode, string Model, int InputTokens, int OutputTokens,
    decimal UsdCost, DateTimeOffset At, Guid? ReservationId = null, Guid? SessionId = null, bool IsEstimated = false);
```

`ReviewCompletionEnvelope`: thêm `bool IsEstimated = false` làm param positional cuối. Kiểm tra lại thứ tự — `AnthropicContentReviewClient.Incomplete()` đã bỏ qua `InputTokens/OutputTokens/UsdCost/Model` nên chúng đã có default, append ở cuối là hợp lệ.

### 5.2 Áp fallback trong từng client

`src/agents/Clawbot.Agents.Core/Chat/OpenAiResponsesChatClient.cs` — cả `ReadSseReplyAsync` và `ParseReply`:

```csharp
var estimated = false;
if (inTok == 0 && outTok == 0)
{
    // Gateway không trả usage -> ước lượng cục bộ để cost cap và báo cáo không bị mù.
    inTok = LlmTokenEstimator.CountPrompt(_config.Model, systemPrompt, history, userMessage);
    outTok = LlmTokenEstimator.CountText(_config.Model, visibleText)
           + LlmTokenEstimator.CountText(_config.Model, reasoningSummary);
    estimated = true;
}
return new ClaudeReply(visibleText, inTok, outTok, Cost(inTok, outTok), _config.Model, estimated);
```

Điều kiện `inTok == 0 && outTok == 0` (AND, không OR) — provider hợp lệ có thể trả `output_tokens = 0` cho câu trả lời rỗng, đừng ghi đè.

`ReadSseReplyAsync` hiện **không nhận** prompt — phải truyền thêm `systemPrompt/history/userMessage` vào signature. `ParseReply` là `internal` (có test dùng), thêm param optional hoặc overload để không phá test hiện có.

Áp cùng pattern cho:
- `OpenAiChatClient` (`CompleteAsync`, `ParseDirectReply`)
- `OpenAiResponsesContentReviewClient` (`ParseSse`, `ParseCompletedJson`)
- `OpenAiChatContentReviewClient.ParseChatCompletion`
- `AnthropicChatClient` / `AnthropicContentReviewClient` — vẫn thêm cho đủ, dù đường này có usage thật.

`StreamAsync` của `OpenAiResponsesChatClient` chỉ cần chuyền `reply.IsEstimated` vào chunk final.

### 5.3 Domain + EF + migration

`src/shared/Clawbot.Domain/Agents/LlmCostEntry.cs`:

- Thêm `public bool IsEstimated { get; private set; }`
- `Create(...)`: thêm `bool isEstimated = false` ở cuối.
- `ApplyReservation(...)`: thêm `bool isEstimated = false` ở cuối, set field.
- `CreateReservation`: giữ `IsEstimated = false`.

`LlmCostEntryConfiguration.cs`: `builder.Property(x => x.IsEstimated).HasDefaultValue(false);`

**Migration mới** `deploy/migrations/0086_llm_cost_ledger_is_estimated.sql`:

```sql
-- Phân biệt chi phí ĐO ĐƯỢC (provider trả usage) vs ƯỚC LƯỢNG (tokenizer cục bộ).
-- Một SqlCommand, KHÔNG có GO. An toàn chạy lại (COL_LENGTH guard).
IF COL_LENGTH(N'dbo.claude_cost_ledger', N'is_estimated') IS NULL
    ALTER TABLE dbo.claude_cost_ledger ADD is_estimated BIT NOT NULL
        CONSTRAINT DF_claude_cost_ledger_is_estimated DEFAULT 0;
```

Ràng buộc bắt buộc nhớ (đã từng vấp):

- **Không có `GO`** — mỗi file chạy như 1 `SqlCommand`.
- Nếu sau này cần index trên cột vừa ALTER → phải tách sang file riêng.
- **`run-all.bat` chỉ replay `*.sql` khi DB trống.** DB hiện có sẵn schema sẽ đi qua khối `:repair_runtime_columns` hardcode. Phải thêm dòng ALTER này vào một câu `docker exec ... -Q "..."` trong khối đó (xem `run-all.bat:539-543`). Câu hiện tại đã gần sát trần 8191 ký tự của cmd.exe → **thêm lệnh `docker exec` mới riêng**, đừng nối vào câu cũ.

### 5.4 Đường ghi

`DbLlmCostTracker.RecordAsync`: truyền `entry.IsEstimated` vào cả `LlmCostEntry.Create(...)` và `reservation.ApplyReservation(...)`.

`ChatAgent` (ghi cost ở ~dòng 169 `ReplyAsync` và ~dòng 285 `StreamReplyAsync`) và `OrchestratorCostGuard.RecordAsync`: truyền `reply.IsEstimated` vào `CostEntry`.

---

## 6. P3 — API + UI

### 6.1 `TokensEndpoints`

Bổ sung vào response, không đổi field cũ (tránh vỡ FE — đã từng gặp: đổi shape list ở BE làm FE crash vì cast nói dối):

- `TokenUsageResponse`: `decimal EstimatedUsd`, `decimal MeasuredUsd`, `bool HasEstimated`.
- `TokenAgentUsageResponse`: `bool HasEstimated`.
- `TokenModelUsageResponse`: `bool HasEstimated`.

Tính bằng cách chia nhóm `costs` theo `cost.IsEstimated`. `Usd` tổng giữ nguyên = measured + estimated (để cột hiện tại không đổi nghĩa).

### 6.2 FE

- `src/frontend/clawbot-web/src/shared/api/tokens.ts` (hoặc tương đương): cập nhật type + cast.
- `TokenManagementPage.tsx`: khi `hasEstimated` → hiện chip trung tính (material icon, **không dùng icon kiểu emoji**) + tooltip:
  > "Nhà cung cấp không trả số token. Con số này do hệ thống tự đếm và **thấp hơn** thực tế vì không thấy được reasoning token."
- `formatUsd` hiện `maximumFractionDigits: 2` → tổng nhỏ hơn $0.005 hiển thị $0.00, đúng cái bug mà khách đang báo. Đổi sang 4 chữ số trên trang này, hoặc render `< $0.01` khi `0 < usd < 0.01`.
- Rà các chỗ khác cũng format USD: `AgentDashboardPage.tsx:153`, `TaskLogsPage.tsx:50` (đã 4 số), `AnalyticsReportsPage.tsx`, `PromptConfigurationPage.tsx:44`.

---

## 7. P4 — Hạn mức và rate

### 7.1 Rate hiện đang sai

Config active có `InputUsdPer1M = NULL`, `OutputUsdPer1M = NULL` → dùng default `3.00 / 15.00` (Claude Sonnet-era pricing), không liên quan gì đến giá gateway. Giá trị 3/3 trong ảnh **chưa được lưu**.

Hành động vận hành (không phải code): lấy bảng giá thật của `aigatewayport.com` cho `gpt-5.5` rồi nhập vào `/llm-providers`, bấm lưu, và **xác minh bằng SQL** rằng 2 cột đã khác NULL. Đường lưu (`AgentConfigDrawer.tsx:77-78,241-242` gửi `inputUsdPer1M/outputUsdPer1M` qua `toNullableNumber`) đã đúng — cần kiểm tra lại `/llm-providers` dùng đúng payload đó.

Cân nhắc code: hạ hai hằng số default `3.00/15.00` xuống, hoặc để `null` rate nghĩa là "không biết giá" và ghi token nhưng usd = 0 (có P0 rồi nên token vẫn vào sổ). Khuyến nghị: **giữ default, nhưng log warning khi rate = NULL** — im lặng dùng giá Claude cho model OpenAI là cái bẫy đã tự bộc lộ ở ca này.

### 7.2 Cap

Sau P0+P2, `MonthToDateUsd` tăng lại → cap $200 sống lại. Rủi ro cần nói trước với khách:

- Cap tính trên số **ước lượng thấp hơn thực tế** → cap sẽ bị vượt trong hóa đơn thật. Đề nghị khách đặt `Tenant.MonthlyCostCapUsd` với biên an toàn (ví dụ 60-70% ngân sách thật).
- Tháng này MTD khởi động từ ~0 nên **không có** nguy cơ chặn đột ngột ngay sau deploy.
- 85 dòng `__cost_reservation__` rác không cần dọn (usd = 0, đã bị `TokensEndpoints` lọc). Nếu muốn sạch thì `DELETE` riêng, ngoài phạm vi plan này.

### 7.3 Không thêm cờ bật/tắt

Fallback chỉ chạy khi provider **không** trả usage → provider chuẩn không bị ảnh hưởng. Thêm config flag là knob vô ích (YAGNI). Nếu sau này cần kill switch, thêm `Llm:EstimateUsageWhenMissing` vào appsettings.

---

## 8. Test

Bộ test .NET đã bị gỡ có chủ đích ở commit `5e24566` (CI hiện chỉ build + lint + E2E). Nếu khôi phục thì lấy scaffold từ `5e24566^` và bắt buộc có `tests/Directory.Build.props` (`NoWarn CA1707...`). Nếu không khôi phục, mục 8.1 chuyển thành script kiểm tra tay.

### 8.1 Unit (nếu có test project)

- `LlmTokenEstimator`: chuỗi rỗng → 0 (không tính priming); chuỗi ASCII đã biết → khớp số token tiktoken tham chiếu; text tiếng Việt có dấu → > `length/4`; model lạ (`cx/gpt-5.5-review`) → không throw, dùng o200k.
- `OpenAiResponsesChatClient` với `HttpClient` stub (đã có test seam `internal OpenAiResponsesChatClient(HttpClient, ResolvedLlmConfig)`):
  - SSE **có** usage → `IsEstimated == false`, dùng đúng số của provider.
  - SSE **không** usage (dựng lại đúng shape đã probe: `response.completed` chỉ có `background/created_at/error/id/object/status`) → `IsEstimated == true`, `InputTokens > 0`, `OutputTokens > 0`, `UsdCost > 0`.
  - SSE có `reasoning_summary_text.delta` → `OutputTokens` lớn hơn hẳn so với chỉ đếm `output_text`.
  - `usage` có nhưng `output_tokens = 0` và `input_tokens > 0` → **không** ước lượng đè.
- `DbLlmCostTracker.RecordAsync`: `(0,0,0)` → không ghi; `(120,50,0)` (cost làm tròn về 0) → **có** ghi; `IsEstimated` xuống đúng cột.
- `OrchestratorCostGuard`: reply có token / cost 0 → reservation được `ApplyReservation`, không bị release về 0.

### 8.2 Verify tay sau deploy

```sql
-- Phải xuất hiện dòng KHÔNG phải reservation trong vài phút sau khi chat/auto-reply chạy.
SELECT TOP 20 created_at, agent_code, model, input_tokens, output_tokens, usd, is_estimated
FROM dbo.claude_cost_ledger
WHERE agent_code <> '__cost_reservation__'
ORDER BY created_at DESC;

-- Tách đo được vs ước lượng trong 24h.
SELECT is_estimated, COUNT(*) AS rows_count, SUM(usd) AS usd, SUM(input_tokens + output_tokens) AS tokens
FROM dbo.claude_cost_ledger
WHERE agent_code <> '__cost_reservation__' AND created_at >= DATEADD(day, -1, SYSDATETIMEOFFSET())
GROUP BY is_estimated;
```

Sau đó mở `/tokens` xác nhận số khác 0 và chip "ước lượng" hiện đúng.

---

## 9. Thứ tự triển khai

| Bước | Nội dung | Ship độc lập được? |
|---|---|---|
| 1 | P0 (3 guard + log warning dedupe) | Có — deploy ngay, giá trị tức thì |
| 2 | Migration 0086 + `run-all.bat` repair + domain/EF `IsEstimated` | Có (cột default 0, chưa ai ghi) |
| 3 | P1 tokenizer + P2 áp fallback trong các client | Có |
| 4 | P3 API + FE nhãn ước lượng | Có |
| 5 | P4 nhập rate thật + đặt cap có biên | Vận hành, không cần deploy |

Sau bước 3 phải chạy `dotnet build` Release — `NuGetAudit` và analyzer CA là **error**, package mới có thể làm vỡ build.

---

## 10. Rủi ro

| Rủi ro | Mức | Xử lý |
|---|---|---|
| Ước lượng thấp hơn hóa đơn thật (không thấy reasoning token) | Cao, không sửa được | Cộng thêm reasoning summary; ghi nhãn "ước lượng" trên UI; nói thẳng với khách. Đây là giới hạn của phương án 3. |
| Rate default 3/15 không phải giá gateway | Cao | P4.1 — nhập rate thật, log warning khi rate NULL |
| Package tokenizer tải vocab qua mạng lúc chạy | Trung bình | Bắt buộc dùng `Microsoft.ML.Tokenizers.Data.O200kBase` (vocab nhúng), không dùng `CreateForModel` |
| `CreateForModel("gpt-5.5")` throw | Trung bình | Chỉ dùng `CreateForEncoding` + tự map + try/catch fallback `length/4` |
| Cap $200 sống lại gây chặn ngoài dự kiến | Thấp (MTD tháng này ~0) | Thông báo trước; kiểm tra `Tenant.MonthlyCostCapUsd` |
| Đổi shape response `/api/tokens/usage` làm FE crash | Trung bình | Chỉ **thêm** field, không đổi field cũ; sửa cả axios cast lẫn consumer (tsc không bắt được vì cast nói dối) |
| Overhead tokenizer trên đường nóng auto-reply | Thấp | Tokenizer cache theo encoding trong `ConcurrentDictionary`; chỉ chạy khi usage vắng |
| Log spam mỗi tin nhắn | Trung bình | Dedupe 60 phút theo `tenantId\|model` |

---

## 11. Điều phải nói với khách

1. Nguyên nhân **không phải bug tính tiền của ClawBot** — gateway `aigatewayport.com` không trả số token, và không có endpoint usage/billing nào để đối chiếu.
2. Từ 2026-07-07 đến nay không có dữ liệu chi phí. **Không backfill được** — số liệu gốc không tồn tại.
3. Hạn mức $200/tháng đã không hoạt động trong cùng khoảng thời gian đó.
4. Sau khi vá, con số hiển thị là **ước lượng và thấp hơn thực tế**, vì model reasoning đốt token vô hình. Muốn số chính xác thì phải đổi sang provider có trả `usage` (OpenAI trực tiếp, Anthropic, hoặc gateway khác) — đó là phương án 1/2 đã bị loại.
5. Cần khách cung cấp bảng giá thật của gateway để nhập vào `/llm-providers`; hiện hệ thống đang dùng giá mặc định 3/15 USD/1M không liên quan đến gateway.
