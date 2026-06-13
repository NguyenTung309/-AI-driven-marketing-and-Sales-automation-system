---
phase: requirements
title: Agent Business-Flow Gaps — Requirements & Problem Understanding
feature: agent-flow-gaps
date: 2026-06-13
status: in-review
---

# Agent Business-Flow Gaps — Requirements & Problem Understanding

> Source: 8-agent fan-out audit of [PhanTich_User_PainPoint_AI_Agent.docx](../../PhanTich_User_PainPoint_AI_Agent.md.docx) vs codebase (2026-06-13). Result: **9 covered · 13 partial · 3 missing**. This feature closes the **3 missing + 7 actionable partials** (10 items). By-design partials (Pancake unified broker) and creds-blocked items (native ad-platform connectors, KB Chinese content) are **out of scope** — see Constraints.

## Problem Statement
**What problem are we solving?**

Khách yêu cầu 8 AI-agent / 18 luồng nghiệp vụ. Audit xác nhận khung 8 agent đã có, phần lớn luồng chạy được, nhưng **3 luồng chưa có code** và **7 luồng mới chạy một phần**. Các gap này khiến sản phẩm chưa khớp 100% kỳ vọng khách:

- **Mất lead từ comment:** comment có ý định mua dưới bài đăng không được trả lời tự động + không có DM mời → lead nguội (Chat-2).
- **Bỏ lỡ upsell:** khách sắp chốt không nhận gợi ý bán thêm (gợi ý hiện là chuỗi tĩnh) (SaleAssist-4).
- **Mù đối thủ:** không theo dõi chiến dịch/giá/bài mới của đối thủ (Research-2 — skill có sẵn nhưng orphaned).
- **Lead nóng nằm im:** khách ≥70 điểm không tự giao cho sale + không báo ngay; lead ấm không tự vào drip (Lead-2, Lead-3).
- **Cảnh báo chờ 1 tầng:** chỉ báo 5 phút, thiếu tầng 10 phút → Sales Lead (SaleAssist-3).
- **Báo cáo thiếu ngữ cảnh:** trend chạy sai múi giờ; daily report thiếu so sánh hôm qua/tuần trước (Research-1, Report-1).
- **Báo giá/quảng cáo nửa vời:** PDF báo giá không tự lấy info hội thoại + không hạn 7 ngày + không gửi; tối ưu ads 4h/lần thay vì mỗi giờ, cảnh báo budget reactive (Docs-1, Ads-1).

**Affected users:** Sale/CSKH (Chat-2, SaleAssist, Lead), Trưởng phòng KD (idle tier, hot-lead notify), Marketing (Research-2, Ads-1, Docs-1), Quản lý (Report-1).

**Current workaround:** thao tác thủ công (trả comment tay, theo dõi đối thủ tay, giao lead tay, tự tính delta trên FE).

## Goals & Objectives

### Primary goals
1. **Chat-2 — Comment auto-reply + DM:** phát hiện comment có ý định mua dưới post → trả lời comment (mục tiêu <30s) + gửi DM mời nhắn riêng.
2. **SaleAssist-4 — Upsell:** phát hiện khách sắp chốt (**hybrid**: lead stage='hot' làm cổng + Claude phân tích tín hiệu chốt) → sinh gợi ý upsell theo ngữ cảnh, đẩy cho sale.
3. **Research-2 — Competitor monitor:** quét nguồn đối thủ định kỳ → phát hiện bài/chiến dịch/giá mới → persist + alert; nguồn cấu hình qua **Admin CRUD per-tenant**.
4. **Lead-2 — Auto-assign + notify:** khi lead lên 'hot' (≥70) → tự giao cho **sale rảnh nhất (least-busy)** + push notify ngay.
5. **Lead-3 — Drip auto-enroll:** lead ấm (30–69) tự enroll drip 3–5 tin/7 ngày.
6. **SaleAssist-3 — Idle tier 2:** thêm tầng >10 phút → escalate Sales Lead.
7. **Research-1 — Cron timezone:** trend tuần chạy **7h sáng VN (GMT+7)**, không phải 00:00 UTC.
8. **Report-1 — Delta:** backend tính so sánh hôm qua / tuần trước cho daily summary.
9. **Docs-1 — Quote hoàn chỉnh:** auto-extract info khách từ hội thoại vào template + link tải hạn 7 ngày + gửi (Zalo/email, config-gated).
10. **Ads-1 — Hourly + proactive budget:** chạy mỗi giờ + tự tính spend/budget ratio để cảnh báo 90% (không chỉ chờ webhook).

