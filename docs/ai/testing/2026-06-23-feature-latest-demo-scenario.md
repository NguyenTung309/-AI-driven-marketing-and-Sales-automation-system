---
phase: testing
title: Kịch bản demo luồng mới nhất — Testing Strategy
description: Cách kiểm tra chất lượng tài liệu và môi trường trước khi demo
feature: latest-demo-scenario
date: 2026-06-23
status: draft
---

# Kịch bản demo luồng mới nhất — Testing Strategy

## Test Coverage Goals
**Cần kiểm tra những gì?**

Đây là thay đổi tài liệu, nên không cần unit test code mới. Thay vào đó, cần ba lớp kiểm tra:

1. **Kiểm tra nội dung tài liệu:** không còn placeholder, không mâu thuẫn, không nói quá code thật.
2. **Kiểm tra traceability:** mỗi cảnh demo phải trỏ được về màn hình, API hoặc module đã triển khai.
3. **Kiểm tra môi trường demo:** các bước chính có thể chạy trong local/staging hoặc có fallback rõ ràng nếu thiếu credential.

Mục tiêu chất lượng:

- 100% tài liệu mới không còn placeholder `[Description]`, `TBD`, `TODO`, `Milestone 1`, `Task 1.1` kiểu template.
- 100% claim quan trọng trong [docs/demo-latest-flow.md](../../demo-latest-flow.md) khớp với [docs/plan.md](../../plan.md), [docs/module-checklist.md](../../module-checklist.md) hoặc code hiện tại.
- 100% dữ liệu demo đề xuất là dữ liệu giả hoặc đã PII-redacted.

## Unit Tests
**Kiểm tra từng phần tài liệu**

### Requirements doc

- [x] Có problem statement rõ ràng.
- [x] Có goals, non-goals, user stories, success criteria, constraints và open items.
- [x] Có audience và scope đã chốt.
- [x] Không mô tả đây là feature code mới.

### Design doc

- [x] Có mạch demo tổng thể.
- [x] Có mermaid diagram.
- [x] Có danh sách dữ liệu cần chuẩn bị.
- [x] Có API surface để dev/QA debug.
- [x] Có quyết định trình bày: board demo script, full product tour, SignalR/in-app notification, Pancake unified adapter.

### Planning doc

- [x] Có milestone và task breakdown.
- [x] Có dependencies, timeline, risks và resources.
- [x] Có chỉ dẫn chạy `/review-requirements`, `/review-design`, rồi `/execute-plan` nếu cần.

### Demo doc

- [x] Có checklist trước demo.
- [x] Có kịch bản 12–15 phút.
- [x] Có lời thoại gợi ý theo từng cảnh.
- [x] Có phần “đã triển khai” và “cần credential/dữ liệu thật”.
- [x] Có FAQ cho câu hỏi khó.

## Integration Tests
**Kiểm tra kết nối giữa tài liệu và hệ thống**

Khi có môi trường chạy, kiểm tra các bước sau:

- [ ] Đăng nhập bằng tài khoản admin hoặc sales lead.
- [ ] Mở dashboard và xem được KPI.
- [ ] Mở inbox, chọn một conversation và xem message.
- [ ] Gọi Sale Assist draft hoặc dùng dữ liệu draft đã chuẩn bị nếu thiếu LLM key.
- [ ] Mở lead detail, xem score/stage/context và assign.
- [ ] Tạo hoặc xem generated document.
- [ ] Mở content queue/calendar.
- [ ] Mở analytics, agent-cost và notifications.
- [ ] Mở agent dashboard, prompt config, token quota và logs.
- [ ] Mở LLM provider config và kiểm tra config masked key.
- [ ] Mở orchestration và kiểm tra plan/trace nếu môi trường có LLM config.
- [ ] Mở public widget/support page theo tenant slug.

## End-to-End Tests
**Luồng người dùng cần xác minh**

### E2E-1: Khách hàng vào từ public widget đến inbox

1. Mở `/chat-widget/{tenantSlug}`.
2. Khách nhập thông tin và gửi câu hỏi.
3. Hệ thống tạo hoặc cập nhật contact, lead, conversation và message.
4. Sale mở inbox và thấy conversation mới.
5. Notification hoặc realtime update xuất hiện nếu cấu hình đúng.

### E2E-2: Sale xử lý hội thoại bằng AI

1. Sale mở conversation.
2. Sale xem context panel.
3. Sale bấm tạo draft hoặc summary.
4. Sale dùng quick reply hoặc sửa draft.
5. Sale gửi outbound message.
6. Hệ thống lưu message và cập nhật conversation.

