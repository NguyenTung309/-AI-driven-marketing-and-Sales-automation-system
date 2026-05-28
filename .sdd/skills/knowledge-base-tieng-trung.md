---
skill: knowledge-base-tieng-trung
agents: [Agent-Chat, Agent-SaleAssist]
owner: P3 Sales Lead + P4 QA
updated: 2026-05-27
---

# Skill — Knowledge Base tiếng Trung

## Purpose
Mọi câu trả lời của Agent-Chat đều phải dựa trên KB này. KB là IP công ty.

## 6 Modules (1-1 với `kb_modules`)

1. **KB-01 — Giáo trình** (HSK 1–6, TOCFL, YCT, giáo trình riêng)
2. **KB-02 — Lộ trình học** (6 mục tiêu: du lịch, công việc, HSK3, HSK5, học vui, trẻ em)
3. **KB-03 — Bảng giá** (gói 1/3/6/12 tháng, học viên cũ, combo, ưu đãi, giá học thử)
4. **KB-04 — 100+ FAQ** (rút từ log chat thực tế 3 tháng)
5. **KB-05 — Giáo viên** (profile, phong cách, lịch dạy)
6. **KB-06 — Đặc thù nền tảng** (Zalo/TikTok/IG/FB/YT tone voice)

## Retrieval contract
- Query: free text VI/中文/EN.
- Index: pgvector cosine on `kb_versions.embedding`.
- Top-K = 5. Min similarity 0.75.

## Required guarantees
- KHÔNG đưa giá ngay khi khách hỏi — hỏi mục tiêu trước (xem `KB-005` trong `chat_scenarios`).
- KHÔNG đề cập hoặc so sánh tiêu cực với đối thủ tên cụ thể.
- KHÔNG bịa thông tin GV / chứng chỉ / cam kết đậu HSK.

## Update cadence
- Bảng giá: khi thay đổi.
- Lộ trình + GV: theo quý.
- FAQ: hàng tháng từ log chat.