### Secondary goals
- Giữ nguyên kiến trúc: alert qua **SignalR/in-app** (Telegram đã bỏ); cost/PII rules giữ nguyên.
- Tái dùng hạ tầng sẵn có (`INotificationPublisher`, Hangfire, `DripSequenceJob`, `RssCompetitorMonitor`, `IDocumentStorage`, `IClaudeChatClient`).

### Non-goals (out of scope)
- **Native ad-platform connectors** (Meta/TikTok lookalike `BuildLookalikeAsync`, native publishers cho Content-3) — **blocked-on-creds**, ops cấp.
- **KB tiếng Trung + bộ 20 câu test/đáp án** (Chat accuracy, Report-4) — blocked, content/ops.
- **Chat đa-kênh native** (Zalo/FB/TikTok SDK riêng) — by-design qua Pancake unified broker.
- Buffer/Later integration cho content scheduling.
- Telegram bất kỳ.

## User Stories & Use Cases

- **Chat-2:** *Là sale*, tôi muốn bot tự trả comment "giá bao nhiêu?" dưới post + nhắn DM mời, để không mất lead ngoài giờ.
- **SaleAssist-4:** *Là sale*, khi khách gần chốt tôi muốn nhận gợi ý upsell phù hợp (gói nâng cao/combo), để tăng giá trị đơn.
- **Research-2:** *Là marketing*, tôi muốn khai báo fanpage/RSS đối thủ và nhận cảnh báo khi họ có bài/giá/chiến dịch mới.
- **Lead-2:** *Là trưởng phòng KD*, khi 1 lead đạt 70 điểm tôi muốn nó tự giao cho sale ít khách nhất + báo ngay, để phản hồi nhanh.
- **Lead-3:** *Là marketing*, tôi muốn lead ấm tự được nuôi dưỡng bằng chuỗi tin, không phải enroll tay.
- **SaleAssist-3:** *Là Sales Lead*, tôi muốn được báo khi 1 hội thoại bị bỏ quên >10 phút (sau khi sale phụ trách đã được nhắc ở mốc 5 phút).
- **Report-1:** *Là quản lý*, tôi muốn báo cáo daily kèm % thay đổi so với hôm qua / tuần trước.
- **Docs-1:** *Là sale*, tôi muốn tạo báo giá tự điền tên/SĐT khách từ hội thoại, có link tải hết hạn sau 7 ngày, gửi qua Zalo/email.

### Edge cases
- Comment trùng / spam / không có ý định mua → không reply (tránh ồn).
- Comment + DM: tránh gửi DM trùng nếu khách đã có hội thoại mở.
- Least-busy: tie-break khi nhiều sale cùng tải; sale offline/inactive bị loại.
- Drip auto-enroll: không enroll lại lead đã enroll; rớt khỏi drip khi lên 'hot' hoặc 'lost'.
- Idle tier-2: không bắn nếu hội thoại đã được nhận/đóng giữa mốc 5–10 phút.
- Competitor: feed lỗi/404 → log, không vỡ job; dedupe bài đã thấy.
- Docs link 7 ngày: truy cập sau hạn → 410/expired.

## Success Criteria