### E2E-3: Lead được chấm điểm và chăm sóc

1. Tạo hoặc chọn lead mẫu.
2. Ghi activity phù hợp scoring rule.
3. Lead tăng điểm và đổi stage nếu đủ ngưỡng.
4. Lead được assign.
5. Hot lead notification hoặc drip enrollment có dữ liệu nếu job/consumer đã chạy.

### E2E-4: Tạo tài liệu bán hàng

1. Chọn contact/lead có dữ liệu.
2. Tạo báo giá hoặc generate kit.
3. Xem generated document.
4. Nếu có storage/email/channel config, gửi tài liệu hoặc mở link.

### E2E-5: Quản trị vận hành AI

1. Mở agent dashboard.
2. Xem status, settings và traces.
3. Mở prompt config và chạy sandbox nếu có LLM config.
4. Mở token quota và xem usage/cap.
5. Mở LLM provider config và xác minh không lộ plaintext key.
6. Mở orchestration và xem kế hoạch/trace nếu môi trường hỗ trợ.

## Test Data
**Cần dữ liệu gì?**

- Một tenant có branding đầy đủ.
- Một admin user và một sale user.
- Ít nhất 3 conversation mẫu: khách hỏi giá, khách muốn học thử, khách im lặng/follow-up.
- Ít nhất 5 lead ở các trạng thái cold, warm, hot, customer/lost nếu có dữ liệu.
- Scoring rules cơ bản: hỏi giá, để lại số điện thoại, đặt lịch học thử, phản hồi lại.
- Quick replies mẫu cho sale.
- KB modules và FAQ mẫu.
- Document templates và generated document mẫu.
- Content briefs/items/calendar mẫu.
- Notifications mẫu.
- Agent sessions/traces mẫu.
- LLM config mẫu hoặc trạng thái “not configured” để demo kiểm soát vận hành.

## Test Reporting & Coverage
**Báo cáo kết quả kiểm tra như thế nào?**

Trước buổi demo, tạo một checklist ngắn:

| Nhóm | Trạng thái | Ghi chú |
|---|---|---|
| Login/RBAC | Pass/Fail | Tài khoản nào dùng demo. |
| Dashboard/Analytics | Pass/Fail | Có dữ liệu KPI chưa. |
| Inbox/Sale Assist | Pass/Fail | Live AI hay dữ liệu mẫu. |
| Leads/Documents | Pass/Fail | Có lead và template chưa. |
| Content/Notifications | Pass/Fail | Có dữ liệu queue/alert chưa. |
| Agents/Prompts/Tokens | Pass/Fail | Có trace và quota chưa. |
| LLM/Orchestration | Pass/Fail | Có key thật hay fallback. |
| Public Widget | Pass/Fail | Tenant slug hoạt động chưa. |

## Manual Testing
**Những gì cần người kiểm tra trực tiếp?**

- Nội dung tiếng Việt có dễ hiểu với người không đọc code hay không.
- Lời thoại có tự nhiên khi đọc thành tiếng hay không.
- Thứ tự demo có mạch kinh doanh hay chỉ là liệt kê màn hình.
- Các phần gap/credential được nói trung thực nhưng không làm mất niềm tin hay không.
- Demo có thể kết thúc trong 12–15 phút hay không.
- Các màn hình quan trọng có dữ liệu đủ đẹp để trình bày hay không.

## Performance Testing
**Kiểm tra tốc độ demo ra sao?**

- Login và load dashboard không quá chậm.
- Inbox/conversation load đủ nhanh để không ngắt mạch trình bày.
- LLM draft nếu chạy live cần kiểm tra trước; nếu latency quá cao, dùng draft đã chuẩn bị sẵn.
- Document generation nếu chạy live cần kiểm tra trước; nếu storage/vendor không ổn định, dùng generated document đã tạo sẵn.
- Orchestration nếu chạy live cần giới hạn goal nhỏ để không tốn thời gian.

## Bug Tracking
**Quản lý vấn đề phát hiện khi review/demo**

- Lỗi tài liệu: sửa trực tiếp trong [docs/demo-latest-flow.md](../../demo-latest-flow.md) và phase docs liên quan.
- Lỗi môi trường: ghi vào checklist demo readiness, không sửa tài liệu nếu không phải claim sai.
- Lỗi code thật: tạo issue hoặc yêu cầu riêng, không trộn vào feature tài liệu này.
- Gap do credential/dữ liệu: ghi là blocker vận hành, không coi là bug code nếu đường code đã có.
