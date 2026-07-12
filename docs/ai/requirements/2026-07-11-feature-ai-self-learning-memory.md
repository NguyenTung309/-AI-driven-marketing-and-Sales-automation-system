---
phase: requirements
title: Requirements & Problem Understanding
description: Clarify the problem space, gather requirements, and define success criteria
---

# Requirements & Problem Understanding — `ai-self-learning-memory`

Hệ thống AI tự học & bộ nhớ cho ClawBot: chưng cất tri thức hằng ngày từ hội thoại thật (knowledge distillation) + trí nhớ theo từng khách (per-contact memory) + trí nhớ nghiệp vụ của agent (per-agent memory).

## Problem Statement
**What problem are we solving?**

- **AI không tự khá lên**: chat agent trả lời dựa trên KB tĩnh do người nhập tay. Khi AI trả lời kém (cờ `Escalate`, KB score < 0.35, reply bị reviewer reject), tri thức thiếu đó KHÔNG được ghi nhận — hôm sau khách hỏi đúng câu đó, AI vẫn trượt.
- **Câu trả lời vàng của sale bị lãng phí**: sale trả lời tay những câu AI bó tay — mẫu chuẩn nhất — nhưng không ai đưa ngược vào KB.
- **AI không nhớ khách**: mỗi hội thoại là tờ giấy trắng. Khách cũ quay lại ("em học HSK3 rồi, ca tối 2-4-6") phải khai lại từ đầu — trải nghiệm kém, sale nhìn vào cũng không có tóm tắt.
- **KB phình + mâu thuẫn theo thời gian**: nhập tay tích lũy bản trùng/na ná, thậm chí 2 giá khác nhau cho cùng khóa học, không ai rà.
- **Ai bị ảnh hưởng**: khách (câu trả lời sai/kém), sale (lặp lại việc đã làm), quản lý (không biết KB hổng chỗ nào), chính AI agents (chất lượng dậm chân).
- **Workaround hiện tại**: admin tự nhớ và nhập KB tay qua màn Kho tri thức; không có vòng phản hồi nào từ vận hành về KB.

## Goals & Objectives
**What do we want to achieve?**

Primary goals:
1. **Vòng học kín hằng ngày**: AI trượt ở đâu hôm nay → đêm nay chưng cất thành đề xuất tri thức → qua gate (tự duyệt đạt rail, hoặc người duyệt) → ngày mai trả lời được. Job ngầm (Hangfire), không cần ai bấm.
2. **Gate 2 chế độ, mặc định AI tự duyệt có rail** (chốt lại 2026-07-11, thay QĐ "luôn chờ người" ban đầu): reviewer-agent chấm rubric + đo accuracy trước/sau cho MỌI đề xuất. Tenant flag `require_kb_human_review` (mặc định OFF = auto): khi auto, đề xuất chỉ được tự deploy nếu **reviewer verdict = approve VÀ accuracy sau ≥ trước (cả 2 accuracy phải đo được)** — không đạt hoặc thiếu bộ test → rơi về chờ người (fail-closed). Bật flag → mọi đề xuất chờ người. Deploy luôn qua KB versioning sẵn có (rollback được).
3. **Memory per-contact lớp mỏng nội bộ** (quyết định đã chốt — KHÔNG dùng mem0 service): LLM extract facts về khách sau hội thoại, lưu `contact_memories`, inject top-k vào prompt ChatAgent cùng chỗ RAG.
4. **Chống phình + mâu thuẫn**: theo pattern memory-ops của mem0 — mỗi fact/entry mới được LLM đối chiếu kho hiện có rồi quyết `ADD / UPDATE / DELETE / NOOP`, không append mù. Nén/gộp KB chạy weekly.
5. **Đo được tiến bộ**: chạy `KbAccuracyTestJob` trên bản đề xuất, hiện % accuracy trước/sau ngay cạnh nút duyệt.

Secondary goals:
- **Memory per-agent**: reviewer-agent tích lũy "lỗi content hay gặp" để chấm ngày càng chuẩn (nạp vào persona lúc review).
- Notification "có N tri thức mới chờ duyệt" qua bell sẵn có.

Non-goals (out of scope):
- KHÔNG tự deploy khi thiếu lưới đo (chưa có KbTestCase → accuracy NULL) hoặc không đạt rail — auto-approve không bao giờ "mù".
- KHÔNG dùng mem0/dịch vụ memory ngoài (giữ stack .NET + Qdrant; mem0 chỉ mượn pattern thiết kế).
- KHÔNG graph memory (Neo4j) — YAGNI ở quy mô hiện tại.
- KHÔNG fine-tune model — "học" ở đây là học qua tri thức truy hồi (RAG + memory), không đổi trọng số.
- KHÔNG lưu PII thô trong memory/đề xuất KB (tuân thủ quy tắc pii-redact-derived-content sẵn có).

## User Stories & Use Cases
**How will users interact with the solution?**

