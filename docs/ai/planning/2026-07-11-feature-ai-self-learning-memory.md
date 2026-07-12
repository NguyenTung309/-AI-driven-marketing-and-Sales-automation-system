---
phase: planning
title: Project Planning & Task Breakdown
description: Break down work into actionable tasks and estimate timeline
---

# Project Planning & Task Breakdown — `ai-self-learning-memory`

## Milestones
**What are the major checkpoints?**

- [x] M1 — Vòng học kín chạy thật (code xong 2026-07-11, cần chạy lại run-all.bat để nạp migration 0056 + job; xác nhận thật sau 1 đêm chạy): đêm chạy job chưng cất, sáng thấy đề xuất chờ duyệt kèm accuracy trước/sau, duyệt xong AI trả lời được câu hôm qua trượt.
- [x] M2 — AI nhớ khách (code xong 2026-07-11; job tests chờ Infrastructure.Tests hết vỡ compile bởi refactor Meta song song): hội thoại khách cũ có facts inject vào prompt, sale thấy "Ghi nhớ về khách" ở panel phải.
- [x] M3 — Tự bảo trì (code xong 2026-07-12; còn 3.3 đo vận hành sau 2 tuần deploy): reviewer-agent có memory lỗi hay gặp; KB được đề xuất nén/gộp hằng tuần.

## Task Breakdown
**What specific work needs to be done?**

### Phase 1: Chưng cất tri thức hằng đêm (M1)
- [x] 1.1 Domain `KbSuggestion`: entity + invariant (Approve/Reject một chiều, không sửa sau decided; dedup_hash bắt buộc) + unit tests (Domain.Tests). DONE 2026-07-11: KbSuggestion.cs + IsAutoApprovable rail + 13 tests (Domain 97/97).
- [x] 1.2 Migration `0056_kb_suggestions.sql` (1 SqlCommand, không GO): bảng kb_suggestions + cột `tenants.require_kb_human_review` bit default 0 + EF config (snake_case, unique index tenant_id+dedup_hash) + dòng docker exec RIÊNG trong repair block run-all.bat. DONE 2026-07-11: + Tenant.RequireKbHumanReview + DbSet; Infrastructure compile sạch.
- [x] 1.3 Agents.Core `KnowledgeDistiller`: prompt distill + consolidate (memory-ops), tiếng Việt 100%, self-repair ≤3, tests. DONE 2026-07-11: Learning/KnowledgeDistiller.cs (DistillAsync/ConsolidateAsync/ComputeDedupHash SHA256) + 7 tests.
- [x] 1.3b `ContentReviewer.ReviewKbSuggestionAsync`: rubric KB riêng, skeleton fail-closed. DONE 2026-07-11: + 3 tests (approve/llm-error→needs_human/empty→reject).
- [x] 1.4 Infrastructure `KnowledgeDistillationJob`. DONE 2026-07-11: mine 3 nguồn (hội thoại escalated; cặp khách-hỏi→sale-đáp out/user; câu lặp ≥3/7d group theo dedup hash in-memory), per-item try/catch, cap qua LearningOptions, DisableConcurrentExecution(900s), 2 notification kb_suggestion_auto_approved/kb_suggestion_pending; 6 tests (Infrastructure 250/250). LƯU Ý: catalog module load 2 query phẳng (SQLite test không hỗ trợ APPLY).
- [x] 1.5 Accuracy trước/sau. DONE 2026-07-11: `KbSuggestionAccuracyEvaluator` (Agents.Core/Learning) — "trước" = context RAG, "sau" = context RAG + contentMd nối thêm, judge fail-closed, cap 20 case, (null,null) khi không có case.
- [x] 1.5b Nhánh auto-approve trong job. DONE 2026-07-11: rail ở entity (KbSuggestion.IsAutoApprovable) + flag tenant chặn ở job; materialize qua `KbSuggestionMaterializer` (Infrastructure/Learning, dùng chung cho endpoint approve 1.6 — archive bản deployed cũ + Deploy + EmbedAndUpsertAsync); tests đủ 5 nhánh rail + dedup 2 lượt chạy.
- [x] 1.6 API `KbSuggestionEndpoints`. DONE 2026-07-11: GET list (kb:read, kèm tên module đích) / POST approve (kb:write, nhận contentMd sửa tay, materialize qua KbSuggestionMaterializer, deploy fail trả 502 nhưng approve đã ghi) / POST reject (kb:write, bắt buộc reason); toggle RequireKbHumanReview vào AdminEndpoints (nullable — client cũ giữ nguyên). LỆCH KẾ HOẠCH: repo không có WebApplicationFactory harness — bỏ test HTTP 403; logic phủ ở Domain (13 tests) + job (6 tests), RequirePermission dùng đúng pattern các endpoint KB sẵn có.
- [x] 1.7 Đăng ký Hangfire recurring. DONE 2026-07-11: HangfireModule — job `kb-knowledge-distillation` Cron.Daily(2) giờ VN; DI bundle Learning (Configure LearningOptions + TryAddScoped ContentReviewer vì API host không gọi AddClawbotContent) trong AddClawbotJobs.
- [x] 1.8 FE. DONE 2026-07-11: `KbSuggestionsPanel` trong màn Kho tri thức (panel tự ẩn khi trống thay vì tab — màn KB không có tab sẵn): card đề xuất với evidence + verdict reviewer + accuracy trước/sau ("Chưa có bộ test" khi NULL) + sửa content trước duyệt + Duyệt/Loại (loại bắt buộc lý do qua modal) + "Lịch sử đã quyết" (soi lại auto-approved, badge "AI tự duyệt"); toggle "AI tự duyệt tri thức" (icon school) trên /agents cạnh 2 toggle review-gate; kb.ts client; tsc sạch.

