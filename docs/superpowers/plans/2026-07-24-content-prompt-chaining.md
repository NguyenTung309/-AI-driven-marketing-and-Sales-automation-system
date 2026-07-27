# Plan: Prompt chaining cho luồng sinh nội dung (Content)

**Ngày:** 2026-07-24
**Trạng thái:** **ĐÃ CHỐT THIẾT KẾ** 2026-07-24 — sẵn sàng vào P1

**Ba quyết định khóa (chốt 2026-07-24):**

1. **Áp cho cả hai đường** — thủ công (nút "Tạo nháp AI") và tự động (orchestrator). Xem §2.2: cả hai cùng gọi `ContentAgent.GenerateAsync`, nên **một lần cài đặt phủ cả hai**.
2. **Chọn hook tự động, nhưng giữ được cả đường tay** — chuỗi chạy một mạch, lưu cả 3 hook vào trace; marketer đổi hook thì chạy lại từ L3. Không dừng chuỗi giữa chừng, không cần bảng state (§4.5).
3. **Refine bám `ContentReviewer` sẵn có** — không tạo agent review mới, không tạo agent code mới. Lý do `reject` được đưa ngược vào step `write`, đúng 1 vòng, rồi chấm lại bằng chính reviewer cũ (§4.7, P6).
**Nhánh:** thang/ai-autoreply-kb-improvements
**Nguồn yêu cầu:** khách nghiên cứu [Chapter 1: Prompt Chaining](../../Chapter%201_%20Prompt%20Chaining.md) (bản trong repo) và đề nghị áp dụng cho luồng xây dựng nội dung đăng bài. Chat và Sale Assist đã tách prompt; Content vẫn là một prompt duy nhất.

## 1. Mục tiêu

Chuyển bước sinh bài từ **một lần gọi LLM ôm hết mọi ràng buộc** sang **chuỗi mắt xích nhỏ, mỗi mắt xích có hợp đồng đầu ra và cổng kiểm tất định giữa hai mắt xích**.

Kết quả mong muốn:

- Chặn bịa số liệu bằng **code** (đối chiếu citation) thay vì bằng lời dặn trong prompt.
- Sửa được một khâu (hook, hashtag, giọng nền tảng) mà không phải viết lại cả prompt.
- Repurpose sang nền tảng khác **tái dùng kế hoạch + dàn ý**, chỉ chạy lại khâu viết và đóng gói.
- Debug được: biết bài hỏng ở mắt xích nào, tốn bao nhiêu token ở mắt xích nào.

Không mục tiêu (giai đoạn này): routing theo loại bài, fan-out song song nhiều biến thể, tự sinh ảnh, đổi luồng duyệt/đăng.

## 2. Hiện trạng (khảo sát mã nguồn 2026-07-24)

