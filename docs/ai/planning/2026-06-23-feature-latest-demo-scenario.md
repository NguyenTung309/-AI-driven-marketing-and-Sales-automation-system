---
phase: planning
title: Kịch bản demo luồng mới nhất — Project Planning & Task Breakdown
description: Kế hoạch hoàn thiện bộ tài liệu demo ClawBot theo code hiện tại
feature: latest-demo-scenario
date: 2026-06-23
status: draft
---

# Kịch bản demo luồng mới nhất — Project Planning & Task Breakdown

## Milestones
**Các mốc chính cần hoàn thành**

- [x] **Milestone 1: Chốt phạm vi demo.** Feature name là `latest-demo-scenario`, audience là ban giám đốc / nhà đầu tư, phạm vi là full product tour.
- [x] **Milestone 2: Đối chiếu code và tài liệu hiện có.** Đã rà [docs/plan.md](../../plan.md), [docs/module-checklist.md](../../module-checklist.md), [docs/sale-flow.md](../../sale-flow.md), [docs/login-flow.md](../../login-flow.md), AI DevKit docs của LLM provider config và dynamic orchestration.
- [x] **Milestone 3: Tạo cấu trúc AI DevKit docs.** Đã chạy `npx ai-devkit@latest docs init-feature latest-demo-scenario`.
- [x] **Milestone 4: Viết requirements, design, planning, implementation, testing, deployment, monitoring.** Các file phase docs được viết bằng tiếng Việt, dễ hiểu, chi tiết.
- [x] **Milestone 5: Viết tài liệu demo chính.** Tạo [docs/demo-latest-flow.md](../../demo-latest-flow.md) để người demo dùng trực tiếp.
- [x] **Milestone 6: Review cùng người dùng.** Đã chạy `/review-requirements` và `/review-design`; requirements/design đã được cập nhật theo quyết định staging, SQL seed và live/fallback path.
- [ ] **Milestone 7: Chuẩn bị dữ liệu demo thực tế.** Nếu cần chạy demo thật, tạo dữ liệu giả, credential và môi trường theo checklist.

## Task Breakdown
**Những việc cụ thể cần làm**

### Phase 1: Nền tảng tài liệu

- [x] Tìm AI DevKit memory liên quan đến demo flow và LLM provider config.
- [x] Kiểm tra các tài liệu hiện có trong thư mục [docs/](../../).
- [x] Đối chiếu với checklist module mới nhất để không viết lại theo tài liệu cũ.
- [x] Chốt tên feature `latest-demo-scenario`.
- [x] Chốt audience chính là ban giám đốc / nhà đầu tư.
- [x] Chốt hướng demo là full product tour, nhưng kể theo câu chuyện kinh doanh.

### Phase 2: Viết AI DevKit phase docs

- [x] Requirements: mô tả vấn đề thiếu kịch bản demo mới, goals, non-goals, user stories, success criteria, constraints và open items.
- [x] Design: thiết kế mạch demo từ public widget / omnichannel vào inbox, Sale Assist, lead, documents, content, analytics, admin, LLM config, token quota và orchestration.
- [x] Planning: chia task để hoàn thiện tài liệu và chuẩn bị demo.
- [x] Implementation: hướng dẫn cách viết và bảo trì tài liệu demo, không thêm code mới.
- [x] Testing: checklist review tài liệu và smoke test môi trường demo.
- [x] Deployment: cách đưa tài liệu vào repo, cách chuẩn bị môi trường demo và cách rollback nếu tài liệu sai.
- [x] Monitoring: cách theo dõi chất lượng demo sau khi dùng trong thực tế.

### Phase 3: Viết tài liệu demo chính

- [x] Viết mục tiêu demo và thông điệp chính.
- [x] Viết checklist chuẩn bị trước demo.
- [x] Viết kịch bản 12–15 phút theo từng cảnh.
- [x] Viết lời thoại gợi ý để người demo có thể nói mạch lạc.
- [x] Viết phần bằng chứng code/module cho từng cảnh.
- [x] Viết phần điều kiện cần credential/dữ liệu thật.
- [x] Viết FAQ cho ban giám đốc / nhà đầu tư.
- [x] Viết checklist sau demo.

### Phase 4: Review và chuyển sang thực thi

- [x] Chạy `/review-requirements` để kiểm tra yêu cầu.
- [x] Chạy `/review-design` để kiểm tra thiết kế mạch demo.
- [x] Nếu cả hai pass, dùng `/execute-plan` nếu muốn chuyển sang việc chuẩn bị dữ liệu demo hoặc kiểm thử môi trường.

### Phase 5: Chuẩn bị dữ liệu demo staging

