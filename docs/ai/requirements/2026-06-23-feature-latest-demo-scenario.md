---
phase: requirements
title: Kịch bản demo luồng mới nhất — Requirements & Problem Understanding
description: Làm rõ yêu cầu xây dựng lại kịch bản demo theo đúng code ClawBot hiện tại
feature: latest-demo-scenario
date: 2026-06-23
status: draft
---

# Kịch bản demo luồng mới nhất — Requirements & Problem Understanding

> Mục tiêu của tài liệu này là xác định rõ yêu cầu viết lại kịch bản demo ClawBot theo đúng trạng thái code đã triển khai. Tài liệu ưu tiên tính dễ hiểu cho ban giám đốc, nhà đầu tư và người ra quyết định, nhưng vẫn giữ các ghi chú kỹ thuật cần thiết để tránh demo vượt quá năng lực thật của hệ thống.

## Problem Statement
**Chúng ta đang giải quyết vấn đề gì?**

Các tài liệu cũ mô tả một tầm nhìn rất rộng của ClawBot: bán hàng đa nền tảng, 8 AI agent, Knowledge Base tiếng Trung, Sale Assist, Lead Scoring, Content, Document, Ads và Analytics. Tuy nhiên, sau nhiều vòng triển khai, code hiện tại đã có thêm nhiều chức năng mới, đồng thời một số phần trong tài liệu cũ đã không còn khớp với thực tế.

Vấn đề chính là: **khi cần demo sản phẩm, đội ngũ không có một kịch bản demo mới, mạch lạc, dễ trình bày và bám sát đúng code hiện tại**. Nếu dùng tài liệu cũ, người demo có thể nói nhầm các phần chưa có hoặc bỏ sót các phần mới đã làm xong như public web chat widget, notification center, token quota, prompt config, LLM provider config và dynamic orchestration.

Người bị ảnh hưởng:

- **Ban giám đốc / nhà đầu tư:** cần nhìn thấy giá trị kinh doanh, luồng vận hành và mức độ hoàn thiện thật.
- **PM / Tech Lead:** cần một kịch bản thống nhất để kiểm soát phạm vi demo và tránh hứa quá năng lực hiện tại.
- **Sale / vận hành:** cần hiểu cách hệ thống hỗ trợ công việc hằng ngày.
- **Dev / QA:** cần biết demo phụ thuộc vào dữ liệu, endpoint và service nào để chuẩn bị môi trường.

Tình trạng hiện tại:

- [docs/plan.md](../../plan.md) là checklist frontend/backend rất đầy đủ, nhưng thiên về tracking module, không phải kịch bản demo.
- [docs/sale-flow.md](../../sale-flow.md) và [docs/login-flow.md](../../login-flow.md) giải thích sâu từng luồng, nhưng không tạo thành câu chuyện demo tổng thể.
- Một số ghi chú trong tài liệu cũ đã được code bổ sung sau đó, ví dụ: idle alert, hot lead alert, drip sequence, document kit, token quota, prompt configs, LLM provider config và dynamic agent orchestration.

## Goals & Objectives
**Chúng ta muốn đạt được điều gì?**

### Mục tiêu chính

1. Viết lại kịch bản demo tổng thể với tên feature `latest-demo-scenario`.
2. Kịch bản dùng tiếng Việt, dễ hiểu, chi tiết, không rút gọn câu đến mức khó đọc.
3. Demo dành cho **ban giám đốc / nhà đầu tư**, nên phải kể được câu chuyện kinh doanh trước, rồi mới bổ sung bằng chứng kỹ thuật.
4. Kịch bản phải bám sát code hiện tại, ưu tiên các module đã được đánh dấu hoàn thành trong [docs/plan.md](../../plan.md) và [docs/module-checklist.md](../../module-checklist.md).
5. Tài liệu phải phân biệt rõ:
   - **Đã triển khai và có thể demo.**
   - **Có code nhưng cần credential hoặc dữ liệu thật để chứng minh.**
   - **Chưa nên nói như tính năng hoàn chỉnh.**
6. Tạo thêm một tài liệu dễ dùng cho người demo: [docs/demo-latest-flow.md](../../demo-latest-flow.md).

### Mục tiêu phụ

- Tạo bộ tài liệu AI DevKit đủ các pha: requirements, design, planning, implementation, testing, deployment, monitoring.
- Giữ các ghi chú kỹ thuật đủ rõ để QA/dev chuẩn bị được dữ liệu demo.
- Lưu lại quyết định tái sử dụng bằng AI DevKit memory để các lần sau không viết demo lệch code.

### Non-goals

- Không viết code mới cho demo.
- Không sửa UI hoặc backend.
- Không tạo dữ liệu seed mới trong phạm vi tài liệu này.
- Không biến kịch bản demo thành tài liệu API đầy đủ.
- Không hứa các phần bị block bởi credential thật, live Pancake payload, real LLM/embedder hoặc vendor API nếu chưa có bằng chứng chạy thật.

## User Stories & Use Cases
**Người dùng sẽ tương tác với tài liệu này như thế nào?**

- Là **người demo trước ban giám đốc**, tôi muốn có một kịch bản 12–15 phút theo trình tự rõ ràng, để trình bày ClawBot như một sản phẩm hoàn chỉnh thay vì danh sách tính năng rời rạc.
- Là **PM**, tôi muốn tài liệu nêu rõ phần nào đã làm xong và phần nào cần điều kiện vận hành, để không bị scope creep trong buổi demo.
- Là **QA**, tôi muốn biết cần chuẩn bị tài khoản, dữ liệu hội thoại, lead, KB, document template, LLM config và token quota nào trước khi demo.
- Là **Dev**, tôi muốn biết demo dựa trên endpoint và màn hình nào, để có thể kiểm tra nhanh nếu môi trường lỗi.
- Là **Sale / vận hành**, tôi muốn kịch bản dùng ngôn ngữ nghiệp vụ dễ hiểu, để có thể tự diễn giải cho khách hàng hoặc nội bộ.