| Điểm | Vị trí | Ghi chú |
|---|---|---|
| Sinh bài = 1 lần gọi LLM | [ContentAgent.cs:42-74](../../../src/agents/Clawbot.Agents.Core/Content/ContentAgent.cs#L42-L74) | RAG TopK=4 → render 1 template → `CompleteAsync` → lấy nguyên `reply.Text` làm body |
| Prompt để ở config | [ContentPromptTemplates.cs:9](../../../src/agents/Clawbot.Agents.Core/Content/ContentPromptTemplates.cs#L9), `Content:PromptTemplates` trong `appsettings.json` (Api + AgentService + api-publish) | Mỗi nền tảng 1 chuỗi ~250-300 ký tự, chỉ có `{{brief}}` + `{{knowledge}}` |
| Repurpose | [ContentAgentGrpcService.cs:128-144](../../../src/agents/Clawbot.AgentService/Services/ContentAgentGrpcService.cs#L128-L144) | Lặp đúng cú gọi đó cho từng nền tảng — viết lại từ đầu, không tái dùng gì |
| Đường vào của UI | [ContentEndpoints.cs:55](../../../src/api/Clawbot.Api/Endpoints/ContentEndpoints.cs#L55) → job `content.generate` → [ContentGenerateJobHandler.cs:22](../../../src/api/Clawbot.Api/Jobs/ContentGenerateJobHandler.cs#L22) | Chạy nền qua Job Center ⇒ **độ trễ chuỗi chấp nhận được**, không chặn UI |
| Gate đã có | [ContentAgentGrpcService.cs:180-239](../../../src/agents/Clawbot.AgentService/Services/ContentAgentGrpcService.cs#L180-L239), [ContentReviewer.cs:128-181](../../../src/agents/Clawbot.Agents.Core/Content/ContentReviewer.cs#L128-L181) | Đã là mắt xích thứ hai: tách trusted/untrusted, parse nghiêm, fail-closed về `needs_human` |
| Mẫu để bắt chước | `ReviewPromptPart.TrustedSystem` / `UntrustedText`, `StrictContentReviewOutcomeParser`, `AgentPromptDefaults.Compose` | Chuỗi mới **dùng lại nguyên các mẫu này**, không phát minh cơ chế mới |

Kết luận: hạ tầng đã có sẵn hình dạng của chain (1 gate LLM riêng + strict parser + ranh giới untrusted). Thiếu duy nhất là bước sinh bài chưa được chẻ.

### 2.1 Prompt content thực tế dài bao nhiêu (đo trên DB `clawbot` + mã nguồn, 2026-07-24)

Đã quét toàn bộ DB tìm mọi cột chứa prompt (`sys.columns` lọc `%prompt%`, `%instruction%`, `%persona%`, `%template%`, `%system%`) và mọi cột `nvarchar(max)` của các bảng `agent*` / `content*` / `*config*`. **Không có bản prompt content dài nào trong DB.** Số đo:

| Nguồn | Nội dung | Độ dài thực đo |
|---|---|---|
| `agent_definitions.persona_prompt` (`content-agent`) | "Create campaign content briefs and channel-ready drafts." | **56 ký tự** (dài nhất toàn bảng là `lead-agent`: 158) |
| `agents.config_json` → `systemPrompt` (`content-agent`) | "Bạn sáng tạo nội dung marketing cho trung tâm tiếng Trung…" | **~146 ký tự** — và **không đường nào đọc nó cho luồng sinh bài**, xem bên dưới |
| `Content:PromptTemplates` (appsettings) | 5 nền tảng | **250-267 ký tự / nền tảng** |
| `content_briefs.brief` (34 dòng thật) | brief marketer gõ | trung bình **152**, dài nhất **269** ký tự |
| `skill_files` | — | **rỗng (0 dòng)** |
| `AgentPromptDefaults.BaseGuardrail` | 5 quy tắc khoá | **459 ký tự** |
| Khối chỉ dẫn ReAct tĩnh trong `GenericLlmAgentWorker` | 11 dòng | **1.422 ký tự** |

Ghép lại thành prompt runtime, có **hai đường** và chúng dài khác hẳn nhau:

- **Đường A — nút "Tạo nháp AI" / job `content.generate` / gRPC `Generate`** (`ContentAgent.GenerateAsync`): system = **chuỗi rỗng** (`CompleteAsync(string.Empty, …)` tại [ContentAgent.cs:61](../../../src/agents/Clawbot.Agents.Core/Content/ContentAgent.cs#L61)), user = template (~260) + brief (~150) + KB (tối đa 4 chunk × 1.000 = 4.000). **Tổng ≤ ~4.500 ký tự (~1.200 token).** Guardrail và `systemPrompt` trong DB **đều không được nạp** ở đường này.
- **Đường B — `content-agent` chạy như agent con trong kế hoạch orchestrator** ([GenericLlmAgentWorker.cs:329-360](../../../src/agents/Clawbot.Agents.Core/Orchestrator/GenericLlmAgentWorker.cs#L329-L360)): BaseGuardrail 459 + roleInstruction + ReAct 1.422 + danh mục 4 tool (~700) + KB (~4.000). **Tổng ~6.800-7.000 ký tự (~2.000-2.400 token).**

**Kết luận đổi trọng tâm của plan:** cảm giác "prompt đang dài quá" **không đến từ số ký tự** — cả hai đường đều dưới 2.500 token, không hề chạm trần context. Nó đến từ **mật độ nhiệm vụ trong một prompt**, và đường B là chỗ đã có bằng chứng hỏng thật:

- Khối ReAct phải chèn 6 gạch đầu dòng "IMPORTANT — act, do not just describe" ([GenericLlmAgentWorker.cs:344-349](../../../src/agents/Clawbot.Agents.Core/Orchestrator/GenericLlmAgentWorker.cs#L344-L349)) chỉ để ép model gọi tool thay vì mô tả suông. Đây **chính là instruction neglect** mà Chapter 1 mô tả: mỗi lần model bỏ sót một ràng buộc, ta lại nhét thêm một câu dặn vào cùng một prompt — và prompt phình ra theo đúng vòng xoáy sách cảnh báo.
- Một prompt duy nhất đang bắt model đồng thời: giữ vai copywriter, tuân giao thức ReAct, chọn đúng tool trong 4 tool, không bịa số liệu từ KB untrusted, và nhớ tái dùng `content_id` từ `upstream_results`.

⇒ Chuỗi trong plan này **không nhằm cắt token**; nó nhằm **giảm số ràng buộc đồng thời trong một prompt**, để chỗ nào hỏng thì thay bằng cổng kiểm tất định (§4) chứ không thêm câu dặn.

### 2.2 Hai đường gặp nhau ở đúng một hàm

Đã chốt áp chuỗi cho **cả hai đường**. Tin tốt: không phải làm hai lần.

```
Nút "Tạo nháp AI" ─► job content.generate ─► gRPC Generate ──┐
                                                             ├─► ContentAgent.GenerateAsync ─► [CHUỖI]
Orchestrator ─► tool "content-agent" (ContentTools.cs:70) ────┘
```

`ContentAgentTool.InvokeAsync` gọi thẳng `_agent.GenerateAsync(...)` tại [ContentTools.cs:70-71](../../../src/agents/Clawbot.AgentService/Services/ContentTools.cs#L70-L71) — **cùng một điểm vào** với đường thủ công. Nên ràng buộc "giữ nguyên chữ ký `GenerateAsync`, chuỗi nằm bên trong" (§5.A) không chỉ để đỡ phải sửa gRPC: nó là **cách phủ cả hai đường bằng một lần cài đặt**, và là cách để cờ tắt/fallback ở §7 áp đồng thời cho cả hai.

Khác biệt duy nhất giữa hai đường sau khi có chuỗi: đường tự động **ghi thêm** `ContentItem` + review task ngay trong tool ([ContentTools.cs:83-98](../../../src/agents/Clawbot.AgentService/Services/ContentTools.cs#L83-L98)), còn `ctx.DryRun` chặn trước khi gọi agent — chuỗi không đụng gì tới hai chỗ đó.

**Nói rõ giới hạn:** chuỗi chữa **chất lượng bài viết** trên cả hai đường. Nó **không** chữa việc agent con ở đường tự động đôi khi mô tả suông thay vì gọi tool — đó là instruction neglect ở tầng giao thức ReAct (§2.1), nằm trong `GenericLlmAgentWorker`, cần một plan riêng cho tầng orchestrator. Đừng kỳ vọng làm xong plan này là hết luôn triệu chứng đó.

## 3. Đối chiếu với Chapter 1

Sách nêu 5 lỗi của prompt đơn khối: **instruction neglect** (bỏ sót ràng buộc), **contextual drift** (trôi khỏi ngữ cảnh ban đầu), **error propagation** (lỗi sớm bị khuếch đại), **context window** không đủ, và **hallucination** do tải nhận thức tăng. Ba lỗi đầu khớp đúng triệu chứng đang gặp ở content: bài quên CTA hoặc quên ràng buộc độ dài, giọng trôi khỏi brief, và số liệu bịa lọt tới tận khâu duyệt.

Ví dụ Content Generation Workflows của sách (§4 của chương) và chuỗi trong plan này:

| Sách | Plan này | Khác biệt và lý do |
|---|---|---|
| P1: sinh 5 ý tưởng chủ đề | Đã có sẵn ở tầng trên: trend scan → `content_briefs` | Không làm lại khâu ideation trong chuỗi; brief là đầu vào |
| *Processing*: người chọn hoặc máy tự chọn 1 ý | **Bước xử lý không LLM giữa L2 và L3** (§4.5) | Sách đặt bước chọn thành first-class — plan trước đây để lẫn vào Q2, nay tách thành bước riêng |
| P2: sinh dàn ý chi tiết | L2 `outline` | Bổ sung `proofPoints` + cổng đối chiếu citation — sách không có |
| P3, P4: viết từng đoạn, đoạn sau nhận đoạn trước làm ngữ cảnh | L3 `write` — **viết một lượt** | Bài social 150-300 chữ, chẻ theo đoạn là chẻ quá vụn; giữ vòng lặp theo đoạn cho bài dài (Website) nếu sau này mở |
| P5: review và refine bản hoàn chỉnh | L5 gate `ContentReviewer` (chấm, không sửa) | **Khoảng trống thật**: ta có khâu chấm, chưa có khâu sửa — xem Q6 và P6 |
| Gán vai riêng cho từng bước ("Market Analyst", "Trade Analyst"…) | Mỗi step có persona riêng trong prompt | Persona khác nhau, nhưng **binding LLM vẫn dùng chung agent code `content-agent`** — xem §5.B |
| Structured output JSON giữa các bước | Đã áp dụng cho L1/L2/L4 | L3 trả plain text có chủ đích (§4.3) |
| LangChain / LangGraph / Google ADK | Tự cài đặt trong .NET | Pattern này không cần framework; kéo thêm dependency chỉ để có `chain.invoke()` là lỗ hổng bảo trì, chưa kể hệ đang chạy net8/net10 với build gate chặt |

Phần **Context Engineering** của chương nói thẳng: chất lượng đầu ra phụ thuộc vào chất lượng ngữ cảnh nhiều hơn phụ thuộc model. Plan này áp đúng ở chỗ **mỗi mắt xích chỉ nhận đúng ngữ cảnh nó cần** (§4.6) — vừa giảm token, vừa thu hẹp bề mặt injection, vừa tránh contextual drift.

## 4. Thiết kế chuỗi

Bốn mắt xích sinh + một bước xử lý không LLM + gate duyệt sẵn có. Giữa hai mắt xích luôn có **cổng kiểm tất định (không LLM)**.

```
brief ─► [L1 plan] ─G1─► [L2 outline] ─G2─► (chọn hook) ─► [L3 write] ─G3─► [L4 package] ─G4─► lint ─► [ContentReviewer]
             ▲                 ▲
             └── KB chunks ────┘   (RAG giữ nguyên, chạy 1 lần, chỉ L2 dùng)
```

### 4.1 L1 — `plan`: chuẩn hóa brief

Persona: chuyên viên hoạch định nội dung. Vào: brief thô + nền tảng. Ra:

```json
{
  "objective": "awareness|lead_gen|nurture|promo",
  "audience": "string",
  "keyMessage": "string",
  "offer": "string|null",
  "tone": "string",
  "cta": { "type": "inbox|comment|link|call", "text": "string" },
  "mustInclude": ["string"],
  "mustAvoid": ["string"],
  "language": "vi"
}
```

**G1:** parse được JSON; `keyMessage` không rỗng; `objective`/`cta.type`/`language` thuộc allow-list; mỗi field ≤ giới hạn ký tự; không chứa URL hay markup lạ. Fail → 1 lần repair (gọi lại kèm mã lỗi cổng), fail tiếp → fallback (§7).

### 4.2 L2 — `outline`: dàn ý + hook + bằng chứng

Persona: biên tập viên. Vào: JSON L1 + KB chunks đánh số `[1..k]` (giữ đúng format `BuildKnowledgeContext` hiện tại). Ra:

```json
{
  "hooks": ["string", "string", "string"],
  "outline": [{ "section": "string", "points": ["string"] }],
  "proofPoints": [{ "claim": "string", "citationId": 1 }],
  "riskFlags": ["string"]
}
```

**G2 — cổng quan trọng nhất:** mọi `citationId` phải nằm trong tập id chunk đã truyền vào. `proofPoint` trỏ tới id không tồn tại → **loại bỏ proofPoint đó** và gắn `evidence_missing` vào trace (không sửa, không bịa thêm). `hooks` rỗng → fail. Đây là chỗ biến "đừng bịa số liệu" từ lời dặn thành ràng buộc thực thi được — và cũng là chỗ chặn **error propagation** mà sách cảnh báo: chuỗi khuếch đại lỗi sớm, nên mắt xích nào cũng phải có van chặn.

### 4.3 L3 — `write`: viết thân bài theo giọng nền tảng

Persona: người viết nội dung của nền tảng tương ứng. Vào: L1 + hook đã chọn + dàn ý + **chính template nền tảng đang có** (`Content:PromptTemplates:{platform}`) làm phần mô tả giọng/độ dài. Ra: **plain text**, không JSON — văn bản dài có xuống dòng, dấu ngoặc, emoji nên bọc JSON chỉ tăng tỉ lệ hỏng parse mà không thêm giá trị (JSON có giá trị khi đầu ra là *dữ liệu có cấu trúc*, không phải khi đầu ra là *một khối văn bản*).

Ràng buộc trong prompt: chưa gắn hashtag, chưa gắn link, không viết lại CTA (CTA đến từ L1).

**G3:** độ dài trong khoảng min/max của nền tảng; không chứa URL; không còn placeholder `{{`; không sao chép nguyên văn brief; ngôn ngữ khớp `language`.

### 4.4 L4 — `package`: đóng gói theo nền tảng

Persona: người tối ưu bài đăng theo nền tảng. Vào: body + `cta` + nền tảng. Ra:

```json
{
  "caption": "string",
  "hashtags": ["#string"],
  "firstComment": "string|null",
  "altText": "string|null"
}
```

**G4:** tổng ký tự caption ≤ giới hạn nền tảng (số cụ thể chốt ở Q5); hashtag không trùng, không khoảng trắng, không nằm trong danh sách cấm, số lượng ≤ ngưỡng; `firstComment` chỉ dùng cho nền tảng có quy ước đó.

**Ghi vào DB:** `ContentItem` chỉ có `Body` (string). P1 **merge** `caption` + `hashtags` thành `Body` theo quy tắc từng nền tảng, **không đổi schema**. `firstComment`/`altText` chỉ ghi vào trace ở P1; muốn dùng thật thì mở rộng ở phase sau.

### 4.5 Bước xử lý (không LLM): chọn hook

Sách tách hẳn bước "Processing" giữa hai prompt. **Chốt: tự động chọn, và vẫn có đường cho marketer đổi tay — bằng chạy lại, không bằng dừng chờ.**

Cách làm:

1. L2 trả **3 hook**. Cổng chấm điểm tất định (độ dài, có số liệu đã qua G2, khớp `mustInclude`, không trùng ý nhau) rồi lấy điểm cao nhất. Không có LLM nào ở bước này.
2. Chuỗi **chạy thẳng** tới L4 và trả nháp. Cả 3 hook + điểm + hook nào được chọn ghi vào `content_generation_traces`.
3. Marketer xem nháp, muốn giọng mở bài khác thì bấm **"Đổi hook"** → job `content.regenerate` chạy lại **từ L3** với `hookIndex` đã chọn, tái dùng nguyên L1/L2 trong trace.

Vì sao không dừng chuỗi chờ người chọn: dừng giữa chừng thì `GenerateAsync` phải trả về khi bài chưa xong, kéo theo bảng state cho chuỗi dở dang, hai job nối nhau, và **đường tự động (orchestrator) không có ai để chờ** — agent con sẽ treo hoặc coi như hỏng. Chạy lại từ trace cho đúng kết quả người dùng muốn mà giữ mô hình "một job chạy hết", và dùng được cho cả hai đường ở §2.2.

Chi phí của "đổi hook": chỉ L3+L4, không chạy lại L1/L2 và không gọi lại RAG.

### 4.6 Ngữ cảnh tối thiểu cho từng mắt xích

| Mắt xích | Nhận | KHÔNG nhận |
|---|---|---|
| L1 `plan` | brief thô, nền tảng | KB (chưa cần — sẽ làm nhiễu việc chuẩn hóa ý định) |
| L2 `outline` | JSON L1, KB chunks | brief thô (đã được L1 chưng cất) |
| L3 `write` | JSON L1, hook đã chọn, dàn ý, template giọng | KB thô (chỉ nhận proofPoint đã qua cổng) |
| L4 `package` | body, `cta`, nền tảng | brief, KB, dàn ý |

Nguyên tắc: **văn bản khách/KB thô chỉ đi vào chuỗi đúng một lần rồi bị chưng cất** — mỗi lần bơm lại nguyên văn vào mắt xích sau là một lần mở lại cửa injection và một lần mời model trôi ngữ cảnh.

### 4.7 L5 — gate (đã có, chỉ bổ sung) và vòng refine bám reviewer cũ

Trước khi gọi `ContentReviewer`: chạy **lint tất định** (regex số điện thoại lạ, cam kết tuyệt đối kiểu "100% đỗ", link ngoài allow-list, ký tự rác). Dính lint → `needs_human` ngay, **không tốn một call LLM**. Qua lint mới gọi reviewer như hiện tại, giữ nguyên fail-closed và separation of duties.

**Refine (chốt: bám reviewer sẵn có, không dựng agent mới).** Sách có bước revise; ta có sẵn khâu chấm, chỉ nối thêm khâu sửa vào đúng khâu chấm đó:

```
[L4 package] ─► lint ─► [ContentReviewer] ─ approve ──────────────► xong
                             │
                             ├─ reject (lý do sửa được) ─► [L3 write lại, kèm lý do] ─► lint ─► [ContentReviewer] ─► approve / needs_human
                             │                                    (đúng 1 vòng)
                             └─ needs_human ────────────────────► người duyệt
```

Ràng buộc cứng:

- **Không agent code mới, không `agent_definitions` mới.** Bước sửa là step `write` chạy lại dưới `content-agent` với persona biên tập; bước chấm vẫn là `reviewer-agent` cũ. Separation of duties hiện có (`item.CreatedByAgentId == reviewerDefId → needs_human`) nhờ vậy vẫn đúng — người sửa và người chấm vẫn là hai agent khác nhau.
- **Đúng 1 vòng cho mỗi revision.** Vòng 2 mà reviewer vẫn `reject` → `needs_human`, dừng hẳn. Đếm bằng cột trên review task, không đếm trong bộ nhớ tiến trình.
- **`needs_human` không bao giờ kích hoạt refine** — chỉ `reject` kèm lý do máy đọc được mới vào vòng sửa. Nghi ngờ thì để người xử.
- Reviewer **vẫn không tự sửa bài** (đúng persona hiện tại của `reviewer-agent`); nó chỉ trả lý do, việc sửa nằm ở `content-agent`.

## 5. Điểm chạm mã nguồn dự kiến

### A. Lõi chuỗi (Agents.Core/Content)

- **Mới** `ContentChain.cs` — điều phối tuần tự, giữ `ChainState`, ghi trace, xử lý repair/fallback.
- **Mới** `IContentChainStep.cs` + 4 lớp step (`PlanStep`, `OutlineStep`, `WriteStep`, `PackageStep`).
- **Mới** `ContentChainContracts.cs` — record `ContentPlan`, `ContentOutline`, `ContentPackage`, `ChainStepResult`.
- **Mới** `ContentChainGates.cs` — toàn bộ cổng kiểm tất định G1-G4 + chọn hook, thuần hàm, không phụ thuộc LLM (dễ unit test).
- **Sửa** [ContentAgent.cs:42](../../../src/agents/Clawbot.Agents.Core/Content/ContentAgent.cs#L42) — **giữ nguyên chữ ký `GenerateAsync`**, bên trong rẽ nhánh: chain bật thì gọi `ContentChain`, tắt hoặc lỗi thì chạy đường single-shot cũ. gRPC, `ContentGenerateTool`, orchestrator **không phải sửa gì**.
- **Sửa** [ContentPromptTemplates.cs:68](../../../src/agents/Clawbot.Agents.Core/Content/ContentPromptTemplates.cs#L68) — loader đọc thêm nhánh `Content:Chain`; `ContentModule.AddClawbotContent` đăng ký chain + các step.

### B. Client LLM và parse

- Dùng lại `ILlmConfigResolver` + factory client đã có; **mọi step resolve dưới cùng agent code `content-agent`**. Sách khuyên gán vai riêng cho từng bước — vai nằm ở **persona trong prompt**, không phải ở agent code. Tách thành 4 agent code mới thì tenant chưa bind LLM cho code lạ sẽ rơi vào `llm_config_not_configured` (đúng vết đã gặp ở orchestration), đổi lấy zero lợi ích.
- Ranh giới untrusted: system = chỉ dẫn tin cậy (`AgentPromptDefaults.Compose` + persona của step), **toàn bộ brief / KB / output của mắt xích trước đưa vào phần user như dữ liệu**. Output LLM là dữ liệu, không phải chỉ thị — nếu không giữ nguyên tắc này, injection từ brief hoặc KB sẽ lan suốt chuỗi.
- Parse JSON: mẫu `StrictContentReviewOutcomeParser`; serialize `JsonSerializerDefaults.Web` (camelCase — đã dính bug `ResultSummary` PascalCase làm FE đọc `undefined`).

### C. Trace

- **Mới** bảng `content_generation_traces` (tenant-scoped): `content_item_id`, `step_id`, `prompt_version`, `model`, `input_tokens`, `output_tokens`, `usd_cost`, `latency_ms`, `gate_result`, `payload_json` (đã PII-redact), `created_at`. Retention 30 ngày.
- Migration theo luật repo: 1 file = 1 `SqlCommand`, **không có `GO`**; index trên cột vừa ALTER phải tách file riêng; đồng thời **thêm vào khối repair trong `run-all.bat`** vì replay `*.sql` chỉ chạy trên DB mới.

### D. Cấu hình

```
Content:Chain:Enabled            = false            # tắt mặc định
Content:Chain:TenantAllowList    = ["<slug>"]       # bật dần
Content:Chain:Version            = "2026-07-24.1"   # ghi vào trace để so sánh chất lượng khi sửa prompt
Content:Chain:Steps:plan:_default    = "<prompt>"
Content:Chain:Steps:outline:_default = "<prompt>"
Content:Chain:Steps:write:facebook   = "<prompt>"   # thiếu thì fallback _default + template nền tảng cũ
Content:Chain:Steps:package:instagram= "<prompt>"
```

Key phẳng `Content:PromptTemplates:{platform}` **giữ nguyên** — vừa là đường single-shot để rollback, vừa được L3 dùng lại làm mô tả giọng nền tảng.

## 6. Chi phí, độ trễ, đo lường

- 1 call → 4 call. Mỗi prompt và mỗi output nhỏ hơn nên token tổng thường tăng **~1.5-2x**, không phải 4x. Repurpose thì **rẻ hơn hiện tại** vì tái dùng L1/L2.
- Độ trễ tăng thật, nhưng `content.generate` chạy nền qua Job Center nên không chặn UI. Cần cap tổng thời gian chuỗi (đề xuất 60s) và cap riêng từng step (đề xuất 15s), vượt → fallback.
- Ledger chi phí giữ nguyên `agentCode = content-agent` ở P1 (không đổi schema ledger); tách theo step đọc từ bảng trace.
- Chỉ số cần theo dõi sau khi bật: tỉ lệ fail từng cổng G1-G4, tỉ lệ fallback về single-shot, tỉ lệ verdict `approve` của reviewer trước/sau, token trung bình mỗi bài, độ trễ p95.

## 7. Bật/tắt, fallback, rollback

| Tình huống | Xử lý |
|---|---|
| `Content:Chain:Enabled=false` hoặc tenant ngoài allow-list | Chạy single-shot như hiện tại |
| Một step lỗi cổng lần 1 | Repair 1 lần: gọi lại đúng step đó kèm mã lỗi cổng |
| Repair vẫn lỗi, hoặc LLM timeout/down | **Fallback single-shot**, ghi `chain_fallback` + lý do vào trace, job vẫn trả nháp — marketer không thấy job fail |
| Chuỗi vượt cap tổng thời gian | Fallback single-shot |
| Cần tắt gấp | Đổi 1 cờ config, không cần deploy lại code |

Fallback ở đây là fail-open **có chủ đích** (vẫn còn gate reviewer fail-closed phía sau chặn nội dung xấu), khác với gate duyệt.

## 8. Chia phase

| Phase | Nội dung | Rủi ro |
|---|---|---|
| **P1 — Khung chuỗi + 2 mắt xích** | `IContentChainStep`, `ContentChain`, cổng G1/G3, step `plan` + `write`, cờ config, fallback, trace ghi bảng mới | Thấp — chưa đụng KB citation |
| **P2 — Mắt xích bằng chứng** | Step `outline` + cổng G2 (đối chiếu citationId) + chọn hook auto, riskFlags | Trung bình — đây là phần đổi chất lượng nhiều nhất |
| **P3 — Đóng gói nền tảng** | Step `package` + G4, merge caption/hashtag vào Body theo nền tảng, lint tất định trước reviewer | Trung bình — phải chốt giới hạn ký tự từng nền tảng |
| **P4 — Repurpose tái dùng** | Repurpose dùng lại plan/outline của item gốc, chỉ chạy L3+L4 cho nền tảng đích | Thấp — giảm cả chi phí lẫn lệch thông điệp |
| **P5 — Vận hành + nút "Đổi hook"** | Cột/flag per-tenant + admin UI, dashboard chỉ số §6, retention trace; job `content.regenerate` chạy lại từ L3 với `hookIndex` (§4.5) | Thấp — chạy lại từ trace, không có state chuỗi dở dang |
| **P6 — Vòng refine bám reviewer cũ** | Reviewer `reject` có lý do máy đọc được → chạy lại step `write` kèm lý do, **đúng 1 vòng**, rồi chấm lại bằng `reviewer-agent` cũ; quá 1 vòng → `needs_human` (§4.7) | Trung bình — bộ đếm phải nằm trên review task, không nằm trong bộ nhớ tiến trình |

## 9. Kiểm thử

**Unit (bắt buộc, ưu tiên cao nhất):** mọi cổng G1-G4 là hàm thuần → bảng case gồm JSON thiếu field, JSON sai enum, citationId lạ, body quá dài/quá ngắn, hashtag trùng/có dấu cách, body lẫn URL, body còn `{{placeholder}}`. Thêm case chọn hook: hooks rỗng, hooks trùng nhau, hook dài bất thường.

**Unit parse:** LLM trả kèm chữ thừa quanh JSON, trả markdown fence, trả JSON rỗng → phải ra lỗi có mã, không throw.

**Integration (fake LLM client):**
- Mỗi step trả hợp lệ → item draft được tạo, trace đủ 4 dòng.
- Step 2 trả citationId lạ → proofPoint bị loại, item vẫn tạo, trace có `evidence_missing`.
- Step 3 timeout → fallback single-shot, job **thành công**, trace có `chain_fallback`.
- Injection nhét trong brief ("bỏ qua hướng dẫn…") → không đổi hành vi các step, reviewer vẫn chặn.

**E2E:** tạo nháp từ brief thật trên 3 nền tảng → so sánh cạnh nhau với đường single-shot (giữ cùng brief, cùng KB) để khách nghiệm thu chất lượng, không chỉ nghiệm thu kỹ thuật.

**Regression:** repurpose 3 nền tảng, review gate approve/reject/needs_human, publish job không đổi.

## 10. Việc KHÔNG làm

- Không đổi chữ ký gRPC `Generate`/`Repurpose` và không đụng `ContentGenerateTool`.
- Không tạo `agent_definitions` mới cho từng step (tránh bắt tenant bind LLM 4 lần).
- Không kéo LangChain/LangGraph hay framework tương đương vào .NET chỉ để có chuỗi tuyến tính.
- Không đụng luồng schedule/publish, không đụng `ContentReviewer` logic verdict.
- Không làm routing theo loại bài, không fan-out song song nhiều biến thể ở giai đoạn này — chương 1 mới là chaining tuyến tính; chẻ quá sớm sẽ thành 4 prompt mơ hồ thay vì 1 prompt dài.

## 11. Rủi ro

| Rủi ro | Giảm thiểu |
|---|---|
| Chẻ quá vụn, mỗi mắt xích mơ hồ | Mỗi step phải có hợp đồng JSON test được; step nào không có cổng kiểm tất định thì không tách |
| Error propagation (sách nêu) — lỗi mắt xích đầu chảy xuống cuối | Cổng tất định sau **mỗi** mắt xích, không chỉ ở cuối chuỗi |
| Lệch giọng giữa các mắt xích | L3 dùng lại đúng template nền tảng hiện tại; giọng chỉ khai báo ở đúng một chỗ |
| Prompt injection lan theo chuỗi | Output mắt xích trước luôn vào phần untrusted; ngữ cảnh tối thiểu theo §4.6; giữ heuristic `LooksLikeInstructionInjection` ở đầu và cuối chuỗi |
| Chi phí tăng ngoài dự kiến | Cờ tắt tức thì + trace token theo step + cap thời gian |
| Trace lưu dữ liệu khách | PII-redact trước khi ghi, retention 30 ngày |

## 12. Trạng thái các câu hỏi chặn

| # | Câu hỏi | Kết luận |
|---|---|---|
| Q1 | Prompt content "dài quá" là bản nào? Áp cho đường nào? | **CHỐT.** Không tồn tại prompt dài (§2.1: persona 56 ký tự, systemPrompt 146 không được dùng, template 250-267). Áp cho **cả hai đường** — và vì cả hai cùng gọi `GenerateAsync` (§2.2) nên chỉ cần một lần cài đặt |
| Q2 | Chọn hook tay hay tự động? | **CHỐT: tự động**, giữ đường tay bằng nút "Đổi hook" chạy lại từ L3 (§4.5). Không dừng chuỗi, không cần bảng state |
| Q6 | Có làm mắt xích refine không? | **CHỐT: có, bám `ContentReviewer` sẵn có** — không agent mới, 1 vòng duy nhất, chấm lại bằng reviewer cũ (§4.7, P6) |
| Q5 | Giới hạn ký tự từng nền tảng | **Mặc định do plan chốt** (không có số nào trong repo lẫn DB): Instagram caption 2.200 + ≤30 hashtag; Facebook theo template 150-300 chữ, hard cap 63.206; Zalo theo giới hạn OA. Đặt trong config để khách sửa, không hardcode |
| Q4 | Ngân sách token mỗi bài | **Tự chốt:** ~1.5-2x hiện tại (§6), có cap thời gian 60s/chuỗi + 15s/step, vượt thì fallback. Chỉ xem lại nếu số đo thực vượt 2x |
| Q3 | Routing theo loại bài | **Ngoài phạm vi phase này** — thiết kế đã chừa chỗ mở ở L1 (`postType`) |

### Việc độc lập, nên làm trước

`agents.config_json.systemPrompt` của `content-agent` đang **chết**: `ContentAgent` gọi `CompleteAsync(string.Empty, …)` tại [ContentAgent.cs:61](../../../src/agents/Clawbot.Agents.Core/Content/ContentAgent.cs#L61), nên prompt khách sửa trong UI/DB không có tác dụng gì. Đây là bug im lặng, không phụ thuộc plan này, và sửa xong thì chuỗi mới có chỗ đúng để cắm persona từng step vào. Làm trước P1.

## 13. Tài liệu liên quan

- [Chapter 1: Prompt Chaining](../../Chapter%201_%20Prompt%20Chaining.md) — tài liệu khách gửi, cơ sở của plan này.
- [2026-07-22-content-platform-focus-zalo-fb-ig.md](2026-07-22-content-platform-focus-zalo-fb-ig.md) — chốt 3 nền tảng writable, ràng buộc Instagram cần media.
- [2026-06-07-feature-content-research-pipeline.md](../../ai/requirements/2026-06-07-feature-content-research-pipeline.md) — yêu cầu gốc của pipeline content, luật "prompt externalized, không hardcode".