- [x] Thiết kế SQL seed idempotent cho dữ liệu demo staging trong `deploy/seed` — đã tạo `deploy/seed/demo-latest-flow.sql`, theo convention `@tenant_slug`, `MERGE`, transaction, assertion, không có `GO`.
- [x] Xác định tenant demo, user demo và quyền cần có — seed dùng tenant slug `demo`; dùng user active đầu tiên trong tenant làm owner demo, không tự tạo password/user mới.
- [x] Tạo dữ liệu mẫu cho contact, conversation, message, lead, activity và quick reply — có 3 khách/hội thoại/lead mẫu, PII trong `redacted_content` đã mask.
- [x] Tạo dữ liệu mẫu cho document template/generated document, content item/calendar, notification, agent session/trace và token ledger — seed tạo generated quote nếu có `QUOTE-V1`, content scheduled item, notifications, trace và cost ledger mẫu.
- [x] Tạo LLM config mẫu an toàn, không chứa secret thật, để demo trạng thái configured/not configured — seed tạo inactive config `Demo fallback — no real secret` với placeholder không phải secret thật.
- [ ] Viết hướng dẫn reset/reseed staging trước rehearsal.
- [ ] Smoke test kịch bản demo theo bảng live/fallback trong `docs/demo-latest-flow.md`.

## Dependencies
**Thứ tự phụ thuộc**

1. Phải đọc tài liệu và code hiện có trước khi viết kịch bản, vì mục tiêu là demo theo code thật.
2. Phải chốt audience trước khi chọn tone tài liệu. Audience là ban giám đốc / nhà đầu tư nên tài liệu dùng ngôn ngữ kinh doanh trước, kỹ thuật sau.
3. Phải chốt full product tour trước khi viết outline, vì kịch bản cần bao phủ cả sales workflow và AI operations.
4. Nếu muốn chạy demo thật, phải chuẩn bị dữ liệu mẫu trước: user, lead, conversation, KB, documents, content, agents, notifications, LLM config và token usage.
5. Nếu demo live các tích hợp ngoài, cần credential: Pancake, LLM provider, Qdrant, SMTP, MinIO, content publisher và ads vendor.

## Timeline & Estimates
**Ước lượng công việc**

| Công việc | Ước lượng | Ghi chú |
|---|---:|---|
| Viết bộ AI DevKit docs | 0.5 ngày | Hoàn thành trong phạm vi yêu cầu hiện tại. |
| Viết tài liệu demo chính | 0.5 ngày | Hoàn thành trong phạm vi yêu cầu hiện tại. |
| Review requirements/design | 0.5 ngày | Dùng `/review-requirements` và `/review-design`. |
| Chuẩn bị dữ liệu demo | 0.5–1 ngày | Phụ thuộc có muốn demo live hay dùng dữ liệu có sẵn. |
| Smoke test demo end-to-end | 0.5 ngày | Cần môi trường chạy đủ API, frontend và AgentService. |
| Chạy rehearsal với người demo | 0.5 ngày | Nên làm trước buổi trình bày chính thức. |

## Risks & Mitigation
**Rủi ro và cách giảm thiểu**

| Rủi ro | Tác động | Cách giảm thiểu |
|---|---|---|
| Tài liệu nói quá khả năng code thật | Mất uy tín khi demo | Mỗi claim bám vào [docs/plan.md](../../plan.md), [docs/module-checklist.md](../../module-checklist.md) hoặc code. |
| Demo phụ thuộc credential chưa có | Demo live bị lỗi | Chuẩn bị fallback bằng dữ liệu giả hoặc nói rõ “đường code đã có, cần credential để chứng minh live”. |
| Dữ liệu demo chứa thông tin khách thật | Rủi ro bảo mật và PII | Chỉ dùng dữ liệu giả hoặc đã PII-redacted. |
| Kịch bản quá dài | Người nghe mất tập trung | Giữ bản chính 12–15 phút; phần chi tiết để phụ lục hoặc Q&A. |
| Người demo đi lạc sang kỹ thuật | Audience không thấy ROI | Mỗi cảnh bắt đầu bằng giá trị kinh doanh, sau đó mới nêu bằng chứng kỹ thuật. |
| Tài liệu stale sau khi code đổi | Demo lệch thực tế | Sau mỗi sprint lớn, cập nhật [docs/demo-latest-flow.md](../../demo-latest-flow.md) cùng [docs/plan.md](../../plan.md). |

## Resources Needed
**Cần gì để demo thành công?**

- Một tài khoản admin hoặc sales lead có đủ quyền để đi qua các màn chính.
- Dữ liệu giả cho contact, conversation, lead, activity, document, content, notification và agent trace.
- Tenant branding được cấu hình để demo public widget và tài liệu PDF nhìn giống sản phẩm thật.
- LLM provider config hợp lệ nếu muốn demo live AI draft, prompt sandbox và orchestration.
- Pancake credential hoặc webhook sample nếu muốn chứng minh luồng omnichannel live.
- SMTP/MinIO nếu muốn demo gửi tài liệu hoặc link tài liệu production-like.
- Người demo đã đọc [docs/demo-latest-flow.md](../../demo-latest-flow.md) và chạy rehearsal ít nhất một lần.