### Luồng demo trọng tâm

1. Đăng nhập và xác thực người dùng.
2. Xem dashboard tổng quan để mở câu chuyện kinh doanh.
3. Mở Unified Inbox để xử lý hội thoại đa kênh.
4. Dùng Sale Assist để tạo draft, tóm tắt hội thoại và dùng quick reply.
5. Chuyển sang lead pipeline để xem điểm, stage, assign, context và forecast.
6. Tạo tài liệu báo giá hoặc bộ tài liệu onboarding/brochure/slide.
7. Xem Content, Analytics, Notifications và Agent dashboard.
8. Vào khu vực vận hành: prompt config, LLM provider config, token quota, logs và orchestration.
9. Kết thúc bằng public web chat widget / support page để thể hiện vòng khép kín từ khách hàng bên ngoài vào hệ thống nội bộ.

## Success Criteria
**Khi nào coi là hoàn thành?**

- Có đủ 7 tài liệu AI DevKit cho feature `latest-demo-scenario` và không còn placeholder kiểu `[Description]`, `TBD`, `TODO`.
- Có tài liệu [docs/demo-latest-flow.md](../../demo-latest-flow.md) dùng trực tiếp để demo.
- Kịch bản viết bằng tiếng Việt, dễ hiểu, chi tiết, không cố rút gọn câu.
- Kịch bản demo theo mạch kinh doanh, không chỉ liệt kê endpoint.
- Tài liệu có phần “đã có thể demo” và “điều kiện cần chuẩn bị”.
- Tài liệu không mô tả Telegram là kênh alert chính vì quyết định hiện tại là dùng SignalR / in-app notification.
- Tài liệu không nói n8n là lõi code hiện tại; code hiện tại dùng Pancake unified adapter cho luồng omnichannel.
- Tài liệu không nói mọi phần vendor đều đã được verify live nếu thực tế còn cần credential hoặc payload thật.

## Constraints & Assumptions
**Những giới hạn cần tuân thủ**

### Ràng buộc kỹ thuật

- Demo phải bám theo code hiện tại trong `src/api`, `src/agents`, `src/shared` và `src/frontend/clawbot-web`.
- Nhiều tính năng phụ thuộc service ngoài: Anthropic/OpenAI-compatible LLM, Pancake, Qdrant, SMTP, MinIO, Meta/TikTok/Content publisher. Nếu thiếu credential, demo phải dùng dữ liệu đã chuẩn bị hoặc mô tả là “đường code đã có, cần credential để chứng minh live”.
- Dữ liệu khách hàng trong demo phải tránh thông tin thật. Nếu dùng nội dung hội thoại mẫu, phải là dữ liệu giả hoặc đã PII-redacted.
- Phân quyền phải khớp RBAC hiện tại. Một số màn yêu cầu quyền admin hoặc quyền quản lý.

### Ràng buộc kinh doanh

- Demo hướng đến ban giám đốc / nhà đầu tư, nên phần đầu phải nói về hiệu quả vận hành: không bỏ sót khách, sale chăm nhiều khách hơn, lead nóng được ưu tiên, content và tài liệu được tự động hóa, chi phí AI được kiểm soát.
- Không demo theo kiểu “đọc code”. Code chỉ dùng làm bằng chứng khi cần.
- Không trình bày các gap như thất bại. Trình bày chúng như điều kiện vận hành hoặc phạm vi phase tiếp theo.

### Giả định

- Feature name đã chốt: `latest-demo-scenario`.
- Audience đã chốt: ban giám đốc / nhà đầu tư.
- Độ phủ demo đã chốt: full product tour.
- Cách tiếp cận đã chốt: board demo script, 12–15 phút, có ghi chú phần đã triển khai và phần cần chuẩn bị.

## Resolved Decisions
**Các quyết định đã chốt trong review requirements**

1. **Môi trường demo chính là staging.** Staging đủ giống production để thuyết phục ban giám đốc / nhà đầu tư, nhưng vẫn an toàn vì dùng dữ liệu giả và credential demo.
2. **Sau bộ tài liệu này sẽ tạo seed dữ liệu demo riêng.** Seed cần phục vụ các cảnh chính: user, role/permission, tenant branding, conversation, lead, activity, quick reply, document, content, notification, agent trace, token usage và LLM config mẫu. Việc tạo seed là bước tiếp theo sau tài liệu, không trộn vào phần viết docs hiện tại.
3. **Credential xử lý theo mô hình hai lớp.** Nếu có credential thật thì demo live. Nếu thiếu credential hoặc vendor chưa verify, dùng fallback bằng dữ liệu mẫu và nói rõ điều kiện vận hành cần có.
4. **Chưa tạo bản demo 30 phút cho training vận hành.** Hiện chỉ cần bản 12–15 phút cho ban giám đốc / nhà đầu tư. Bản training dài hơn sẽ là yêu cầu riêng khi có buổi đào tạo sale/ops/QA.

## Questions & Open Items
**Cần làm rõ thêm điều gì?**

Không còn câu hỏi nền tảng chặn requirements. Các việc còn lại chuyển sang design/planning:

1. Thiết kế seed dữ liệu demo staging gồm những bảng nào, thứ tự chạy seed ra sao và cách reset dữ liệu sau mỗi lần rehearsal.
2. Chốt danh sách credential nào sẽ dùng live trong staging và credential nào sẽ dùng fallback mẫu.
3. Trong design, cần ghi rõ cách người demo chuyển giữa “live path” và “fallback path” mà không làm người nghe hiểu nhầm.
