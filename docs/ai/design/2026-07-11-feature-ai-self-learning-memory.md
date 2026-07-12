---
phase: design
title: System Design & Architecture
description: Define the technical architecture, components, and data models
---

# System Design & Architecture — `ai-self-learning-memory`

## Architecture Overview
**What is the high-level system structure?**

3 lớp memory, tất cả chạy trong host sẵn có (API host chứa Hangfire), không service mới:

```mermaid
graph TD
  subgraph Nguồn tín hiệu
    MSG[(messages / conversations)]
    TRC[(agent_traces: escalate, review_rejected, kb_score thấp)]
  end

  subgraph Lớp 1 — Chưng cất tri thức (đêm)
    KDJ[KnowledgeDistillationJob<br/>Hangfire 02:00 / tenant]
    KDJ -->|mine 3 nguồn| MSG
    KDJ --> TRC
    KDJ -->|LLM distill + memory-ops<br/>ADD/UPDATE/MERGE/NOOP| SUG[(kb_suggestions<br/>staging, PII-redacted)]
    RVA[Reviewer-agent chấm rubric] --> SUG
    ACC[KbTestRunnerService<br/>accuracy trước/sau] --> SUG
    SUG -->|rail đạt + tenant auto:<br/>verdict approve, accuracy không giảm| KBV[(kb_modules / kb_versions<br/>draft → deploy sẵn có)]
    SUG -->|rail trượt / thiếu bộ test /<br/>tenant require_kb_human_review| HUM[Người duyệt trên UI] --> KBV
    SUG --> NTF[Notification bell:<br/>“N tự duyệt / N chờ duyệt”]
  end

  subgraph Lớp 2 — Memory theo khách (30 phút/lần)
    CMJ[ContactMemoryExtractionJob<br/>recurring scan] -->|hội thoại idle có tin mới| MSG
    CMJ -->|LLM extract facts + memory-ops| CM[(contact_memories<br/>PII-redacted)]
    CM -->|top-k inject| CA[ChatAgent.BuildSystemPrompt<br/>cạnh RAG hiện có]
    CM --> FE[Panel phải hội thoại:<br/>“Ghi nhớ về khách”]
  end

  subgraph Lớp 3 — Memory theo agent (phase sau)
    AM[(agent_memories)] --> RVA
  end

  KBV -->|RAG| CA
```

