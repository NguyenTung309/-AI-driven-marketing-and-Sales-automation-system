---
phase: monitoring
title: Kịch bản demo luồng mới nhất — Monitoring & Observability
description: Cách theo dõi chất lượng tài liệu demo và buổi demo sau khi sử dụng
feature: latest-demo-scenario
date: 2026-06-23
status: draft
---

# Kịch bản demo luồng mới nhất — Monitoring & Observability

## Key Metrics
**Cần theo dõi điều gì?**

### Metrics về chất lượng demo

- Người demo có chạy hết kịch bản trong 12–15 phút không.
- Có cảnh nào bị bỏ qua vì dữ liệu hoặc credential chưa sẵn sàng không.
- Có claim nào bị stakeholder hỏi lại vì chưa rõ hoặc chưa thuyết phục không.
- Có phần nào bị nhầm giữa “đã triển khai” và “đang cần credential/dữ liệu thật” không.
- Có câu hỏi nào lặp lại nhiều lần sau demo không.

### Metrics về chất lượng tài liệu

- Số lần tài liệu phải sửa sau rehearsal.
- Số link markdown bị sai hoặc stale.
- Số phần bị phát hiện không khớp với code mới.
- Số câu hỏi FAQ mới cần bổ sung.
- Thời gian người mới cần để đọc tài liệu và tự demo lại.

### Metrics về môi trường demo

- Login có ổn định không.
- Dashboard/inbox/lead/documents/content/analytics load có đủ nhanh không.
- LLM call có thành công và latency chấp nhận được không.
- Public widget/support page có dùng đúng tenant branding không.
- Notification/SignalR có hoạt động nếu demo realtime không.
- Token quota/logs/traces có dữ liệu đủ để trình bày không.

## Monitoring Tools
**Dùng công cụ gì?**

- Checklist trong [docs/demo-latest-flow.md](../../demo-latest-flow.md) để kiểm tra trước buổi demo.
- Checklist trong testing doc để QA xác minh.
- Logs/trace trong chính ClawBot: `/logs`, agent traces, audit logs, token usage.
- Notification center để xác minh alert nội bộ.
- Manual notes sau buổi demo để cập nhật FAQ.
- AI DevKit review commands: `/review-requirements`, `/review-design`.

## Logging Strategy
**Ghi nhận vấn đề như thế nào?**

Không cần log runtime mới cho feature tài liệu. Tuy nhiên, sau mỗi buổi rehearsal hoặc demo thật, nên ghi lại:

- Ngày demo.
- Môi trường dùng: local, staging hoặc production-like.
- Người demo.
- Phần nào chạy live, phần nào dùng dữ liệu chuẩn bị sẵn.
- Câu hỏi của stakeholder.
- Điểm nào tài liệu chưa rõ.
- Bug môi trường nếu có.
- Việc cần làm tiếp theo.

Không ghi vào tài liệu bất kỳ secret, token, API key, số điện thoại thật, email thật hoặc nội dung khách hàng thật.

## Alerts & Notifications
**Khi nào cần cảnh báo hoặc hành động?**

### Critical Alerts

- **Tài liệu mô tả sai trạng thái code:** dừng dùng bản demo đó, sửa tài liệu trước khi demo chính thức.
- **Demo dùng dữ liệu khách thật chưa được phê duyệt:** dừng demo và thay bằng dữ liệu giả hoặc redacted.
- **Credential thật bị lộ trong tài liệu hoặc screenshot:** xoá khỏi tài liệu, rotate secret và thông báo người phụ trách bảo mật.

### Warning Alerts

- **Một phần live integration chưa sẵn sàng:** dùng fallback dữ liệu mẫu và ghi rõ điều kiện vận hành.
- **Kịch bản vượt 15 phút:** rút bớt cảnh phụ, giữ mạch chính.
- **Người nghe hỏi nhiều về một phần chưa rõ:** bổ sung FAQ và lời thoại giải thích.
- **Tài liệu stale sau khi code đổi:** cập nhật [docs/demo-latest-flow.md](../../demo-latest-flow.md) cùng checklist module liên quan.

## Dashboards
**Cần dashboard nào cho demo?**

Không cần dashboard mới cho feature tài liệu. Khi demo ClawBot, dùng các dashboard đã có:

- Dashboard tổng quan: KPI đa kênh.
- Analytics: omnichannel, delta, funnel, forecast, anomaly, agent cost.
- Notification center: hot lead, idle, anomaly, system.
- Logs: task runs, traces, audit.
- Tokens: usage và quota.
- Agents: trạng thái agent và trace.

## Incident Response
**Nếu demo lỗi thì xử lý thế nào?**

### Trước buổi demo

1. QA chạy checklist.
2. Người demo rehearsal.
3. Dev kiểm tra service cần thiết: frontend, API, AgentService, DB, SignalR và LLM nếu demo live.
4. Chuẩn bị fallback bằng dữ liệu đã tạo sẵn.

### Trong buổi demo

1. Nếu một màn lỗi, không debug dài trước stakeholder.
2. Chuyển sang dữ liệu đã chuẩn bị hoặc screenshot/video nếu có.
3. Nói rõ: “Phần này có đường code và UI, môi trường hiện tại đang thiếu credential/live service nên tôi sẽ trình bày bằng dữ liệu đã chuẩn bị.”
4. Ghi lại lỗi để xử lý sau.

### Sau buổi demo

1. Tổng hợp câu hỏi và điểm vướng.
2. Cập nhật FAQ trong [docs/demo-latest-flow.md](../../demo-latest-flow.md).
3. Tạo issue riêng cho lỗi code hoặc môi trường.
4. Nếu stakeholder yêu cầu demo sâu hơn, tách thành bản 30 phút hoặc runbook training.

## Health Checks
**Xác minh hệ thống sẵn sàng demo như thế nào?**

Checklist trước demo:

- [ ] Frontend mở được.
- [ ] API health/live/ready hoạt động.
- [ ] Login thành công bằng tài khoản demo.
- [ ] User có đủ permission.
- [ ] Dashboard có dữ liệu.
- [ ] Inbox có conversation mẫu.
- [ ] Sale Assist có draft hoặc dữ liệu fallback.
- [ ] Leads có stage/score đủ đẹp.
- [ ] Documents có template và generated document.
- [ ] Content queue/calendar có item mẫu.
- [ ] Analytics có KPI, funnel, cost hoặc dữ liệu mẫu.
- [ ] Notifications có unread items.
- [ ] Agent traces/logs có dữ liệu.
- [ ] Token quota hiển thị rõ.
- [ ] LLM provider config không lộ plaintext key.
- [ ] Public widget/support page mở được bằng tenant slug.
- [ ] Orchestration có kế hoạch/trace hoặc fallback rõ.

## Continuous Improvement
**Cải thiện tài liệu sau mỗi lần dùng**

Sau mỗi buổi demo, cập nhật ba phần:

1. **FAQ:** thêm câu hỏi stakeholder đã hỏi thật.
2. **Checklist chuẩn bị:** thêm bước nào từng bị thiếu.
3. **Script:** rút gọn hoặc làm rõ đoạn người demo nói chưa mượt.

Nguyên tắc bảo trì: tài liệu demo phải đi cùng trạng thái code hiện tại. Khi module mới được triển khai hoặc thay đổi lớn, cập nhật [docs/demo-latest-flow.md](../../demo-latest-flow.md) trước khi dùng lại.