- **Chat-2:** comment ý-định-mua được reply trong vòng poll-interval (mục tiêu <30s) + 1 DM mời; có log/trace; **gated sau spike xác minh Pancake**.
- **SaleAssist-4:** endpoint trả gợi ý upsell **động** (không hardcode) cho lead 'hot' có tín hiệu chốt; có unit test cho hybrid gate.
- **Research-2:** Admin tạo/sửa/xoá nguồn; job quét → `CompetitorPost` persist + alert SignalR; endpoint list kết quả.
- **Lead-2:** lead lên 'hot' → assigned cho least-busy sale + notification record + SignalR push (test: transition tạo assignment + notify).
- **Lead-3:** lead vào dải 30–69 → DripEnrollment tạo tự động (idempotent).
- **SaleAssist-3:** hội thoại idle >10min (chưa nhận) → notify Sales Lead role.
- **Research-1:** job chạy Thứ Hai ~07:00 GMT+7.
- **Report-1:** API trả delta dod/wow cho từng metric.
- **Docs-1:** quote auto-điền ≥ tên+SĐT từ Contact/hội thoại; `ExpiresAt = now+7d` enforce; gửi gated config.
- **Ads-1:** cron mỗi giờ; rule tính spend/budget ≥90% tự bắn alert (không cần webhook).
- Build 0/0 (NuGetAudit + CA gates); unit tests cho từng item ≥80% nhánh logic mới; không phá test hiện có (250 green).

## Constraints & Assumptions

### Technical
- **Build gates:** NuGetAudit + CA analyzers = error; package mới phải clean.
- **Migrations:** DDL-as-source, **không `GO`**; index trên cột ALTER-added phải file riêng (batch riêng).
- **Tenant scoping:** `ITenantOwned` global filter; service singleton-safe qua `IServiceScopeFactory`.
- **PII:** text dẫn xuất (gợi ý upsell, competitor snippet) phải redact, raw purge 30 ngày.
- **Chat-2 dependency:** phụ thuộc Pancake bắn **comment webhook** (post_id, comment-vs-DM) + có **send-DM API**. **Chưa xác minh** → **spike trước** khi cam kết lịch.
- **Cost:** Chat-2 + SaleAssist-4 + competitor dùng Claude → đếm vào ledger + cap $200/tháng.

### Business
- Telegram **không dùng** — alert qua SignalR/in-app (quyết định 2026-06-13).
- Single-org, admin-provisioned (no self-register).

### Assumptions
- `RssCompetitorMonitor` parse RSS đúng; nguồn đối thủ chủ yếu có RSS/fanpage feed.
- Contact entity có đủ tên/SĐT để điền quote (nếu thiếu → để trống, không vỡ).
- Least-busy = đếm hội thoại/lead đang mở của sale trong tenant.

## Questions & Open Items

1. **Chat-2 spike (BLOCKING design):** Pancake có gửi comment event + send-DM không? Nếu không → fallback nào (chỉ reply comment, bỏ DM? hay defer toàn bộ)?
2. **Comment intent:** tái dùng `KeywordIntentClassifier` thêm nhãn `purchase_intent`, hay LLM-classify? (rẻ vs chính xác)
3. **Least-busy metric:** đếm theo lead đang mở, hội thoại đang mở, hay cả hai? Cần định nghĩa "đang mở".
4. **Drip content:** dùng DripSequence mặc định nào cho lead ấm? Cần seed sequence 3–5 bước hay admin tự tạo trước?
5. **Competitor scan tần suất:** hàng ngày hay hàng tuần? Có giới hạn số nguồn/tenant?
6. **Docs gửi:** kênh gửi ưu tiên (Zalo vs email)? Zalo gửi qua Pancake hay SDK riêng? (Pancake-only theo hướng khách)
7. **Ads-1 hourly:** đổi cron AdsRuleEvaluationJob sang hourly có gây tải/chi phí API Meta/TikTok vượt rate-limit không?
8. **Report-1 delta:** tính on-the-fly khi gọi API hay precompute thêm cột vào `kpi_daily`?
