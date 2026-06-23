---
phase: implementation
title: Kịch bản demo luồng mới nhất — Implementation Guide
description: Hướng dẫn viết, bảo trì và sử dụng tài liệu demo theo code ClawBot hiện tại
feature: latest-demo-scenario
date: 2026-06-23
status: draft
---

# Kịch bản demo luồng mới nhất — Implementation Guide

## Development Setup
**Bắt đầu như thế nào?**

Đây là feature tài liệu, không phải feature code. Vì vậy không cần tạo migration, endpoint, component hoặc test code mới. Việc “implementation” ở đây là viết và duy trì tài liệu demo trong thư mục [docs/](../../).

Các bước đã thực hiện:

1. Tìm AI DevKit memory bằng lệnh:
   ```bash
   npx ai-devkit@latest memory search --query "Clawbot latest demo flow scenario implemented code"
   npx ai-devkit@latest memory search --query "llm provider configuration demo flow Clawbot"
   ```
   Kết quả không có memory liên quan, nên tài liệu được xây từ code và docs hiện có.

2. Khởi tạo cấu trúc tài liệu bằng lệnh:
   ```bash
   npx ai-devkit@latest docs init-feature latest-demo-scenario
   ```

3. Đối chiếu với các nguồn sau:
   - [docs/plan.md](../../plan.md)
   - [docs/module-checklist.md](../../module-checklist.md)
   - [docs/sale-flow.md](../../sale-flow.md)
   - [docs/login-flow.md](../../login-flow.md)
   - AI DevKit docs của `llm-provider-config`
   - AI DevKit docs của `dynamic-agent-orchestration`

## Code Structure
**Tài liệu được tổ chức ở đâu?**

Bộ tài liệu gồm hai lớp:

```text
docs/
├── demo-latest-flow.md
└── ai/
    ├── requirements/2026-06-23-feature-latest-demo-scenario.md
    ├── design/2026-06-23-feature-latest-demo-scenario.md
    ├── planning/2026-06-23-feature-latest-demo-scenario.md
    ├── implementation/2026-06-23-feature-latest-demo-scenario.md
    ├── testing/2026-06-23-feature-latest-demo-scenario.md
    ├── deployment/2026-06-23-feature-latest-demo-scenario.md
    └── monitoring/2026-06-23-feature-latest-demo-scenario.md
```

- [docs/demo-latest-flow.md](../../demo-latest-flow.md) là tài liệu chính cho người demo.
- Các file trong [docs/ai/](../) là tài liệu theo quy trình AI DevKit để review yêu cầu, thiết kế, kế hoạch và sẵn sàng triển khai.

## Implementation Notes
**Những chi tiết cần nhớ khi viết tài liệu**

### Nguyên tắc 1: Viết theo code hiện tại, không viết theo tầm nhìn cũ

Tài liệu cũ có nhiều phần đúng ở mức vision nhưng không khớp với code hiện tại. Khi viết demo, ưu tiên checklist mới nhất:

- Frontend web surfaces đã hoàn thành trong [docs/plan.md](../../plan.md).
- Module backend đã hoàn thành trong [docs/module-checklist.md](../../module-checklist.md).
- Các feature mới đã có: notification center, public widget/support page, prompt configs, token quota, LLM provider config, logs/trace và orchestration.

### Nguyên tắc 2: Câu chuyện demo phải bắt đầu từ giá trị kinh doanh

Không mở đầu bằng “hệ thống có endpoint nào”. Mở đầu bằng bài toán:

- Khách nhắn nhiều kênh dễ bị bỏ sót.
- Sale phản hồi chậm và phải tự gõ nhiều lần.
- Lead nóng cần được nhận diện và ưu tiên.
- Marketing cần dữ liệu đa kênh để ra quyết định.
- AI cần được kiểm soát chi phí, model, prompt và trace.

Sau đó mới đi qua các màn hình chứng minh từng điểm.

### Nguyên tắc 3: Không che giấu điều kiện vận hành

