---
phase: deployment
title: Kịch bản demo luồng mới nhất — Deployment Strategy
description: Cách đưa tài liệu demo vào repo và chuẩn bị môi trường demo
feature: latest-demo-scenario
date: 2026-06-23
status: draft
---

# Kịch bản demo luồng mới nhất — Deployment Strategy

## Infrastructure
**Tài liệu và demo sẽ chạy ở đâu?**

Đây là feature tài liệu. Phần “deploy” có hai nghĩa:

1. **Deploy tài liệu vào repo:** các file markdown nằm trong [docs/](../../) và [docs/ai/](../).
2. **Chuẩn bị môi trường để chạy demo:** local, staging hoặc production-like environment có frontend, API, AgentService và các dependency cần thiết.

Tài liệu chính:

- [docs/demo-latest-flow.md](../../demo-latest-flow.md)

Tài liệu AI DevKit:

- [docs/ai/requirements/2026-06-23-feature-latest-demo-scenario.md](../requirements/2026-06-23-feature-latest-demo-scenario.md)
- [docs/ai/design/2026-06-23-feature-latest-demo-scenario.md](../design/2026-06-23-feature-latest-demo-scenario.md)
- [docs/ai/planning/2026-06-23-feature-latest-demo-scenario.md](../planning/2026-06-23-feature-latest-demo-scenario.md)
- [docs/ai/implementation/2026-06-23-feature-latest-demo-scenario.md](../implementation/2026-06-23-feature-latest-demo-scenario.md)
- [docs/ai/testing/2026-06-23-feature-latest-demo-scenario.md](../testing/2026-06-23-feature-latest-demo-scenario.md)
- [docs/ai/deployment/2026-06-23-feature-latest-demo-scenario.md](./2026-06-23-feature-latest-demo-scenario.md)
- [docs/ai/monitoring/2026-06-23-feature-latest-demo-scenario.md](../monitoring/2026-06-23-feature-latest-demo-scenario.md)

## Deployment Pipeline
**Triển khai tài liệu như thế nào?**

### Build Process

Không có build code mới. Tuy nhiên, trước khi merge hoặc dùng tài liệu, cần kiểm tra:

- Markdown không còn placeholder template.
- Link tương đối trong docs trỏ đúng file.
- Mermaid diagram không có syntax quá lạ.
- Nội dung không chứa thông tin nhạy cảm.
- Nội dung không mô tả sai trạng thái code.

### CI/CD Pipeline

Nếu repo có CI kiểm tra markdown/link thì tài liệu sẽ đi qua pipeline hiện có. Nếu chưa có, review thủ công là đủ cho phạm vi này.

Khuyến nghị trước khi dùng demo chính thức:

1. Chạy `/review-requirements`.
2. Chạy `/review-design`.
3. Người demo đọc [docs/demo-latest-flow.md](../../demo-latest-flow.md) và rehearsal.
4. QA chạy checklist trong testing doc.

## Environment Configuration
**Mỗi môi trường cần chuẩn bị gì?**

### Development

- Dùng tài khoản seed hoặc tài khoản admin tạo thủ công.
- Frontend thường chạy qua Vite dev server.
- API và AgentService chạy local.
- Có thể thiếu credential vendor; khi đó dùng dữ liệu mẫu.
- Phù hợp để review tài liệu và chạy thử flow cơ bản.

### Staging

- Nên dùng staging cho demo chính thức.
- Có dữ liệu giả nhưng giống thật.
- Có tenant branding hoàn chỉnh.
- Có LLM provider config hợp lệ nếu muốn demo AI live.
- Có public widget/support page trỏ đúng tenant slug.
- Có generated documents và content queue mẫu.
- Có notifications, traces và token usage mẫu.

### Production

- Chỉ dùng production nếu dữ liệu đã được lọc và có quyền trình bày.
- Không dùng dữ liệu khách thật trong buổi demo nếu chưa được phê duyệt.
- Không tạo thử dữ liệu gây ảnh hưởng vận hành thật.
- Nếu demo production, chỉ dùng read-only path hoặc tenant demo riêng.

## Deployment Steps
**Quy trình phát hành tài liệu**

1. Commit hoặc lưu các file tài liệu mới trong [docs/](../../) và [docs/ai/](../).
2. Chạy review tài liệu bằng `/review-requirements` và `/review-design`.
3. Sửa các điểm review yêu cầu.
4. Nếu chuẩn bị demo live, tạo dữ liệu demo theo checklist.
5. Chạy smoke test các màn chính.
6. Rehearsal 1 lần từ đầu đến cuối.
7. Chốt bản tài liệu sẽ dùng trong buổi demo.
8. Sau demo, cập nhật FAQ hoặc các điểm người nghe hỏi nhiều.

## Database Migrations
**Có migration DB không?**

Không có migration mới trong phạm vi feature tài liệu này.

Nếu sau này muốn tạo seed demo riêng, cần tách thành yêu cầu mới. Khi đó phải tuân thủ quy tắc migration của repo:

- Mỗi file migration chạy như một `SqlCommand`.
- Không dùng `GO`.
- Index trên cột vừa `ALTER` phải tách file riêng.
- Không dùng dữ liệu khách thật làm seed demo.

## Secrets Management
**Xử lý secret khi demo như thế nào?**

- Không ghi API key, token, password hoặc secret thật vào tài liệu markdown.
- LLM provider key chỉ cấu hình qua UI/API tương ứng, không paste vào docs.
- Pancake access token, webhook secret, SMTP, MinIO, Meta/TikTok credentials không được đưa vào screenshot hoặc tài liệu.
- Nếu cần hiển thị màn LLM provider config, chỉ hiển thị trạng thái masked key như `hasApiKey` hoặc UI tương đương.

## Rollback Plan
**Nếu tài liệu sai thì xử lý ra sao?**

- Nếu sai nội dung nhỏ: sửa file markdown và ghi rõ thay đổi trong PR/commit message.
- Nếu sai claim nghiêm trọng về tính năng: dừng dùng bản demo đó, sửa lại claim theo code thật, rồi review lại.
- Nếu demo script quá dài: rút bớt cảnh phụ, giữ mạch chính dashboard → inbox → sale assist → lead → documents → analytics/ops.
- Nếu môi trường không sẵn sàng: chuyển sang demo có dữ liệu chuẩn bị sẵn và nói rõ phần live integration cần credential.

## Release Communication
**Thông báo cho ai?**

- PM / Tech Lead: xác nhận tài liệu bám đúng phạm vi.
- Người demo: đọc và rehearsal theo tài liệu chính.
- QA: chạy checklist môi trường.
- Dev: standby nếu demo live dùng staging/local.
- Stakeholder: nhận bản tóm tắt mục tiêu demo, không cần nhận toàn bộ tài liệu kỹ thuật nếu không cần.