- Là **quản lý trung tâm**, tôi muốn mỗi sáng thấy danh sách "tri thức mới chờ duyệt" (kèm nguồn gốc: câu khách hỏi + AI đã trượt thế nào + sale đã trả lời gì) để duyệt trong vài phút, và thấy accuracy trước/sau để biết duyệt có đáng không.
- Là **sale**, tôi muốn câu trả lời tay của tôi cho câu khó tự động được đề xuất vào KB, để lần sau AI tự trả lời thay tôi.
- Là **khách quay lại**, tôi muốn AI nhớ tôi học trình độ nào, thích ca nào, đã hẹn gì — không phải khai lại.
- Là **sale mở hội thoại khách cũ**, tôi muốn thấy tóm tắt facts về khách (trình độ, lịch, trạng thái chuyển khoản) ngay trong panel phải.
- Là **reviewer-agent**, tôi tích lũy các lỗi content lặp lại của tenant để lần chấm sau bắt nhanh hơn.
- Edge cases: khách nói đùa/sai rồi sửa (fact UPDATE); 2 sale trả lời khác nhau cùng câu hỏi (mâu thuẫn → đưa cả 2 cho người chọn); hội thoại nhóm (facts thuộc về nhóm, không gán cho 1 người); tin nhắn chứa SĐT/địa chỉ (redact trước khi thành memory); khách yêu cầu xóa dữ liệu (xóa contact_memories theo contact).

## Success Criteria
**How will we know when we're done?**

- Job chưng cất chạy hằng đêm, tạo đề xuất KB từ ≥3 nguồn: AI-trượt, sale-trả-lời-tay, câu-hỏi-lặp. Đề xuất trùng KB hiện có bị NOOP (đo bằng test dedup).
- Không tri thức chung nào vào KB active mà chưa qua gate: người duyệt, HOẶC auto-rail đạt chuẩn (verdict approve + accuracy không giảm) khi tenant để chế độ auto (test cả 2 nhánh + nhánh thiếu-bộ-test rơi về người).
- Đề xuất auto-approved ghi `approval_mode = auto`, có notification cho người biết, và rollback được qua KB version history.
- Accuracy trước/sau hiển thị cạnh nút duyệt; sau khi duyệt batch đầu tiên, KbAccuracyTest tăng hoặc giữ nguyên (không giảm).
- ChatAgent inject được top-k facts per-contact vào prompt; hội thoại khách cũ có ít nhất 1 fact được dùng (đo qua trace).
- Facts mâu thuẫn được UPDATE thay vì tồn tại song song (test memory-ops).
- Toàn bộ text derived (facts, đề xuất KB) đã qua PII redactor trước khi persist.
- Suites test hiện có vẫn xanh; job có `DisableConcurrentExecution` + idempotent (chạy lại không nhân đôi đề xuất).

## Constraints & Assumptions
**What limitations do we need to work within?**

Technical constraints:
- Stack .NET 8 + EF Core + SQL Server + Qdrant + Hangfire (API host) — không thêm service runtime mới.
- Migrations: 1 SqlCommand/file, không GO; cột mới phải vào cả repair block run-all.bat (dòng riêng — trần 8191 ký tự cmd.exe).
- LLM qua binding per-agent (LlmConfigResolver, có fallback active config); gateway hiện tại streaming-only, chập chờn → mọi bước LLM phải retry/self-repair + fail-safe (đề xuất ít hơn thay vì fail cả job).
- Qdrant collection đặt tên theo model+dim (quy ước sẵn — đổi embedder = collection mới).
- Consumer bus chạy 2 host — job enqueue-per-message không dùng được; dùng recurring scan (pattern CommentAutoReplyJob.RunScanAsync).
- PII: facts/đề xuất lấy từ tin khách = derived text → bắt buộc qua `IPiiRedactor` trước khi lưu.

Business constraints:
- Tri thức chung sai = AI nói sai cho mọi khách → human gate bắt buộc, không nhượng.
- Chi phí LLM: distill chạy đêm 1 lần/ngày/tenant, giới hạn số hội thoại xử lý mỗi lượt (cap), đi qua cost ledger sẵn có.

Assumptions:
- KB versioning + kb.deploy + KbAccuracyTestJob + KbTestCase hoạt động đúng như hiện tại.
- Tenant đã bind LLM config (resolver fallback đã có).
- Khối lượng hội thoại/ngày ở mức trăm, không nghìn — batch đêm xử lý đủ trong vài phút.

## Questions & Open Items
**What do we still need to clarify?**

Tất cả đã chốt (review 2026-07-11):

- (CHỐT) Gate 2 chế độ: mặc định AI tự duyệt với rail (reviewer verdict approve + accuracy không giảm, thiếu đo → chờ người); tenant tắt được về duyệt thủ công (`require_kb_human_review`).
- (CHỐT) Per-contact memory: lớp mỏng nội bộ, không dùng mem0.
- (CHỐT) Ngưỡng "câu hỏi lặp nhiều": ≥3 lần/7 ngày, để config chỉnh sau.
- (CHỐT) Retention per-contact: không tự xóa — hạ trọng số theo recency; xóa khi khách yêu cầu hoặc sale gỡ tay.
- (CHỐT) Màn duyệt: tab "Chờ duyệt" trong màn Kho tri thức hiện có.
- (CHỐT) Per-agent memory: Phase 3, sau khi 2 lớp chính chạy.