Một số phần có code nhưng cần điều kiện bên ngoài để demo live:

- Pancake live payload cần tài khoản và webhook mẫu thật.
- LLM provider cần API key hợp lệ.
- RAG accuracy cần KB thật và embedder/model thật.
- SMTP/MinIO cần credential nếu muốn gửi tài liệu production-like.
- Ads / content publisher cần vendor credential nếu muốn chứng minh publish/live action.

Trong tài liệu demo, những phần này phải được ghi là “cần chuẩn bị” thay vì nói mặc định đã chạy live.

### Nguyên tắc 4: Dùng dữ liệu giả hoặc đã redacted

Demo không dùng thông tin khách hàng thật. Nếu cần hội thoại mẫu, dùng tên giả, số điện thoại giả và nội dung không nhạy cảm. Nếu tài liệu lấy từ log thật, phải PII-redact trước khi đưa vào demo.

## Integration Points
**Tài liệu kết nối các phần nào của hệ thống?**

- **Auth / RBAC:** login, 2FA, permission, user management.
- **Inbox / Pancake / Public widget:** nguồn hội thoại và lead đi vào hệ thống.
- **Sale Assist:** draft, summary, quick reply, upsell, feedback loop.
- **Lead:** scoring, stage, assignment, drip, forecast, context.
- **Docs:** báo giá, brochure, onboarding kit, generated documents.
- **Content / Research:** brief, trend, queue, calendar, repurpose.
- **Analytics / Notifications:** KPI, delta, funnel, cost, anomaly, in-app alert.
- **Agent ops:** agent dashboard, traces, logs, prompt sandbox, token quota, LLM provider config, orchestration.
- **Admin:** users, roles, API keys, Pancake config, tenant branding, audit logs.

## Error Handling
**Khi demo có vấn đề thì xử lý như thế nào?**

- Nếu đăng nhập lỗi: kiểm tra tài khoản seed, mật khẩu, lockout và quyền.
- Nếu API trả 403: kiểm tra role permission seed và quyền của user.
- Nếu Sale Assist không sinh draft: kiểm tra AgentService, LLM provider config và API key.
- Nếu public widget không hiển thị branding: kiểm tra tenant slug và tenant branding.
- Nếu document generate lỗi: kiểm tra template code, storage config và AgentService.
- Nếu notification không realtime: kiểm tra SignalR connection và user group.
- Nếu orchestration không chạy: kiểm tra LLM config cho orchestrator, token quota và permission `orchestration:*`.

Tài liệu demo phải có fallback lời thoại: “Phần này có đường code và UI, nhưng môi trường hiện tại chưa có credential/live service để chạy đến vendor thật.”

## Performance Considerations
**Làm sao để demo mượt?**

- Không demo quá nhiều thao tác CRUD nhỏ. Chỉ chọn các thao tác có giá trị kể chuyện.
- Chuẩn bị sẵn dữ liệu trước để tránh chờ tạo mới quá lâu.
- Nếu gọi LLM live, cần kiểm tra latency trước buổi demo.
- Nếu network hoặc vendor không ổn định, dùng dữ liệu đã sinh sẵn và giải thích đây là kết quả của cùng luồng.
- Kịch bản chính nên giữ trong 12–15 phút. Phần kỹ thuật chi tiết để Q&A.

## Security Notes
**Các điểm bảo mật cần nói đúng**

- Hệ thống dùng JWT, RBAC, permission và 2FA.
- API key và LLM provider key không được trả plaintext về frontend.
- Audit/logs và trace giúp truy vết vận hành.
- Token quota giúp kiểm soát chi phí AI.
- Không demo bằng dữ liệu khách thật.
- Không nói `localStorage` là phương án bảo mật production tối ưu; nếu bị hỏi, nói đây là điểm đã được ghi nhận trong [docs/login-flow.md](../../login-flow.md) và cần cải thiện bằng httpOnly cookie/refresh token khi hardened production.
