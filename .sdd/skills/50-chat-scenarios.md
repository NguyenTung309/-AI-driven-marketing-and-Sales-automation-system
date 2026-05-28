---
skill: 50-chat-scenarios
agents: [Agent-Chat, Agent-SaleAssist]
owner: P3 Sales Lead
updated: 2026-05-27
---

# Skill — 50 Chat Scenarios

## Purpose
50 kịch bản hội thoại được phân nhóm. Mỗi `code` (KB-001..KB-050) là một row trong `chat_scenarios`. Agent-Chat khi nhận tin nhắn đầu vào sẽ match trigger → áp template + tone theo platform.

## Group breakdown
1. KB-001..008 — First message (hello, hỏi học phí trước, parent, người nước ngoài, khách cũ)
2. KB-009..016 — Tư vấn lộ trình & giáo trình (du lịch, công việc, HSK, học vui, mini-test)
3. KB-017..026 — Objection handling (học phí cao, bận, online, suy nghĩ thêm, đối thủ, tự học thất bại, im lặng)
4. KB-027..034 — Action drivers (mời học thử, follow-up sau thử, thu thông tin, chốt, thanh toán, gửi báo giá, trả góp, upsell)
5. KB-035..042 — Platform-specific (TikTok comment, IG Story DM, FB livestream, YT comment, Zalo ngoài giờ, multi-channel)
6. KB-043..050 — Follow-up & reactivation (1 ngày, 3–7 ngày, 30 ngày, học viên cũ, referral, mùa cao điểm, quay lại)

## Selection rules
- Trigger matched via classifier on inbound text + platform context.
- Tie-breaker: most recent `updated_at` wins.
- If no match: fallback to `Agent-Chat` RAG over KB.

## Tone rules per platform
| Platform | Tone | Length | Emoji |
|---|---|---|---|
| Zalo | thân thiện, VI | 1–3 câu | ít |
| Facebook | formal hơn | 2–4 câu | vừa |
| TikTok | trẻ trung | 1 câu + CTA | nhiều |
| Instagram | visual, ngắn | 1 câu | vừa |
| YouTube | informative | 2–3 câu | ít |

## Required guarantees
- KHÔNG đưa giá khi khách chưa nêu mục tiêu (KB-005).
- Không tự ý hứa khuyến mãi.