### Phase 2: Memory theo khách (M2)
- [x] 2.1 Domain `ContactMemory`. DONE 2026-07-11: entity + Supersede(supersededById?, at) một chiều (null = delete) + 4 tests (Domain 103/103).
- [x] 2.2 Migration `0057_contact_memories.sql`. DONE 2026-07-11: bảng + index (tenant, contact, is_active) + cột `conversations.memory_extracted_at` + Conversation.MarkMemoryExtracted + EF config + DbSet + dòng docker exec riêng repair block.
- [x] 2.3 Agents.Core `ContactFactExtractor`. DONE 2026-07-11: memory-ops add/update/delete/noop, factId bịa → cả batch coi hỏng để self-repair, category lạ rơi về profile, confidence thiếu = 0.7; tách helper `LlmJsonRepair` dùng chung với Distiller; 5 tests (Agents 320/320).
- [x] 2.4 Infrastructure `ContactMemoryExtractionJob`. DONE 2026-07-11: scan */30ph, idle ≥15ph + tin mới sau watermark + có contact; transcript 30 tin gần nhất (khách/sale/AI); extractor null → throw giữ watermark để lượt sau quét lại; cap `Learning:MaxConversationsPerScan` (20); 4 tests VIẾT XONG nhưng CHƯA CHẠY ĐƯỢC — Infrastructure.Tests đang vỡ compile bởi refactor Meta per-tenant song song (AdsConnectorTests/MetaGraphClientTests/GraphSocialPublisherTests... đổi IMetaGraphClient signature, KHÔNG phải code feature này); src compile sạch.
- [x] 2.5 Inject ChatAgent. DONE 2026-07-11: `ChatAgentRequest.ContactFacts` + khối "Ghi nho ve khach hang nay" trong BuildSystemPrompt (dặn model dùng tự nhiên, khách vừa nói khác thì theo khách); ChatAgentGrpcService.LoadContactFactsAsync top-10 active confidence ≥0.6 mới nhất trước, lỗi query → bỏ qua không fail reply (AgentService 80/80).
- [x] 2.6 API + FE. DONE 2026-07-11: ContactsEndpoints GET memories / DELETE all (xóa cứng — quyền được quên) / DELETE 1 fact (hạ cờ giữ vết); FE `ContactMemoryPanel` ở panel phải hội thoại (nhãn category tiếng Việt, nút gỡ từng fact) + contactMemories.ts; tsc sạch.

### Phase 3: Memory theo agent + nén KB (M3)
- [x] 3.1 `agent_memories` (migration 0058). DONE 2026-07-12: Domain AgentMemory (supersede như ContactMemory) + EF config + repair block; `AgentMistakeExtractor` (memory-ops, category ép "mistake"); `AgentMemoryDistillationJob` 01:30 VN mine `content_items.rejected_reason` 24h (cap 50/tenant) → bài học cho reviewer-agent; inject qua `IAgentMemoryProvider` (Agents.Core) + `EfAgentMemoryProvider` (Infrastructure DI cả 2 host) → `ContentReviewer.ComposePersonaAsync` nạp top-10 vào persona cả 2 đường chấm, provider lỗi không chặn review. Tests: Domain 5 + extractor 3 + job 2 + reviewer-injection 2.
- [x] 3.2 `KbCompressionJob` weekly. DONE 2026-07-12: Chủ nhật 03:00 VN; `ProposeMergesAsync` (cặp trùng từ catalog excerpt, id bịa/tự-gộp bị bắt sửa) → `MergeModulesAsync` (gộp FULL content, mâu thuẫn giữ cả 2 + [CẦN NGƯỜI KIỂM]) → suggestion `op=merge` dedup theo cặp code; **merge LUÔN chờ người** (không đo accuracy → rail đóng, không auto — gộp nhóm là thay đổi lớn, duyệt xong người tự lưu trữ nhóm nguồn, ghi rõ trong rationale + notification). Tests 2 (pending-never-auto + dedup cặp).
- [ ] 3.3 Đo sau 2 tuần chạy (việc VẬN HÀNH, không phải code — bắt đầu tính từ ngày deploy): accuracy trend + số đề xuất được duyệt/loại — quyết định chỉnh ngưỡng (câu lặp ≥3, idle 15 phút, cap 50).