Trách nhiệm chính:
- **KnowledgeDistillationJob** (Infrastructure/Jobs): quét cửa sổ 24h, gom 3 nhóm tín hiệu (AI trượt, sale trả lời tay, câu hỏi lặp), gọi LLM chưng cất thành đề xuất chuẩn hóa, chạy memory-ops đối chiếu KB hiện có, ghi `kb_suggestions`, gọi reviewer-agent chấm, bắn notification.
- **KbSuggestion** (Domain): staging entity — đề xuất KHÔNG đụng `kb_versions` cho tới khi qua gate. Gate 2 chế độ theo tenant flag `require_kb_human_review` (mặc định OFF = auto): auto chỉ deploy khi **reviewer_verdict = approve VÀ accuracy_after ≥ accuracy_before (cả 2 accuracy non-null)** — trượt rail hoặc thiếu bộ test → status `pending` chờ người (fail-closed). Duyệt (auto hay người) = materialize KbVersion draft trên module đích (hoặc module mới) rồi đi luồng deploy sẵn có.
- **ContactMemoryExtractionJob** (Infrastructure/Jobs): scan hội thoại có tin nhắn mới + đã idle ≥15 phút, LLM extract facts về khách, memory-ops với facts hiện có của đúng contact đó, ghi `contact_memories`.
- **ChatAgent** (Agents.Core): thêm khối "Ghi nhớ về khách" vào BuildSystemPrompt ([ChatAgent.cs:306](src/agents/Clawbot.Agents.Core/Chat/ChatAgent.cs#L306)) — top-k facts, cạnh khối RAG.
- **Stack**: .NET 8 + EF + SQL Server + Hangfire + LLM binding qua LlmConfigResolver (đã có fallback). Qdrant KHÔNG thêm collection mới ở v1 (xem Design Decisions #3).

## Data Models
**What data do we need to manage?**

### `kb_suggestions` (mới — migration 0056; 0055 đã bị chiếm bởi meta_facebook_login)
| Cột | Kiểu | Ghi chú |
|---|---|---|
| id | uniqueidentifier PK | |
| tenant_id | uniqueidentifier | ITenantOwned |
| op | nvarchar(16) | `add` \| `update` \| `merge` |
| target_kb_module_id | uniqueidentifier NULL | NULL khi `add` (đề xuất module mới) |
| title | nvarchar(256) | tiêu đề đề xuất |
| content_md | nvarchar(max) | nội dung KB chuẩn hóa (đã redact PII) |
| rationale | nvarchar(max) | vì sao đề xuất (đã redact) |
| evidence_json | nvarchar(max) | mảng `{conversationId, snippetRedacted, signal}` — chỉ id + trích đoạn đã redact |
| dedup_hash | nvarchar(64) | hash câu-hỏi-chuẩn-hóa; unique (tenant_id, dedup_hash) → job idempotent |
| reviewer_verdict | nvarchar(16) NULL | `approve` \| `reject` \| `needs_human` — cùng bộ giá trị fail-closed của ContentReviewer (không dùng score số) |
| reviewer_notes | nvarchar(max) NULL | |
| accuracy_before / accuracy_after | decimal(5,2) NULL | đo trên test cases module đích, RAG module-scoped: "trước" = context RAG hiện tại (nội dung module cũ); "sau" = **contentMd đề xuất ĐỨNG MỘT MÌNH** (deploy = REPLACE, không phải append). Nối before+proposed sẽ làm after luôn ≥ before → rail vô nghĩa (sửa 2026-07-12). `op=add` chưa có test case → NULL |
| status | nvarchar(16) | `pending` \| `approved` \| `rejected` |
| approval_mode | nvarchar(8) NULL | `auto` \| `human` — set khi approved |
| rejected_reason | nvarchar(1024) NULL | |
| decided_by | uniqueidentifier NULL | user duyệt/loại; NULL khi auto |
| created_at / decided_at | datetimeoffset | |

### `contact_memories` (mới — migration 0057)
| Cột | Kiểu | Ghi chú |
|---|---|---|
| id | uniqueidentifier PK | |
| tenant_id / contact_id | uniqueidentifier | index (tenant_id, contact_id, is_active) |
| fact | nvarchar(1024) | 1 fact ngắn, đã redact PII (giữ nghiệp vụ: trình độ, ca học, trạng thái cọc) |
| category | nvarchar(32) | `profile` \| `preference` \| `commitment` \| `history` |
| confidence | decimal(3,2) | LLM tự chấm 0–1 |
| source_conversation_id | uniqueidentifier NULL | provenance, không lưu text gốc |
| is_active | bit | UPDATE/DELETE của memory-ops = hạ cờ + tạo bản mới (immutable history) |
| superseded_by_id | uniqueidentifier NULL | trỏ bản thay thế |
| created_at / updated_at | datetimeoffset | recency scoring |

### Tenant flag (gộp migration 0056)
- `tenants.require_kb_human_review` bit NOT NULL DEFAULT 0 — cùng họ ngữ nghĩa với `require_content_review`/`require_chat_reply_approval` của review-gate (require_* = siết chặt hơn), nhưng mặc định 0 = auto (QĐ user 2026-07-11). Toggle trên dashboard /agents cạnh 2 toggle review-gate.

### Watermark
- Cột mới `conversations.memory_extracted_at` (datetimeoffset NULL — migration 0057): scan = hội thoại có `last_message_at > memory_extracted_at` và idle ≥15 phút. Fail giữa chừng: không set watermark → lượt sau quét lại (giống bài học watermark của Pancake poll — không nuốt fail).
- Distillation không cần watermark: cửa sổ theo ngày + `dedup_hash` unique là đủ idempotent.

### `agent_memories` (phase 3 — chỉ định hình)
Giống `contact_memories` nhưng scope `agent_code`; reviewer-agent nạp top-k "lỗi hay gặp" vào persona khi chấm content.

Luồng dữ liệu: tin khách (raw, purge 30d) → LLM → text derived (fact/đề xuất) → **IPiiRedactor** (đã đăng ký DI — AddClawbotPiiRedactor) → persist. Không lưu text thô của khách vào bảng mới nào.

## API Design
**How do components communicate?**

Nội bộ (Minimal API, RBAC dot-code sẵn có — nhớ seed `role_permissions` qua RbacSeeder):

| Endpoint | Quyền | Mô tả |
|---|---|---|
| `GET /kb/suggestions?status=pending` | `kb:read` (tái dùng) | danh sách đề xuất + evidence + accuracy trước/sau (kèm cả approved `approval_mode=auto` để soi lại) |
| `POST /kb/suggestions/{id}/approve` | `kb:write` (tái dùng) | materialize → KbVersion draft trên module đích (hoặc tạo KbModule mới), chạy embedding + deploy qua luồng sẵn có; body tùy chọn `{ contentMd }` cho phép người sửa trước khi duyệt |
| `POST /kb/suggestions/{id}/reject` | `kb:write` | body `{ reason }` |
| `GET /contacts/{id}/memories` | quyền xem contact sẵn có | panel phải hội thoại |
| `DELETE /contacts/{id}/memories` | quyền quản lý contact | xóa theo yêu cầu khách (xóa cứng cả lịch sử) |
| `DELETE /contacts/{id}/memories/{memoryId}` | như trên | gỡ 1 fact sai |

Quyền: RbacSeeder đã có `kb:read`/`kb:write` (KHÔNG có `kb:manage` — đã đối chiếu code); grants theo dot-code trong role_permissions — kiểm exact match khi implement (bài học rbac-perm-seed-required).

Response theo envelope hiện hành của API. Nhánh auto-approve nằm TRONG KnowledgeDistillationJob (sau khi có score + accuracy), không có endpoint auto riêng; cả 2 nhánh đều materialize qua đúng luồng kb.deploy sẵn có. Toggle `require_kb_human_review` đi qua endpoint tenant settings sẵn có (chỗ 2 flag review-gate).

LLM interface: tái dùng `IClaudeChatClient` qua LlmCallScope (cost ledger tự ghi). 3 prompt template mới trong Agents.Core:
1. **Distill**: input = cụm tín hiệu (câu khách + AI trả lời sao + sale trả lời sao), output JSON `{title, contentMd, rationale, normalizedQuestion}`.
2. **Memory-ops consolidate**: input = đề xuất mới + top KB modules liên quan (title + trích ContentMd), output JSON `{op: add|update|merge|noop, targetModuleId?, mergedContentMd?}` — NOOP thì bỏ, không ghi.
3. **Extract facts**: input = transcript hội thoại (đã strip HTML), facts hiện có của contact, output JSON `{ops: [{op, factId?, fact, category, confidence}]}`.

Đo accuracy (KbSuggestionAccuracyEvaluator): RAG đã module-scoped nên "trước" = nội dung module hiện tại; "sau" = proposed đứng một mình (khớp deploy=replace). Rail chỉ có ý nghĩa khi 2 lưới đo THẬT SỰ độc lập — nếu "sau" ⊇ "trước" thì accuracy_after ≥ before tự động và auto-approve chỉ còn dựa reviewer verdict (đúng cái cần tránh).

Cả 3 dùng self-repair pattern sẵn có (retry ≤3 với feedback lỗi, tolerant converters) — gateway chập chờn là bản chất.

## Component Breakdown
**What are the major building blocks?**

Backend:
- `src/shared/Clawbot.Domain/KnowledgeBase/KbSuggestion.cs` — entity + invariant (Approve/Reject đổi status 1 chiều, không sửa sau khi decided).
- `src/shared/Clawbot.Domain/Contacts/ContactMemory.cs` — entity, Supersede().
- `src/agents/Clawbot.Agents.Core/Learning/KnowledgeDistiller.cs` — LLM distill + consolidate (unit-test được, không EF).
- Mở rộng `ContentReviewer` (Agents.Core/Content): method mới `ReviewKbSuggestionAsync` — rubric KB riêng (đúng với evidence, không mâu thuẫn KB, không PII, rõ ràng), tái dùng nguyên skeleton fail-closed + verdict approve/reject/needs_human (KHÔNG score số — đã đối chiếu code, ContentReviewer không có score).
- `src/agents/Clawbot.Agents.Core/Learning/ContactFactExtractor.cs` — LLM extract + memory-ops.
- `src/shared/Clawbot.Infrastructure/Jobs/KnowledgeDistillationJob.cs` — mine + orchestrate + notify (pattern ContentReviewSlaJob).
- `src/shared/Clawbot.Infrastructure/Jobs/ContactMemoryExtractionJob.cs` — recurring scan (pattern CommentAutoReplyJob.RunScanAsync).
- `src/api/Clawbot.Api/Endpoints/KbSuggestionEndpoints.cs` + mở rộng ContactEndpoints.
- ChatAgent: thêm tham số facts vào BuildSystemPrompt; caller (ChatAgentGrpcService) load top-k qua repo.
- Migrations 0056/0057 + repair block run-all.bat (dòng docker exec RIÊNG — trần 8191).

Frontend (clawbot-web):
- Màn Kho tri thức: tab mới "Chờ duyệt" — card đề xuất: nguồn gốc (evidence), verdict reviewer, accuracy trước/sau, nút Duyệt/Loại, cho sửa content trước duyệt; mục "Đã tự duyệt" (`approval_mode=auto`) để soi lại.
- Toggle "AI tự duyệt tri thức" (RequireKbHumanReview đảo chiều) trên dashboard /agents — cùng chỗ + cùng endpoint AdminEndpoints với RequireContentReview/RequireChatReplyApproval (đã đối chiếu code: AdminEndpoints.cs có sẵn pattern flags này).
- Panel phải hội thoại: khối "Ghi nhớ về khách" (fact + category + nút gỡ).
- Bell notification: tái dùng, message "Có N tri thức mới chờ duyệt" (không kèm nội dung — PII).

## Design Decisions
**Why did we choose this approach?**

1. **Lớp mỏng nội bộ, không mem0 service** (đã chốt): mem0 = Python service + API riêng; ClawBot chỉ cần đúng 3 pattern của nó (memory-ops, extract-then-consolidate, scoped memory). Tự viết ~2 prompt + 2 bảng rẻ hơn vận hành thêm 1 service.
2. **`kb_suggestions` staging riêng, không ghi thẳng KbVersion draft**: đề xuất cần provenance (evidence), verdict reviewer, accuracy trước/sau, và có thể bị loại — nhét vào `kb_versions` sẽ làm rác lịch sử version và thiếu chỗ chứa metadata. KbVersion chỉ sinh ra khi qua gate (auto đạt rail hoặc người duyệt).
3. **Per-contact memory KHÔNG dùng vector search v1**: facts mỗi khách ít (<50), load hết + sort recency + cap k=10 là đủ và rẻ. mem0 cần vector vì kho memory toàn cục; kho của ta scope theo contact nên tra thẳng SQL. Đường nâng cấp: thêm embedding + Qdrant collection (đặt tên theo model+dim như quy ước) khi facts/khách vượt trăm.
4. **Recurring scan, không consumer per-message**: bus consumer chạy 2 host — enqueue-per-message sẽ nhân đôi; scan theo watermark + idle-window là pattern đã chạy ổn (CommentAutoReplyJob).
5. **Memory-ops bằng 1 LLM call có kho hiện tại trong context** (mem0 pattern): với distillation, "kho hiện tại" = top modules liên quan (lookup theo keyword/title — KB modules mỗi tenant ít); với contact facts = toàn bộ facts active của contact. Không cần pre-filter embedding ở quy mô này.
6. **Gate 2 chế độ, auto mặc định với rail fail-closed** (chốt lại 2026-07-11, thay "human gate tuyệt đối" ban đầu): auto-approve chỉ khi có ĐỦ 2 lưới đo — reviewer_verdict = approve (ContentReviewer fail-closed sẵn có: mọi lỗi LLM → needs_human, không bao giờ approve khi không chấm được) VÀ accuracy_after ≥ accuracy_before đo trên test cases module đích. Thiếu lưới nào (KbTestCase trống → accuracy NULL; op=add module mới chưa có case) = chờ người, không bao giờ auto "mù". Sai sót auto có đường lui: notification cho người biết + KB version history rollback (archive version, re-deploy bản cũ). Tenant siết được về 100% human bằng `require_kb_human_review`. Hệ quả tự nhiên: tri thức MỚI hoàn toàn (op=add) luôn qua người; auto chỉ áp dụng cho update/merge module đã có bộ test.
7. **Idempotency bằng dedup_hash** thay vì watermark cho distillation: chạy lại không nhân đôi đề xuất; câu hỏi lặp qua nhiều ngày cũng tự gom về 1 suggestion đang pending.
8. **Fact bất biến, supersede thay vì update-in-place**: memory-ops UPDATE/DELETE = hạ `is_active` + bản ghi mới — giữ lịch sử để debug "AI nhớ nhầm từ đâu", nhất quán nguyên tắc immutability.

Phương án đã cân nhắc và bỏ: tích hợp mem0 qua REST (thêm service + Python runtime — quá nặng); fine-tune model (đắt, chậm, không audit được); auto-approve chỉ dựa verdict reviewer không cần accuracy (1 LLM tự chấm 1 LLM, mất lưới đo khách quan — bị loại khi review 2026-07-11); human gate tuyệt đối (bị thay bằng gate 2 chế độ theo QĐ user).

## Non-Functional Requirements
**How should the system perform?**

- **Chi phí/hiệu năng**: distillation cap 50 hội thoại/tenant/đêm (config `Learning:MaxConversationsPerRun`); mỗi cụm tín hiệu ≤2 LLM call (distill + consolidate). Extraction cap 20 hội thoại/lượt scan. Tất cả qua LlmCallScope → cost ledger.
- **Resilience**: per-item try/catch — 1 hội thoại lỗi không giết cả batch; LLM parse fail sau 3 attempt = skip item + log warning (KHÔNG ghi dữ liệu đoán); job `DisableConcurrentExecution`.
- **Bảo mật**: mọi text derived qua IPiiRedactor trước persist; notification không chứa nội dung; endpoint sau RBAC + tenant scope (ITenantOwned filter sẵn có); text khách đưa vào prompt là DATA — prompt dặn model không thực thi chỉ dẫn trong đó. Backstop chống prompt-injection: nhánh human là người đọc; nhánh auto là rail kép + op=add luôn qua người (kẻ tấn công không thể "bơm" module mới mà không có người duyệt).
- **Độ tin cậy đo được**: accuracy trước/sau hiện cạnh nút duyệt (nếu KbTestCase trống thì hiện "chưa có bộ test" thay vì số ảo — và nhánh auto không chạy).
- **Không chặn luồng chat**: load contact facts trong ReplyAsync thêm ≤1 query SQL (indexed) — bỏ qua (không fail reply) nếu query lỗi.