### Fix từ /check-implementation (2026-07-12)
- [x] CRITICAL: accuracy rail tautology — KbSuggestionAccuracyEvaluator đổi `contextAfter = proposedContentMd` (replace, bỏ append) nên accuracy_after phản ánh đúng trạng thái post-deploy; rail lấy lại răng. +3 test evaluator (proposed kém → after < before → chặn được).
- [x] LOW: KbCompressionJob redact cả title + rationale (đồng bộ KnowledgeDistillationJob).
- [x] MEDIUM: +3 test mining nguồn 2 (sale_answered) & 3 (repeated_question) + case dưới ngưỡng không mine.

## Dependencies
**What needs to happen in what order?**

- 1.1 → 1.2 → 1.4; 1.3 song song với 1.1/1.2; 1.5–1.8 sau 1.4.
- Phase 2 độc lập Phase 1 (có thể làm song song), nhưng 2.3 nên sau 1.3 để tái dùng khung memory-ops prompt + parser.
- Phase 3 phụ thuộc pipeline suggestion của Phase 1 (3.2) và review-gate reviewer sẵn có (3.1).
- Ngoài hệ: LLM gateway (aigatewayport.com) phải hoạt động — đã có self-repair + fallback config; KbTestCase cần được nhập để accuracy có nghĩa (việc của vận hành, không chặn dev).

## Timeline & Estimates
**When will things be done?**

- Phase 1: ~4 ngày công (1.4 là task nặng nhất — mining queries + orchestrate; 1.3 ~1 ngày; API+FE ~1 ngày).
- Phase 2: ~2.5 ngày công (extractor + job ~1.5, inject + FE ~1).
- Phase 3: ~2 ngày công.
- Buffer: +30% cho độ chập chờn gateway và chỉnh prompt tiếng Việt (bài học từ plan-suggestions: mất thêm thời gian ép ngôn ngữ + parse).
- Mốc đề xuất: M1 trong tuần 2026-07-13; M2 cuối tuần đó; M3 tuần kế tiếp.

## Risks & Mitigation
**What could go wrong?**

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Gateway LLM trả rác/rỗng từng đợt | Job đêm ra ít/không đề xuất | Self-repair ≤3 + per-item skip + log; chạy lại đêm sau tự bù (dedup_hash chống trùng) |
| Chất lượng distill kém (đề xuất sai) | Người duyệt mất niềm tin | Evidence kèm theo + reviewer-agent chấm + cho sửa tay trước duyệt; tenant tắt auto về human khi chưa tin |
| AI tự duyệt tri thức sai vào KB | AI nói sai cho mọi khách | Rail kép (verdict approve + accuracy không giảm, thiếu đo → chờ người; op=add luôn qua người) + notification mỗi lần tự duyệt + rollback qua KB version history + toggle tắt auto |
| PII redactor sót pattern VN (SĐT 09xx, địa chỉ) | Lộ PII trong KB/facts | Rà rule redactor với mẫu VN trước khi bật job; test case redact riêng; evidence chỉ giữ trích đoạn ngắn |
| Câu hỏi cùng nghĩa khác chữ → dedup_hash không bắt | Vài suggestion trùng ý | Chấp nhận v1 — consolidate memory-ops sẽ NOOP khi trùng KB đã duyệt; người loại tay phần còn lại |
| KbTestCase trống → accuracy NULL | Không đo được tiến bộ | UI nói thẳng "chưa có bộ test"; đề xuất vận hành nhập test case từ chính các câu hỏi thật |
| Chi phí LLM tăng | Vượt ngân sách | Cap 50/20 + cost ledger + cap chi tiêu hiện có; chạy 1 lần/đêm |
| Facts sai làm AI trả lời sai cho khách đó | Trải nghiệm xấu 1 khách | Confidence threshold khi inject (≥0.6); sale gỡ được từng fact trên UI; supersede giữ vết |

## Resources Needed
**What do we need to succeed?**

- 1 dev .NET full-stack (backend + FE React) — codebase đã có đủ pattern mẫu cho mọi task (jobs, prompts, self-repair, endpoints, RBAC, FE tabs).
- LLM gateway hiện tại + LLM config đã bind (fallback sẵn có).
- Không hạ tầng mới: SQL Server + Hangfire + notification + KB versioning + KbTestRunner tái dùng nguyên.
- Tài liệu: requirements + design cùng feature này; plan review-gate `docs/superpowers/plans/2026-07-10-mandatory-review-gate-plan.md` (reviewer rubric, fail-closed); memory pattern tham khảo mem0 (đã chốt chỉ mượn pattern).
